namespace Scs.Builder;
internal sealed class SourceSetup:UserControl
{
    readonly string data;
    readonly ConnectionStore connections;
    readonly TextBox repo=new(){Dock=DockStyle.Fill,PlaceholderText="https://github.com/organization/repository.git"},commit=new(){Dock=DockStyle.Fill,PlaceholderText="Approved 40-character main commit SHA"},root=new(){Dock=DockStyle.Fill,ReadOnly=true};
    readonly ComboBox folders=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly Label profile=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},status=new(){Dock=DockStyle.Fill,AutoEllipsis=true};
    readonly Button fetch=new(){Text="Fetch main / discover projects",AutoSize=true},save=new(){Text="Save project selection",AutoSize=true,Enabled=false},browse=new(){Text="Browse...",AutoSize=true},reload=new(){Text="Reload active profile",AutoSize=true};
    SourceResult? result; bool busy; Guid? shownProfileId;
    public SourceSetup(string data)
    {
        this.data=data;connections=new(Path.Combine(data,"connections.json"));Dock=DockStyle.Fill;
        var defaultRoot=Path.Combine(Path.GetDirectoryName(data)!,"Workspace");Directory.CreateDirectory(defaultRoot);root.Text=defaultRoot;
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=9,Padding=new Padding(16)};
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<7;i++)panel.RowStyles.Add(new RowStyle(SizeType.Absolute,i==6?48:40));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,65));
        void Field(int n,string label,Control field){panel.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,n);panel.Controls.Add(field,1,n);}
        Field(0,"Active connection",profile);Field(1,"GitHub repository",repo);Field(2,"Branch",new TextBox{Text="main",ReadOnly=true,Dock=DockStyle.Fill});Field(3,"Approved main SHA",commit);
        var rootLine=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};rootLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));rootLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));rootLine.Controls.Add(root,0,0);rootLine.Controls.Add(browse,1,0);Field(4,"Workspace parent",rootLine);
        Field(5,"AL project folder",folders);var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};buttons.Controls.AddRange([reload,fetch,save]);panel.Controls.Add(buttons,0,6);panel.SetColumnSpan(buttons,2);
        panel.Controls.Add(status,0,7);panel.SetColumnSpan(status,2);
        var note=new Label{Dock=DockStyle.Fill,Text="Fetch uses a new unique child folder. Main tip must match entered SHA. No Git credentials are collected or reused by this prototype; private repo authentication is pending. Build and deployment remain disabled. Saved selection is not release approval."};
        panel.Controls.Add(note,0,8);panel.SetColumnSpan(note,2);Controls.Add(panel);
        void InvalidateResult(){result=null;folders.Items.Clear();save.Enabled=false;}
        repo.TextChanged+=(_,_)=>InvalidateResult();commit.TextChanged+=(_,_)=>InvalidateResult();root.TextChanged+=(_,_)=>InvalidateResult();
        browse.Click+=(_,_)=>{using var dlg=new FolderBrowserDialog{SelectedPath=root.Text,Description="Choose parent for a new isolated source workspace",ShowNewFolderButton=true};if(dlg.ShowDialog()==DialogResult.OK)root.Text=dlg.SelectedPath;};
        reload.Click+=(_,_)=>{InvalidateResult();ReloadProfile(true);};
        fetch.Click+=async (_,_)=>{await Execute(async ()=>{
            InvalidateResult();var active=Active();shownProfileId=active.Id;
            profile.Text=$"{active.Name} | {active.Environment} | {active.TenantId:D}";
            status.Text="Fetching approved main tip into new isolated workspace...";
            var candidate=await SourceWorkspace.FetchAsync(active,repo.Text.Trim(),commit.Text.Trim(),root.Text,CancellationToken.None);
            if(Active()!=active)throw new InvalidOperationException("Active connection changed; selection not accepted.");
            result=candidate;folders.Items.AddRange(candidate.Projects.Cast<object>().ToArray());folders.SelectedIndex=0;
            status.Text=$"SOURCE ACQUIRED (not release-qualified)\r\nCommit: {candidate.Commit}\r\nWorkspace: {candidate.Workspace}\r\nDiscovered AL projects: {candidate.Projects.Length}";
        });};
        save.Click+=async (_,_)=>{await Execute(async ()=>{
            var current=result??throw new InvalidOperationException("Fetch first.");
            var folder=folders.SelectedItem as string??throw new InvalidOperationException("Choose project folder.");
            var active=Active();var path=await SourceWorkspace.SaveAsync(data,active,current,folder,CancellationToken.None);
            status.Text=$"Saved source selection (UNVERIFIED)\r\n{path}\r\nBuild remains HOLD.";
        });};
        VisibleChanged+=(_,_)=>{if(Visible&&!busy)ReloadProfile(false);};ReloadProfile(true);
    }
    ConnectionProfile Active(){var d=connections.Read();return d.Profiles.SingleOrDefault(p=>p.Id==d.ActiveProfileId)??throw new InvalidOperationException("Select a connection profile first.");}
    void ReloadProfile(bool force)
    {
        try{var p=Active();profile.Text=$"{p.Name} | {p.Environment} | {p.TenantId:D}";
            if(force||shownProfileId!=p.Id){
                result=null;folders.Items.Clear();save.Enabled=false;shownProfileId=p.Id;repo.Clear();commit.Clear();
                var path=Path.Combine(data,"Source",p.TenantId.ToString("D"),p.Id.ToString("D"),"selection.json");
                if(File.Exists(path)){
                    var s=System.Text.Json.JsonSerializer.Deserialize<SourceSelection>(File.ReadAllText(path),new System.Text.Json.JsonSerializerOptions{PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase});
                    if(s is null||s.SchemaVersion!=1||s.ProfileId!=p.Id||s.TenantId!=p.TenantId||s.ClientId!=p.ClientId||s.Environment!=p.Environment||s.Branch!="main"||!SourceAcquisition.IsAllowedRepository(s.Repository)||!SourceAcquisition.IsPinnedCommit(s.Commit))throw new InvalidDataException("Saved source profile mismatch.");
                    repo.Text=s.Repository;commit.Text=s.Commit;status.Text=$"Previous selection: {s.ProjectFolder}. Fetch again to verify.";
                }else status.Text="Enter repository and approved main SHA; choose workspace parent.";
            }
            fetch.Enabled=true;status.ForeColor=Color.DarkSlateBlue;
        }catch(Exception ex){result=null;shownProfileId=null;folders.Items.Clear();save.Enabled=false;fetch.Enabled=false;profile.Text="No valid active profile";status.Text="BLOCKED: "+ex.Message;}
    }
    async Task Execute(Func<Task> action)
    {
        if(busy)return;busy=true;fetch.Enabled=save.Enabled=browse.Enabled=reload.Enabled=repo.Enabled=commit.Enabled=folders.Enabled=false;
        try{await action();status.ForeColor=Color.DarkSlateBlue;}
        catch(Exception ex){status.Text="BLOCKED: "+ex.Message;status.ForeColor=Color.DarkRed;}
        finally{busy=false;fetch.Enabled=browse.Enabled=reload.Enabled=repo.Enabled=commit.Enabled=folders.Enabled=true;save.Enabled=result!=null&&folders.SelectedItem!=null;}
    }
}
