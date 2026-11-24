using System.Text.Json.Nodes;

sealed class ConnectionSetup : Form
{
    readonly TextBox address=new(){Text="http://192.168.1.100/",Dock=DockStyle.Fill};
    readonly TextBox key=new(){UseSystemPasswordChar=true,Dock=DockStyle.Fill};
    public static bool IsRequired()
    {
        try{var cfg=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"config.json")))!;return string.IsNullOrWhiteSpace(cfg["ApiKey"]?.ToString())||cfg["ApiKey"]?.ToString()=="CHANGE_ME";}
        catch{return true;}
    }
    public ConnectionSetup()
    {
        Text="Connect your SmallTV";ClientSize=new Size(510,300);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=7};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,24));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,24));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.Controls.Add(new Label{Text="Enter the address and device key for a SmallTV running Sensor Mod firmware 1.5 or newer.",Dock=DockStyle.Fill},0,0);
        layout.Controls.Add(new Label{Text="Display address",AutoSize=true},0,1);layout.Controls.Add(address,0,2);
        layout.Controls.Add(new Label{Text="Device key (ApiKey from your firmware configuration)",AutoSize=true},0,3);layout.Controls.Add(key,0,4);
        layout.Controls.Add(new Label{Text="This is not your Wi-Fi or Windows password. Settings are saved locally.",Dock=DockStyle.Fill,ForeColor=Color.DimGray},0,5);
        var connect=new Button{Text="Save and open",Dock=DockStyle.Right,Width=150};connect.Click+=(_,_)=>Save();layout.Controls.Add(connect,0,6);AcceptButton=connect;Controls.Add(layout);
    }
    void Save()
    {
        if(!Uri.TryCreate(address.Text.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="http"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment)){MessageBox.Show(this,"Enter an HTTP address, for example http://192.168.1.100/.");return;}
        string value=key.Text.Trim();if(value.Length<12||value.Length>63){MessageBox.Show(this,"Enter the 12–63 character key configured on the device.");return;}
        try
        {
            var cfg=new JsonObject{["Device"]=uri.AbsoluteUri,["ApiKey"]=value,["IntervalSeconds"]=2,["Brightness"]=32,["TaskName"]="GeekMagicSensorSender",["PanelCount"]=4,["Orientation"]="side-by-side",["PanelMetrics"]=new JsonArray("case_temp","cpu_temp","ram_load","gpu_temp")};
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"config.json"),cfg.ToJsonString(new(){WriteIndented=true}));DialogResult=DialogResult.OK;Close();
        }
        catch(Exception ex){MessageBox.Show(this,"Unable to save settings. Extract the package to a writable folder.\n"+ex.Message);}
    }
}
