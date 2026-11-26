using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace Scs.Builder;
internal sealed record ConnectionProfile(Guid Id,string Name,Guid TenantId,Guid ClientId,string Environment);
internal sealed record ConnectionDocument(int SchemaVersion,Guid? ActiveProfileId,List<ConnectionProfile> Profiles);
internal sealed class ConnectionStore
{
    private readonly string path;
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public ConnectionStore(string file){path=Path.GetFullPath(file);Directory.CreateDirectory(Path.GetDirectoryName(path)!);}
    private FileStream Lock()=>new(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    private ConnectionDocument ReadUnsafe()
    {
        if(!File.Exists(path))return new(1,null,[]);
        var doc=JsonSerializer.Deserialize<ConnectionDocument>(File.ReadAllText(path),Options)??throw new InvalidDataException("Empty configuration.");
        Validate(doc);return doc;
    }
    public ConnectionDocument Read(){using var guard=Lock();return ReadUnsafe();}
    public static ConnectionProfile Create(string name,string tenant,string client,string environment)
    {
        if(!Guid.TryParse(tenant,out var t)||!Guid.TryParse(client,out var c))throw new ArgumentException("Tenant ID and Client ID must be GUIDs.");
        var p=new ConnectionProfile(Guid.NewGuid(),name.Trim(),t,c,environment.Trim());ValidateProfile(p);return p;
    }
    private static void ValidateProfile(ConnectionProfile p)
    {
        if(p.Id==Guid.Empty||p.TenantId==Guid.Empty||p.ClientId==Guid.Empty||string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>100||p.Name.Any(char.IsControl)||string.IsNullOrWhiteSpace(p.Environment)||!Regex.IsMatch(p.Environment,@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,79}\z"))
            throw new InvalidDataException("Invalid profile ID, name or environment. Use a BC environment name, not a URL.");
    }
    private static void Validate(ConnectionDocument doc)
    {
        if(doc.SchemaVersion!=1||doc.Profiles is null)throw new InvalidDataException("Unsupported profile schema.");
        foreach(var p in doc.Profiles){if(p is null)throw new InvalidDataException("Null profile.");ValidateProfile(p);}
        if(doc.Profiles.Select(p=>p.Id).Distinct().Count()!=doc.Profiles.Count||doc.Profiles.GroupBy(p=>(p.TenantId,p.ClientId,p.Environment.ToUpperInvariant())).Any(g=>g.Count()>1))throw new InvalidDataException("Duplicate connection profile.");
        if(doc.ActiveProfileId is Guid id&&!doc.Profiles.Any(p=>p.Id==id))throw new InvalidDataException("Active profile does not exist.");
    }
    private void WriteUnsafe(ConnectionDocument doc)
    {
        Validate(doc);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var fs=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(fs,doc,Options);fs.Flush(true);}
            if(File.Exists(path))File.Replace(temp,path,path+".bak",false);else File.Move(temp,path);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public ConnectionDocument Add(ConnectionProfile profile){using var guard=Lock();var old=ReadUnsafe();var next=old with {Profiles=[..old.Profiles,profile]};WriteUnsafe(next);return next;}
    public ConnectionDocument Activate(Guid id){using var guard=Lock();var old=ReadUnsafe();var next=old with {ActiveProfileId=id};WriteUnsafe(next);return next;}
    public static string Authority(ConnectionProfile p)=>$"https://login.microsoftonline.com/{p.TenantId:D}";
    public static string BrokerRedirect(ConnectionProfile p)=>$"ms-appx-web://Microsoft.AAD.BrokerPlugin/{p.ClientId:D}";
    public static string ApiRoot(ConnectionProfile p)=>$"https://api.businesscentral.dynamics.com/v2.0/{p.TenantId:D}/{Uri.EscapeDataString(p.Environment)}";
    public static string ProfileKey(ConnectionProfile p)=>$"{p.TenantId:D}/{p.ClientId:D}/{p.Id:D}";
}
