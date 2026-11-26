using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Scs.Builder;

internal static class SourceAcquisition
{
    // GitHub HTTPS URL only, no URL credentials, query or arbitrary protocol.
    public static bool IsAllowedRepository(string repo) =>
        Regex.IsMatch(repo, @"\Ahttps://github\.com/[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+(?:\.git)?\z", RegexOptions.CultureInvariant)
        && !repo.EndsWith("/.",StringComparison.Ordinal) && !repo.EndsWith("/..",StringComparison.Ordinal);
    public static bool IsPinnedCommit(string commit)=>Regex.IsMatch(commit,@"\A[0-9a-f]{40}\z",RegexOptions.CultureInvariant);

    // Not exposed in the MVP UI. No credentials supplied; no submodules, hooks or push.
    // Approval evidence must be independently qualified before this is wired to production.
    public static async Task<string> FetchApprovedCommitAsync(string repository,string commit,string isolatedRoot,CancellationToken ct)
    {
        if(!IsAllowedRepository(repository)||!IsPinnedCommit(commit))throw new ArgumentException("Invalid repository or full commit");
        var workspace=Path.Combine(Path.GetFullPath(isolatedRoot),Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        await Git(workspace,ct,"init");
        await Git(workspace,ct,"-c","protocol.file.allow=never","fetch","--no-tags","--no-recurse-submodules",repository,"refs/heads/main:refs/remotes/origin/main");
        await Git(workspace,ct,"merge-base","--is-ancestor",commit,"refs/remotes/origin/main");
        await Git(workspace,ct,"-c","core.hooksPath=NUL","-c","submodule.recurse=false","checkout","--detach",commit);
        var actual=(await Git(workspace,ct,"rev-parse","HEAD")).Trim();
        if(actual!=commit)throw new InvalidOperationException("Commit mismatch");
        return workspace;
    }
    private static async Task<string> Git(string cwd,CancellationToken ct,params string[] args)
    {
        var exe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Git","cmd","git.exe");
        if(!File.Exists(exe))throw new FileNotFoundException("Qualified Git executable missing");
        var psi=new ProcessStartInfo(exe){WorkingDirectory=cwd,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        psi.Environment["GIT_TERMINAL_PROMPT"]="0";psi.Environment["GCM_INTERACTIVE"]="Never";
        foreach(var arg in args)psi.ArgumentList.Add(arg);
        using var p=Process.Start(psi)??throw new InvalidOperationException("Git start failed");
        var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try{await p.WaitForExitAsync(timeout.Token);}catch{try{if(!p.HasExited)p.Kill(true);}catch{}throw;}
        var result=await output;await errors;
        if(p.ExitCode!=0)throw new InvalidOperationException($"Git {args[0]} failed ({p.ExitCode}); workspace retained for inspection.");
        return result;
    }
}
