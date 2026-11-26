namespace Scs.Builder;
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if(args.SequenceEqual(new[]{"--self-test"}))return SelfTest();
        if(args.SequenceEqual(new[]{"--profile-self-test"}))return ProfileTests.Run();
        if(args.SequenceEqual(new[]{"--workspace-self-test"}))return WorkspaceTests.Run();
        ApplicationConfiguration.Initialize();
        var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SCS","UATPackageBuilder","prototype.db");
        using var db=new Registry(data);
        db.SeedMock();
        Application.Run(new MainForm(db,data));
        return 0;
    }
    static int SelfTest()
    {
        var dir=Path.Combine(Path.GetTempPath(),"SCS-UATBuilder-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);var path=Path.Combine(dir,"test.db");
        int count=0;
        void Assert(bool yes,string name){if(!yes)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
        try{
            using(var db=new Registry(path)){
                db.SeedMock();db.SeedMock();
                Assert(db.Rows("projects").Count==1,"idempotent mock project");
                Assert(db.Rows("environments").Count==1,"environment registry");
                Assert(db.Rows("installed_apps").Count==1,"installed package identity");
                Assert(db.Rows("dependencies").Count==1,"dependency registry");
                var checks=Validation.Evaluate(db);
                Assert(checks.Any(c=>c.Name=="PTE scope"&&c.Status=="FAIL"),"DEV scope fails closed");
                Assert(checks.Any(c=>c.Name=="Direct dependency minimum version"&&c.Status=="FAIL"),"missing dependency fails closed");
                Assert(checks.Any(c=>c.Name=="Snapshot evidence"&&c.Status=="UNKNOWN"),"mock cannot qualify release");
                Assert(checks.Any(c=>c.Status=="HOLD"),"release stays HOLD");
            }
            using(var db=new Registry(path))Assert(db.Rows("snapshots").Count==1,"SQLite persistence across reopen");
            Assert(SourceAcquisition.IsAllowedRepository("https://github.com/example/mock-al-project.git"),"GitHub URL allowed");
            Assert(!SourceAcquisition.IsAllowedRepository("https://user@github.com/a/b"),"credential URL rejected");
            Assert(!SourceAcquisition.IsAllowedRepository("file:///C:/repo"),"local protocol rejected");
            Assert(!SourceAcquisition.IsAllowedRepository("https://github.com.evil.test/a/b"),"spoofed host rejected");
            Assert(!SourceAcquisition.IsPinnedCommit("main"),"mutable ref rejected");
            Assert(SourceAcquisition.IsPinnedCommit(new string('a',40)),"full commit accepted");
            Console.WriteLine($"PASS {count}/{count} local tests; no network or real build");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
        finally{Directory.Delete(dir,true);}
    }
}
internal sealed class MainForm : Form
{
    public MainForm(Registry db,string path)
    {
        Text="SCS UAT Package Builder | Source Setup Prototype";Width=1100;Height=720;MinimumSize=new Size(850,500);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",10);
        var title=new Label{Text="SCS UAT Package Builder  |  OFFLINE MOCK  |  RELEASE HOLD",Dock=DockStyle.Top,Height=48,Font=new Font("Segoe UI",13,FontStyle.Bold),Padding=new Padding(12),ForeColor=Color.DarkSlateBlue};
        var footer=new Label{Text="Legacy mock SQLite: "+path+"   |   No live BC API, AL build or deploy enabled.",Dock=DockStyle.Bottom,Height=44,Padding=new Padding(12)};
        var tabs=new TabControl{Dock=DockStyle.Fill};
        var setup=new TabPage("Connection Setup"); setup.Controls.Add(new ConnectionSetup(Path.Combine(Path.GetDirectoryName(path)!,"Data","connections.json"))); tabs.TabPages.Add(setup);
        var source=new TabPage("Source Setup"); source.Controls.Add(new SourceSetup(Path.Combine(Path.GetDirectoryName(path)!,"Data"))); tabs.TabPages.Add(source);
        foreach(var pair in new[]{("Projects","projects"),("Environments","environments"),("Snapshots","snapshots"),("Installed apps","installed_apps"),("Dependencies","dependencies"),("Source history","source_acquisitions"),("Build history","build_history")}){
            var page=new TabPage(pair.Item1);var rows=db.Rows(pair.Item2);var grid=Grid();
            if(rows.Count>0){foreach(var key in rows[0].Keys)grid.Columns.Add(key,key);foreach(var row in rows)grid.Rows.Add(row.Values.Cast<object>().ToArray());}
            page.Controls.Add(grid);tabs.TabPages.Add(page);
        }
        var validation=new TabPage("Validation");var v=Grid();v.Columns.Add("check","Check");v.Columns.Add("status","Status");v.Columns.Add("reason","Reason");
        foreach(var c in Validation.Evaluate(db))v.Rows.Add(c.Name,c.Status,c.Reason);
        validation.Controls.Add(v);tabs.TabPages.Add(validation);tabs.SelectedTab=source;
        Controls.Add(tabs);Controls.Add(footer);Controls.Add(title);
    }
    private static DataGridView Grid()=>new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells,RowHeadersVisible=false,BackgroundColor=Color.White,DefaultCellStyle=new DataGridViewCellStyle{WrapMode=DataGridViewTriState.True}};
}
