using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Scs.Builder;

internal record SourceResult(Guid ProfileId, Guid TenantId, Guid ClientId, string Environment, string Repository, string Commit, string Workspace, string[] Projects);
internal record SourceSelection(int SchemaVersion, Guid ProfileId, Guid TenantId, Guid ClientId, string Environment, string Repository, string Branch, string Commit, string Workspace, string ProjectFolder, DateTimeOffset ObservedUtc, string Status);

internal static class SourceWorkspace
{
    internal static readonly string GitExe=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),"Git","cmd","git.exe");
    public static string ValidateRoot(string input)
    {
        if(string.IsNullOrWhiteSpace(input)||!Path.IsPathFullyQualified(input)||input.StartsWith(@"\\")||input.StartsWith("//"))throw new ArgumentException("Select a local absolute directory.");
        var full=Path.GetFullPath(input);
        if(!Directory.Exists(full)||full.TrimEnd('\\').Equals(Path.GetPathRoot(full)!.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Select an existing non-root directory.");
        for(var d=new DirectoryInfo(full);d!=null;d=d.Parent)
        {
            if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Reparse points are not allowed in workspace path.");
            if(File.Exists(Path.Combine(d.FullName,".git"))||Directory.Exists(Path.Combine(d.FullName,".git")))throw new ArgumentException("Do not select an existing Git working tree.");
        }
        return full;
    }
    internal static async Task<string> Git(string cwd,CancellationToken ct,params string[] args)
    {
        if(!File.Exists(GitExe))throw new FileNotFoundException("Git executable missing.");
        var psi=new ProcessStartInfo(GitExe){WorkingDirectory=cwd,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,StandardOutputEncoding=System.Text.Encoding.UTF8};
        foreach(var key in psi.Environment.Keys.Where(k=>k.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)).ToArray())psi.Environment.Remove(key);
        psi.Environment["GIT_CONFIG_NOSYSTEM"]="1";psi.Environment["GIT_CONFIG_GLOBAL"]="NUL";
        psi.Environment["GIT_TERMINAL_PROMPT"]="0";psi.Environment["GCM_INTERACTIVE"]="Never";
        foreach(var a in new[]{"-c","core.hooksPath=NUL","-c","core.autocrlf=false","-c","core.protectNTFS=true","-c","core.symlinks=false","-c","credential.helper=","-c","http.followRedirects=false","-c","submodule.recurse=false"})psi.ArgumentList.Add(a);
        foreach(var a in args)psi.ArgumentList.Add(a);
        using var p=Process.Start(psi)??throw new InvalidOperationException("Git start failed.");
        var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try{await p.WaitForExitAsync(timeout.Token);}catch{try{if(!p.HasExited)p.Kill(true);}catch{}throw;}
        var result=await output;await error;
        if(p.ExitCode!=0)throw new InvalidOperationException("Git operation failed ("+p.ExitCode+"). Check repository access, commit and network. Workspace retained.");
        return result;
    }
    public static async Task<SourceResult> FetchAsync(ConnectionProfile profile,string repository,string commit,string root,CancellationToken ct)
    {
        if(!SourceAcquisition.IsAllowedRepository(repository))throw new ArgumentException("Use a GitHub HTTPS repository URL without credentials.");
        return await FetchCore(profile,repository,commit,root,false,ct);
    }
    internal static async Task<SourceResult> FetchCore(ConnectionProfile profile,string repo,string commit,string root,bool fixture,CancellationToken ct)
    {
        if(!SourceAcquisition.IsPinnedCommit(commit))throw new ArgumentException("Enter an approved full 40-character lowercase commit SHA.");
        if(!fixture&&!SourceAcquisition.IsAllowedRepository(repo))throw new ArgumentException("Invalid repository.");
        root=ValidateRoot(root);
        var workspace=Path.Combine(root,"SCS-UAT-"+profile.Id.ToString("N")+"-"+Guid.NewGuid().ToString("N"));
        if(Directory.Exists(workspace)||File.Exists(workspace))throw new IOException("Workspace collision.");
        Directory.CreateDirectory(workspace);
        await Git(workspace,ct,"init","--template=");
        await Git(workspace,ct,"-c",fixture?"protocol.file.allow=always":"protocol.file.allow=never","-c","protocol.ext.allow=never","fetch","--no-tags","--no-recurse-submodules",repo,"refs/heads/main:refs/remotes/origin/main");
        var tip=(await Git(workspace,ct,"rev-parse","refs/remotes/origin/main")).Trim();
        if(tip!=commit)throw new InvalidOperationException("Main tip differs from entered commit. Reconfirm approval; no checkout performed.");
        var tree=await Git(workspace,ct,"ls-tree","-r","-z",commit);
        var projects=InspectTree(tree);
        await Git(workspace,ct,"checkout","--detach",commit);
        if((await Git(workspace,ct,"rev-parse","HEAD")).Trim()!=commit)throw new InvalidOperationException("Commit mismatch.");
        if(!string.IsNullOrWhiteSpace(await Git(workspace,ct,"status","--porcelain","--untracked-files=all")))throw new InvalidOperationException("Workspace not clean.");
        foreach(var folder in projects)ValidateProject(workspace,folder);
        return new(profile.Id,profile.TenantId,profile.ClientId,profile.Environment,repo,commit,workspace,projects);
    }
    internal static string[] InspectTree(string tree)
    {
        var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var projects=new List<string>();
        foreach(var entry in tree.Split('\0',StringSplitOptions.RemoveEmptyEntries))
        {
            var tab=entry.IndexOf('\t');if(tab<0)throw new InvalidDataException("Invalid Git tree.");
            var meta=entry[..tab].Split(' ');var path=entry[(tab+1)..];
            if(meta.Length!=3||meta[1]!="blob"||(meta[0]!="100644"&&meta[0]!="100755"))throw new InvalidDataException("Symlinks, submodules and non-regular files are blocked.");
            var parts=path.Split('/');
            foreach(var part in parts)
            {
                if(string.IsNullOrEmpty(part)||part=="."||part==".."||part.EndsWith(' ')||part.EndsWith('.')||part.Equals(".git",StringComparison.OrdinalIgnoreCase)||part.Contains('~')||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase))throw new InvalidDataException("Unsafe Windows path in Git tree.");
            }
            if(!files.Add(path)||dirs.ContainsKey(path))throw new InvalidDataException("Case-insensitive path collision.");
            for(int i=1;i<parts.Length;i++){
                var prefix=string.Join('/',parts.Take(i));
                if(files.Contains(prefix)||(dirs.TryGetValue(prefix,out var existing)&&existing!=prefix))throw new InvalidDataException("Case-insensitive directory collision.");
                dirs[prefix]=prefix;
            }
            if(parts[^1]=="app.json")projects.Add(parts.Length==1?".":string.Join('/',parts.Take(parts.Length-1)));
        }
        if(projects.Count==0)throw new InvalidDataException("No AL app.json found on approved main commit.");
        return projects.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    }
    internal static void ValidateProject(string workspace,string folder)
    {
        var full=Path.GetFullPath(Path.Combine(workspace,folder));
        var root=Path.GetFullPath(workspace).TrimEnd('\\');
        if(full!=root&&!full.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Project outside workspace.");
        for(var d=new DirectoryInfo(full);d!=null;d=d.Parent){
            if(!d.Exists||(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Unsafe project folder.");
            if(d.FullName.TrimEnd('\\').Equals(root,StringComparison.OrdinalIgnoreCase))break;
        }
        var manifest=Path.Combine(full,"app.json");
        if(!File.Exists(manifest)||(File.GetAttributes(manifest)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Manifest missing or unsafe.");
        using var json=JsonDocument.Parse(File.ReadAllText(manifest));var obj=json.RootElement;
        if(!obj.TryGetProperty("id",out var id)||id.ValueKind!=JsonValueKind.String||!Guid.TryParse(id.GetString(),out var appId)||appId==Guid.Empty||
           !obj.TryGetProperty("name",out var name)||name.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(name.GetString()))throw new InvalidDataException("Invalid AL app.json identity.");
    }
    public static async Task<string> SaveAsync(string data,ConnectionProfile active,SourceResult result,string folder,CancellationToken ct)
    {
        if(active.Id!=result.ProfileId||active.TenantId!=result.TenantId||active.ClientId!=result.ClientId||active.Environment!=result.Environment)throw new InvalidOperationException("Active profile changed. Fetch again.");
        if(!result.Projects.Contains(folder,StringComparer.Ordinal))throw new InvalidDataException("Choose a discovered AL project.");
        ValidateProject(result.Workspace,folder);
        if((await Git(result.Workspace,ct,"rev-parse","HEAD")).Trim()!=result.Commit||!string.IsNullOrWhiteSpace(await Git(result.Workspace,ct,"status","--porcelain","--untracked-files=all")))throw new InvalidOperationException("Source changed after fetch.");
        var path=Path.Combine(data,"Source",active.TenantId.ToString("D"),active.Id.ToString("D"),"selection.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var guard=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var item=new SourceSelection(1,active.Id,active.TenantId,active.ClientId,active.Environment,result.Repository,"main",result.Commit,result.Workspace,folder,DateTimeOffset.UtcNow,"SOURCE_SELECTED_UNVERIFIED");
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var fs=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(fs,item,new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase});fs.Flush(true);}
            if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}
        finally{if(File.Exists(temp))File.Delete(temp);}
        return path;
    }
}
