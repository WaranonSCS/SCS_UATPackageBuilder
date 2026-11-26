namespace Scs.Builder;
internal static class WorkspaceTests
{
    public static int Run()=>RunAsync().GetAwaiter().GetResult();
    static async Task<int> RunAsync()
    {
        var dir=Path.Combine(Path.GetTempPath(),"SCS-source-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);int count=0;
        var ct=CancellationToken.None;
        void Assert(bool yes,string name){if(!yes)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
        async Task<bool> Reject(Func<Task> a){try{await a();return false;}catch(ArgumentException){return true;}catch(InvalidDataException){return true;}catch(InvalidOperationException){return true;}catch(IOException){return true;}}
        try{
            var src=Path.Combine(dir,"fixture-repo");Directory.CreateDirectory(src);
            var target=Path.Combine(dir,"workspaces");Directory.CreateDirectory(target);
            var data=Path.Combine(dir,"data");
            var profile=ConnectionStore.Create("Mock Tenant A",Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),"UAT");
            var other=ConnectionStore.Create("Mock Tenant B",Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),"UAT");
            var original="{\"id\":\""+Guid.NewGuid()+"\",\"name\":\"Mock AL Root\",\"version\":\"1.0.0.0\"}";
            File.WriteAllText(Path.Combine(src,"app.json"),original);Directory.CreateDirectory(Path.Combine(src,"extensions","second"));
            File.WriteAllText(Path.Combine(src,"extensions","second","app.json"),"{\"id\":\""+Guid.NewGuid()+"\",\"name\":\"Mock AL Second\",\"version\":\"1.0.0.0\"}");
            await SourceWorkspace.Git(src,ct,"init","--template=","-b","main");
            await SourceWorkspace.Git(src,ct,"add",".");
            await SourceWorkspace.Git(src,ct,"-c","user.name=OfflineFixture","-c","user.email=fixture@example.invalid","commit","--no-gpg-sign","-m","Local test fixture only");
            var sha=(await SourceWorkspace.Git(src,ct,"rev-parse","HEAD")).Trim();
            Assert(SourceAcquisition.IsPinnedCommit(sha),"fixture full commit");
            Assert(await Reject(()=>SourceWorkspace.FetchCore(profile,src,"main",target,true,ct)),"mutable SHA rejected");
            Assert(await Reject(()=>SourceWorkspace.FetchCore(profile,src,sha,src,true,ct)),"existing Git tree blocked");
            Assert(await Reject(()=>SourceWorkspace.FetchCore(profile,src,new string('0',40),target,true,ct)),"main tip mismatch rejected");
            Assert(await Reject(()=>SourceWorkspace.FetchAsync(profile,src,sha,target,ct)),"local repo forbidden in live path");
            var a=await SourceWorkspace.FetchCore(profile,src,sha,target,true,ct);
            Assert(a.Projects.SequenceEqual(new[]{".","extensions/second"}),"root and nested AL projects discovered");
            Assert(a.Workspace.StartsWith(target+"\\")&&a.Workspace!=src,"isolated workspace");
            Assert((await SourceWorkspace.Git(a.Workspace,ct,"rev-parse","HEAD")).Trim()==sha,"exact pinned HEAD");
            var b=await SourceWorkspace.FetchCore(profile,src,sha,target,true,ct);
            Assert(a.Workspace!=b.Workspace,"repeat fetch uses new directory");
            Assert(await Reject(()=>SourceWorkspace.SaveAsync(data,other,a,".",ct)),"cross tenant selection rejected");
            Assert(await Reject(()=>SourceWorkspace.SaveAsync(data,profile,a,"../fixture-repo",ct)),"unlisted project rejected");
            var file=await SourceWorkspace.SaveAsync(data,profile,a,"extensions/second",ct);
            Assert(File.Exists(file)&&file.Contains(profile.TenantId.ToString())&&File.ReadAllText(file).Contains("SOURCE_SELECTED_UNVERIFIED"),"tenant scoped JSON selection");
            File.WriteAllText(Path.Combine(a.Workspace,"app.json"),original+" ");
            Assert(await Reject(()=>SourceWorkspace.SaveAsync(data,profile,a,".",ct)),"modified checkout rejected");
            Assert(File.ReadAllText(Path.Combine(src,"app.json"))==original&&string.IsNullOrWhiteSpace(await SourceWorkspace.Git(src,ct,"status","--porcelain")),"fixture source unchanged");
            Assert(await Reject(()=>Task.Run(()=>SourceWorkspace.InspectTree("120000 blob abc\tlink\0"))),"symlink rejected");
            Assert(await Reject(()=>Task.Run(()=>SourceWorkspace.InspectTree("160000 commit abc\tsubmodule\0"))),"submodule rejected");
            Assert(await Reject(()=>Task.Run(()=>SourceWorkspace.InspectTree("100644 blob abc\tCON.txt\0"))),"Windows reserved path rejected");
            Assert(await Reject(()=>Task.Run(()=>SourceWorkspace.InspectTree("100644 blob abc\tDir/app.json\0"+"100644 blob def\tdir/file.al\0"))),"case collision rejected");
            Console.WriteLine($"PASS {count}/{count} workspace tests; local fixture only; no network, no AL build");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}finally{try{Directory.Delete(dir,true);}catch{Console.WriteLine("Fixture retained: "+dir);}}
    }
}
