namespace Scs.Builder;
internal static class ProfileTests
{
    public static int Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"SCS-profile-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var file=Path.Combine(root,"connections.json");int count=0;
        void Assert(bool value,string name){if(!value)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
        bool Reject(Action a){try{a();return false;}catch(ArgumentException){return true;}catch(InvalidDataException){return true;}catch(System.Text.Json.JsonException){return true;}catch(IOException){return true;}}
        try {
            var store=new ConnectionStore(file);Assert(store.Read().Profiles.Count==0,"empty store");
            var a=ConnectionStore.Create("Tenant A",Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),"UAT");var b=ConnectionStore.Create("Tenant B",Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),"UAT");
            store.Add(a);store.Add(b);store.Activate(b.Id);var read=new ConnectionStore(file).Read();
            Assert(read.Profiles.Count==2&&read.ActiveProfileId==b.Id,"two tenants and active selection persist");
            Assert(ConnectionStore.ProfileKey(a)!=ConnectionStore.ProfileKey(b),"tenant-specific identity key");
            Assert(ConnectionStore.ApiRoot(a)!=ConnectionStore.ApiRoot(b),"same environment name distinct tenant URLs");
            Assert(ConnectionStore.Authority(a).EndsWith(a.TenantId.ToString()),"tenant-specific authority");
            Assert(ConnectionStore.BrokerRedirect(b).EndsWith(b.ClientId.ToString()),"client-specific WAM redirect");
            var before=File.ReadAllText(file);
            Assert(Reject(()=>store.Add(a with {Id=Guid.NewGuid(),Environment="uat"})),"duplicate connection rejected");
            Assert(File.ReadAllText(file)==before,"duplicate leaves JSON unchanged");
            Assert(Reject(()=>store.Activate(Guid.NewGuid())),"unknown active profile rejected");
            Assert(Reject(()=>ConnectionStore.Create("Invalid","bad",b.ClientId.ToString(),"UAT")),"bad tenant rejected");
            Assert(Reject(()=>ConnectionStore.Create("Invalid",a.TenantId.ToString(),b.ClientId.ToString(),"https://bad.test")),"URL environment rejected");
            using(var l=new FileStream(file+".lock",FileMode.Open,FileAccess.ReadWrite,FileShare.None))Assert(Reject(()=>store.Activate(a.Id)),"concurrent writer blocked");
            Assert(File.Exists(file+".bak"),"previous JSON backup exists");
            File.WriteAllText(file,before.Replace("\"schemaVersion\": 1","\"schemaVersion\": 99"));Assert(Reject(()=>store.Read()),"unknown schema rejected");
            File.WriteAllText(file,before.Replace("\"schemaVersion\": 1","\"schemaVersion\": 1, \"accessToken\": \"mock-not-a-token\""));Assert(Reject(()=>store.Read()),"unexpected credential field rejected");
            Console.WriteLine($"PASS {count}/{count} profile tests; no API or live login");return 0;
        }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}finally{Directory.Delete(root,true);}
    }
}
