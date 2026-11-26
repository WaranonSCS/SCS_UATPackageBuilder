namespace Scs.Builder;
internal sealed class ConnectionSetup : UserControl
{
    private readonly ConnectionStore store;
    private readonly DataGridView table=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,RowHeadersVisible=false};
    private readonly TextBox name=new(){Dock=DockStyle.Fill}, tenant=new(){Dock=DockStyle.Fill}, client=new(){Dock=DockStyle.Fill}, environment=new(){Dock=DockStyle.Fill};
    private readonly Label status=new(){Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft};
    private readonly Button add=new(){Text="Add profile",AutoSize=true}, activate=new(){Text="Select active profile",AutoSize=true}, reload=new(){Text="Reload",AutoSize=true};
    public ConnectionSetup(string path)
    {
        store=new(path);Dock=DockStyle.Fill;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=8,Padding=new Padding(12)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<4;i++)layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        void Field(int row,string title,Control control){layout.Controls.Add(new Label{Text=title,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);layout.Controls.Add(control,1,row);}
        Field(0,"Profile name",name);Field(1,"Tenant ID",tenant);Field(2,"Client ID",client);Field(3,"BC environment",environment);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};buttons.Controls.AddRange([add,activate,reload]);layout.Controls.Add(buttons,0,4);layout.SetColumnSpan(buttons,2);
        foreach(var col in new[]{"Active","Profile","Tenant ID","Client ID","Environment"})table.Columns.Add(col,col);
        layout.Controls.Add(table,0,5);layout.SetColumnSpan(table,2);layout.Controls.Add(status,0,6);layout.SetColumnSpan(status,2);
        var note=new Label{Dock=DockStyle.Fill,Text="Each tenant needs its own authorized public-client registration and delegated permissions. No passwords or tokens are saved. Profiles are configuration only; login and BC API are not enabled.",AutoEllipsis=true};
        layout.Controls.Add(note,0,7);layout.SetColumnSpan(note,2);Controls.Add(layout);
        add.Click+=(_,_)=>Run(()=>{store.Add(ConnectionStore.Create(name.Text,tenant.Text,client.Text,environment.Text));name.Clear();tenant.Clear();client.Clear();environment.Clear();RefreshRows();});
        activate.Click+=(_,_)=>Run(()=>{if(table.SelectedRows.Count!=1||table.SelectedRows[0].Tag is not Guid id)throw new InvalidOperationException("Select exactly one profile.");store.Activate(id);RefreshRows();});
        reload.Click+=(_,_)=>Run(RefreshRows);Run(RefreshRows);
    }
    private void Run(Action action){try{action();}catch(Exception e){status.Text="BLOCKED: "+e.Message;status.ForeColor=Color.DarkRed;}}
    private void RefreshRows()
    {
        var doc=store.Read();table.Rows.Clear();
        foreach(var p in doc.Profiles){int i=table.Rows.Add(doc.ActiveProfileId==p.Id?"Active":"",p.Name,p.TenantId,p.ClientId,p.Environment);table.Rows[i].Tag=p.Id;}
        var active=doc.Profiles.SingleOrDefault(p=>p.Id==doc.ActiveProfileId);
        status.Text=active is null?"No active profile. Add and select a profile.":$"Selected: {active.Name} | {active.Environment} | {ConnectionStore.Authority(active)} | UNVERIFIED";
        status.ForeColor=Color.DarkSlateBlue;
    }
}
