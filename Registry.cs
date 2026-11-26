using System.Runtime.InteropServices;
using System.Text.Json;

namespace Scs.Builder;

// Windows inbox SQLite; all stored values are metadata, never credentials.
internal sealed class Registry : IDisposable
{
    private IntPtr db;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RowCallback(IntPtr context, int count, IntPtr values, IntPtr names);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, RowCallback? callback, IntPtr arg, out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr value);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
    public Registry(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        int rc=sqlite3_open_v2(path,out db,6,IntPtr.Zero);
        if(rc!=0){Dispose();throw new InvalidOperationException($"SQLite open error {rc}");}
        try {
            Exec("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
            Exec("""
                CREATE TABLE IF NOT EXISTS projects(id TEXT PRIMARY KEY, name TEXT NOT NULL, repo TEXT NOT NULL, branch TEXT NOT NULL CHECK(branch='main'), app_id TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS environments(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, name TEXT NOT NULL, bc_version TEXT NOT NULL, target_scope TEXT NOT NULL CHECK(target_scope='PTE'), evidence_source TEXT NOT NULL, observed_utc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS snapshots(id TEXT PRIMARY KEY, environment_id TEXT NOT NULL REFERENCES environments(id), observed_utc TEXT NOT NULL, source TEXT NOT NULL, status TEXT NOT NULL CHECK(status IN ('MOCK','UNVERIFIED','VERIFIED')));
                CREATE TABLE IF NOT EXISTS installed_apps(snapshot_id TEXT NOT NULL REFERENCES snapshots(id), app_id TEXT NOT NULL, package_id TEXT NOT NULL, name TEXT NOT NULL, publisher TEXT NOT NULL, version TEXT NOT NULL, scope TEXT NOT NULL CHECK(scope IN ('DEV','PTE','GLOBAL','UNKNOWN')), PRIMARY KEY(snapshot_id,app_id));
                CREATE TABLE IF NOT EXISTS dependencies(snapshot_id TEXT NOT NULL REFERENCES snapshots(id), project_id TEXT NOT NULL REFERENCES projects(id), app_id TEXT NOT NULL, minimum_version TEXT NOT NULL, direction TEXT NOT NULL CHECK(direction IN ('DIRECT','REVERSE')), evidence TEXT NOT NULL, PRIMARY KEY(snapshot_id,project_id,app_id,direction));
                CREATE TABLE IF NOT EXISTS source_acquisitions(id TEXT PRIMARY KEY,project_id TEXT NOT NULL REFERENCES projects(id),commit_sha TEXT NOT NULL,workspace TEXT NOT NULL,observed_utc TEXT NOT NULL,status TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS build_history(id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id), snapshot_id TEXT NOT NULL REFERENCES snapshots(id), commit_sha TEXT NOT NULL, toolchain_hash TEXT NOT NULL, artifact_hash TEXT NOT NULL, observed_utc TEXT NOT NULL, result TEXT NOT NULL);
                """);
        } catch {Dispose();throw;}
    }
    private static string Q(string value) => "'" + value.Replace("'","''") + "'";
    private void Run(string sql, RowCallback? cb)
    {
        int rc=sqlite3_exec(db,sql,cb,IntPtr.Zero,out var err);
        if(rc!=0){var msg=Marshal.PtrToStringUTF8(err);if(err!=IntPtr.Zero)sqlite3_free(err);throw new InvalidOperationException($"SQLite {rc}: {msg}");}
    }
    private void Exec(string sql)=>Run(sql,null);
    public List<Dictionary<string,string>> Rows(string table)
    {
        if(!new[]{"projects","environments","snapshots","installed_apps","dependencies","source_acquisitions","build_history"}.Contains(table))throw new ArgumentException("Unknown table");
        var rows=new List<Dictionary<string,string>>();
        RowCallback cb=(_,count,values,names)=>{
            var row=new Dictionary<string,string>();
            for(int i=0;i<count;i++)row[Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(names,i*IntPtr.Size))!]=Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values,i*IntPtr.Size))??"";
            rows.Add(row);return 0;
        };
        Run("SELECT * FROM "+table,cb);GC.KeepAlive(cb);return rows;
    }
    public void SeedMock()
    {
        var now=Q(DateTimeOffset.UtcNow.ToString("O"));
        Exec("""
            BEGIN IMMEDIATE;
            INSERT OR IGNORE INTO projects VALUES('mock-project','MOCK AP Voucher','https://github.com/example/mock-ap-voucher.git','main','11111111-1111-4111-8111-111111111111');
            """+$"""
            INSERT OR IGNORE INTO environments VALUES('mock-env','22222222-2222-4222-8222-222222222222','MOCK_UAT','29.0','PTE','LOCAL MOCK ONLY',{now});
            INSERT OR IGNORE INTO snapshots VALUES('mock-snapshot','mock-env',{now},'LOCAL MOCK ONLY','MOCK');
            """+"""
            INSERT OR IGNORE INTO installed_apps VALUES('mock-snapshot','11111111-1111-4111-8111-111111111111','33333333-3333-4333-8333-333333333333','MOCK AP Voucher','MOCK','1.0.0.0','DEV');
            INSERT OR IGNORE INTO dependencies VALUES('mock-snapshot','mock-project','44444444-4444-4444-8444-444444444444','1.0.0.0','DIRECT','MOCK MISSING DEPENDENCY');
            COMMIT;
            """);
    }
    public void Dispose(){if(db!=IntPtr.Zero){sqlite3_close(db);db=IntPtr.Zero;}}
}
internal record Check(string Name,string Status,string Reason);
internal static class Validation
{
    public static IReadOnlyList<Check> Evaluate(Registry registry)
    {
        var projects=registry.Rows("projects");var snapshots=registry.Rows("snapshots");var apps=registry.Rows("installed_apps");var deps=registry.Rows("dependencies");
        if(projects.Count!=1||snapshots.Count!=1)return [new("Registry selection","UNKNOWN","MVP requires exactly one test project and snapshot")];
        var p=projects[0];var s=snapshots[0];
        var installed=apps.Where(a=>a["snapshot_id"]==s["id"]).ToList();
        var target=installed.SingleOrDefault(a=>a["app_id"]==p["app_id"]);
        bool missing=deps.Where(d=>d["snapshot_id"]==s["id"]&&d["project_id"]==p["id"]&&d["direction"]=="DIRECT").Any(d=>!installed.Any(a=>a["app_id"]==d["app_id"]&&Version.TryParse(a["version"],out var av)&&Version.TryParse(d["minimum_version"],out var mv)&&av>=mv));
        return [
            new("Snapshot evidence",s["status"]=="VERIFIED"?"PASS":"UNKNOWN","Mock or unverified metadata cannot authorize release"),
            new("Package identity",target!=null&&Guid.TryParse(target["package_id"],out _)?"PASS":"UNKNOWN","Metadata only; not package hash equivalence"),
            new("PTE scope",target==null?"UNKNOWN":target["scope"]=="PTE"?"PASS":target["scope"]=="DEV"?"FAIL":"UNKNOWN","DEV-to-PTE requires separately qualified transition"),
            new("Direct dependency minimum version",missing?"FAIL":"UNKNOWN",missing?"Missing or incompatible dependency":"Complete dependency graph not established"),
            new("Approved source and compiler","UNKNOWN","Live GitHub approval and toolchain not qualified"),
            new("Release authorization","HOLD","Prototype cannot release or deploy")
        ];
    }
}
