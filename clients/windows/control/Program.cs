using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

ApplicationConfiguration.Initialize();
if(args.Contains("--check-package"))
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"control.check.json"),System.Text.Json.JsonSerializer.Serialize(new{Runtime=Environment.Version.ToString(),X64=Environment.Is64BitProcess,App="GeekMagicControl"}));
    return;
}
if(ConnectionSetup.IsRequired())
{
    using var setup=new ConnectionSetup();
    if(setup.ShowDialog()!=DialogResult.OK)return;
}
Application.Run(new ControlWindow());

sealed record MetricChoice(string Id,string Name,string Field,string Unit,string BarField)
{
    public static readonly MetricChoice[] All=[
        new("case_temp","Case / system temperature","CaseTemp","°C","CaseTemp"),
        new("cpu_temp","CPU temperature","CpuTemp","°C","CpuLoad"),
        new("ram_load","RAM usage","RamLoad","%","RamLoad"),
        new("gpu_temp","GPU temperature","GpuTemp","°C","GpuLoad"),
        new("cpu_load","CPU usage","CpuLoad","%","CpuLoad"),
        new("gpu_load","GPU usage","GpuLoad","%","GpuLoad"),
        new("ram_used","RAM used","RamUsed","GB","RamLoad"),
        new("ram_total","RAM total","RamTotal","GB","RamLoad")];
    public static MetricChoice Find(string id)=>All.FirstOrDefault(x=>x.Id==id)??All[0];
}

sealed class ControlWindow : Form
{
    readonly string root=AppContext.BaseDirectory;
    readonly HttpClient http=new(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(5)};
    readonly Label status=new(){AutoSize=true};
    readonly Label[] values=new Label[4], details=new Label[4];
    readonly ProgressBar[] bars=new ProgressBar[4];
    readonly TextBox address=new(){Width=270};
    readonly TrackBar brightness=new(){Minimum=5,Maximum=100,Value=32,TickFrequency=10,Width=300,Height=40};
    readonly Label brightnessValue=new(){AutoSize=true};
    readonly NumericUpDown interval=new(){Minimum=1,Maximum=60,Value=2,Width=64};
    readonly ComboBox countChoice=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=60};
    readonly ComboBox orientation=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=150};
    readonly TableLayoutPanel grid=new(){Dock=DockStyle.Fill,Padding=new Padding(0,5,0,8)};
    readonly Button toggle=new(){Width=165,Height=36},apply=new(){Width=165,Height=36,Text="Apply to display"};
    readonly System.Windows.Forms.Timer timer=new(){Interval=2000};
    readonly string[] selected=["case_temp","cpu_temp","ram_load","gpu_temp"];
    JsonObject config=new();JsonNode? latest;
    bool refreshing,changing,sending,ready,layoutEdited,layoutSynced;
    readonly Color foreground=Color.FromArgb(231,239,250),muted=Color.FromArgb(155,174,198),accent=Color.FromArgb(66,210,231);
    public ControlWindow()
    {
        Text="GeekMagic Sensor Mod";ClientSize=new Size(760,730);MinimumSize=new Size(776,769);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(12,19,31);ForeColor=foreground;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        try{config=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"config.json")))!.AsObject();}
        catch{config=new JsonObject{["Device"]="http://192.168.1.100/",["IntervalSeconds"]=2,["ApiKey"]="",["TaskName"]="GeekMagicSensorSender"};}
        address.Text=config["Device"]?.ToString()??"";interval.Value=Math.Clamp(config["IntervalSeconds"]?.GetValue<int>()??2,1,60);
        brightness.Value=Math.Clamp(config["Brightness"]?.GetValue<int>()??32,5,100);
        countChoice.Items.AddRange([1,2,3,4]);countChoice.SelectedIndex=Math.Clamp(config["PanelCount"]?.GetValue<int>()??4,1,4)-1;
        orientation.Items.AddRange(["Side by side","Stacked"]);orientation.SelectedIndex=config["Orientation"]?.ToString()=="stacked"?1:0;
        if(config["PanelMetrics"] is JsonArray saved)for(int i=0;i<Math.Min(4,saved.Count);i++){string? id=saved[i]?.ToString();if(MetricChoice.All.Any(x=>x.Id==id))selected[i]=id!;}
        var content=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=9};
        foreach(int height in new[]{42,28,42,25})content.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        content.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach(int height in new[]{48,58,46,28})content.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        content.Controls.Add(new Label{Text="GeekMagic Sensor Mod",Font=new Font("Segoe UI",22,FontStyle.Bold),AutoSize=true},0,0);
        status.ForeColor=muted;status.Text="Connecting to display…";content.Controls.Add(status,0,1);
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,5,0,0)};
        layout.Controls.Add(new Label{Text="Panels",AutoSize=true,Padding=new Padding(0,5,8,0)});layout.Controls.Add(countChoice);
        layout.Controls.Add(new Label{Text="Two-panel layout",AutoSize=true,Padding=new Padding(18,5,8,0)});layout.Controls.Add(orientation);content.Controls.Add(layout,0,2);
        content.Controls.Add(new Label{Text="Click any panel to choose its value. Apply when the preview looks right.",AutoSize=true,ForeColor=muted},0,3);
        content.Controls.Add(grid,0,4);
        var target=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0)};
        target.Controls.Add(new Label{Text="Display address",AutoSize=true,Padding=new Padding(0,5,6,0)});target.Controls.Add(address);
        target.Controls.Add(new Label{Text="Interval (s)",AutoSize=true,Padding=new Padding(12,5,4,0)});target.Controls.Add(interval);content.Controls.Add(target,0,5);
        var light=new FlowLayoutPanel{Dock=DockStyle.Fill};light.Controls.Add(new Label{Text="Brightness",AutoSize=true,Padding=new Padding(0,12,16,0)});light.Controls.Add(brightness);light.Controls.Add(brightnessValue);brightnessValue.Padding=new Padding(5,12,0,0);brightnessValue.Text=brightness.Value+"%";
        brightness.ValueChanged+=(_,_)=>brightnessValue.Text=brightness.Value+"%";content.Controls.Add(light,0,6);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};actions.Controls.Add(toggle);actions.Controls.Add(apply);
        foreach(var button in new[]{toggle,apply}){button.FlatStyle=FlatStyle.Flat;button.BackColor=Color.FromArgb(35,54,76);button.ForeColor=foreground;}
        content.Controls.Add(actions,0,7);content.Controls.Add(new Label{Text="Closing this window keeps the background sender running.",AutoSize=true,ForeColor=muted},0,8);Controls.Add(content);
        countChoice.SelectedIndexChanged+=(_,_)=>LayoutChanged();orientation.SelectedIndexChanged+=(_,_)=>LayoutChanged();
        ready=true;BuildPanels();
        apply.Click+=async(_,_)=>await ApplyAsync();toggle.Click+=async(_,_)=>await ToggleAsync();timer.Tick+=async(_,_)=>await RefreshAsync();
        Shown+=async(_,_)=>{timer.Start();await RefreshAsync();};FormClosed+=(_,_)=>{timer.Dispose();http.Dispose();};
    }
    int PanelCount=>countChoice.SelectedIndex+1;
    string Orientation=>orientation.SelectedIndex==1?"stacked":"side-by-side";
    void LayoutChanged(){if(!ready)return;layoutEdited=true;BuildPanels();}
    void BuildPanels()
    {
        grid.SuspendLayout();foreach(Control child in grid.Controls.Cast<Control>().ToArray())child.Dispose();grid.Controls.Clear();grid.RowStyles.Clear();grid.ColumnStyles.Clear();
        int n=PanelCount;bool side=n==2&&Orientation=="side-by-side";
        grid.ColumnCount=n>=3||side?2:1;grid.RowCount=n>=3||(n==2&&!side)?2:1;
        for(int i=0;i<grid.ColumnCount;i++)grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/grid.ColumnCount));
        for(int i=0;i<grid.RowCount;i++)grid.RowStyles.Add(new RowStyle(SizeType.Percent,100f/grid.RowCount));
        orientation.Enabled=n==2;
        for(int i=0;i<n;i++)
        {
            int slot=i;var metric=MetricChoice.Find(selected[i]);
            var card=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(22,33,50),Margin=new Padding(5),Padding=new Padding(14),RowCount=4,ColumnCount=1,Cursor=Cursors.Hand};
            card.RowStyles.Add(new RowStyle(SizeType.Absolute,27));card.RowStyles.Add(new RowStyle(SizeType.Percent,100));card.RowStyles.Add(new RowStyle(SizeType.Absolute,24));card.RowStyles.Add(new RowStyle(SizeType.Absolute,12));
            var heading=new Label{Text=metric.Name+" ▾",AutoSize=true,ForeColor=accent,Cursor=Cursors.Hand};card.Controls.Add(heading,0,0);
            values[i]=new Label{Text="—",Font=new Font("Segoe UI",n==1?48:32,FontStyle.Bold),AutoSize=true,Cursor=Cursors.Hand};card.Controls.Add(values[i],0,1);
            details[i]=new Label{Text="Waiting for sensor data",AutoSize=true,ForeColor=muted,Cursor=Cursors.Hand};card.Controls.Add(details[i],0,2);
            bars[i]=new ProgressBar{Dock=DockStyle.Fill,Maximum=100,Style=ProgressBarStyle.Continuous,Cursor=Cursors.Hand};card.Controls.Add(bars[i],0,3);
            foreach(Control item in new Control[]{card,heading,values[i],details[i],bars[i]})item.Click+=(_,_)=>ChooseMetric(slot);
            int col=n>=3?i%2:(side?i:0),row=n>=3?i/2:(side?0:i);if(n==1)row=0;
            grid.Controls.Add(card,col,row);if(n==3&&i==2)grid.SetColumnSpan(card,2);
        }
        grid.ResumeLayout();RenderMetrics();
    }
    void ChooseMetric(int slot)
    {
        var menu=new ContextMenuStrip{BackColor=Color.FromArgb(22,33,50),ForeColor=foreground,ShowCheckMargin=true,ShowImageMargin=false};
        foreach(var metric in MetricChoice.All){var item=new ToolStripMenuItem(metric.Name){Checked=selected[slot]==metric.Id};item.Click+=(_,_)=>{selected[slot]=metric.Id;layoutEdited=true;BuildPanels();};menu.Items.Add(item);}
        // ToolStrip still processes the item after Closed; defer disposal until it returns.
        menu.Closed+=(_,_)=>{if(!IsDisposed)BeginInvoke(new Action(menu.Dispose));};menu.Show(Cursor.Position);
    }
    void RenderMetrics()
    {
        if(latest is null)return;
        float? N(string name)=>latest[name]?.GetValue<float>();
        for(int i=0;i<PanelCount;i++)
        {
            var metric=MetricChoice.Find(selected[i]);var value=N(metric.Field);float? fill=metric.Id=="ram_total"?(value.HasValue?100:null):N(metric.BarField);
            values[i].Text=value.HasValue?value.Value.ToString(metric.Unit=="GB"?"0.0":"0")+" "+metric.Unit:"—";
            bars[i].Value=Math.Clamp((int)Math.Round(fill??0),0,100);
            details[i].Text=!value.HasValue?"Sensor unavailable":metric.Id=="case_temp"?"Motherboard system sensor":metric.Id.StartsWith("ram_")?$"{N("RamUsed"):0.0} / {N("RamTotal"):0} GB":$"Load {fill:0}%";
        }
    }
    string TaskName => config["TaskName"]?.ToString() ?? "Codex-PC-Ekrani-Sayisal";
    Uri BaseUri => new(config["Device"]!.ToString().TrimEnd('/')+"/");
    HttpRequestMessage Request(HttpMethod method,string path,bool basic=false)
    {
        var request=new HttpRequestMessage(method,new Uri(BaseUri,path));var key=config["ApiKey"]?.ToString()??"";
        if(basic)request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:"+key)));
        else request.Headers.Add("X-Api-Key",key);return request;
    }
    async Task SyncLayoutAsync()
    {
        if(layoutSynced||layoutEdited)return;
        using var request=Request(HttpMethod.Get,"api/layout");using var response=await http.SendAsync(request);
        if(!response.IsSuccessStatusCode)return;
        var layout=JsonNode.Parse(await response.Content.ReadAsStringAsync())!;int count=layout["count"]?.GetValue<int>()??4;
        if(count<1||count>4||layout["metrics"] is not JsonArray metrics||metrics.Count!=count)return;
        if(changing||layoutEdited||IsDisposed)return;
        ready=false;countChoice.SelectedIndex=count-1;orientation.SelectedIndex=layout["orientation"]?.ToString()=="stacked"?1:0;
        for(int i=0;i<count;i++){string? id=metrics[i]?.ToString();if(MetricChoice.All.Any(x=>x.Id==id))selected[i]=id!;}
        ready=true;layoutSynced=true;BuildPanels();
    }
    async Task RefreshAsync()
    {
        if(refreshing||changing||IsDisposed)return;refreshing=true;
        try
        {
            using var healthRequest=Request(HttpMethod.Get,"health");using var healthResponse=await http.SendAsync(healthRequest);healthResponse.EnsureSuccessStatusCode();var health=JsonNode.Parse(await healthResponse.Content.ReadAsStringAsync())!;
            using var metricsRequest=Request(HttpMethod.Get,"api/metrics");using var metricsResponse=await http.SendAsync(metricsRequest);metricsResponse.EnsureSuccessStatusCode();latest=JsonNode.Parse(await metricsResponse.Content.ReadAsStringAsync());
            if(IsDisposed)return;RenderMetrics();await SyncLayoutAsync();if(IsDisposed)return;
            var processes=Process.GetProcessesByName("MiniScreen");sending=processes.Length>0;foreach(var process in processes)process.Dispose();
            bool fresh=!(health["stale"]?.GetValue<bool>()??true);
            status.Text=sending&&fresh?$"● Live · {health["ip"]} · updated {DateTime.Now:HH:mm:ss}":"Display online · sender paused or waiting for data";
            if(layoutEdited)status.Text+=" · preview not applied";status.ForeColor=sending&&fresh?accent:muted;toggle.Text=sending?"Pause sending":"Start sending";
        }
        catch(Exception){if(!IsDisposed){status.Text="Display unavailable · check the address and connection";status.ForeColor=Color.Orange;toggle.Text="Start sending";sending=false;}}
        finally{refreshing=false;}
    }
    async Task ApplyAsync()
    {
        if(changing)return;
        if(!Uri.TryCreate(address.Text.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="http"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment)){MessageBox.Show(this,"Enter an HTTP display address, for example http://192.168.1.100/.");return;}
        changing=true;apply.Enabled=false;toggle.Enabled=false;var original=(JsonObject)config.DeepClone();
        try
        {
            config["Device"]=uri.AbsoluteUri;
            var chosen=new JsonArray(selected.Take(PanelCount).Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
            var layout=new JsonObject{["count"]=PanelCount,["orientation"]=Orientation,["metrics"]=chosen};
            using var layoutRequest=Request(HttpMethod.Post,"api/layout");layoutRequest.Content=new StringContent(layout.ToJsonString(),Encoding.UTF8,"application/json");using var layoutResponse=await http.SendAsync(layoutRequest);
            if((int)layoutResponse.StatusCode==404)throw new Exception("This display needs firmware 1.5 or newer for custom layouts.");layoutResponse.EnsureSuccessStatusCode();
            var accepted=JsonNode.Parse(await layoutResponse.Content.ReadAsStringAsync())!;
            if(accepted["count"]?.GetValue<int>()!=PanelCount)throw new Exception("The display did not accept the selected layout.");
            using var request=Request(HttpMethod.Post,"display",true);request.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"brightness",brightness.Value.ToString()}});using var response=await http.SendAsync(request);
            if(!response.IsSuccessStatusCode&&(int)response.StatusCode!=303)throw new Exception("The display did not accept the settings.");
            config["Brightness"]=brightness.Value;config["IntervalSeconds"]=(int)interval.Value;config["PanelCount"]=PanelCount;config["Orientation"]=Orientation;config["PanelMetrics"]=new JsonArray(selected.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
            File.WriteAllText(Path.Combine(root,"config.json"),config.ToJsonString(new(){WriteIndented=true}));layoutEdited=false;layoutSynced=true;
            if(original["IntervalSeconds"]?.GetValue<int>()!=(int)interval.Value||original["Device"]?.ToString()!=uri.AbsoluteUri){File.WriteAllText(Path.Combine(root,"stop.signal"),"");await Task.Delay(3500);await TaskCommand("Stop-ScheduledTask");await TaskCommand("Start-ScheduledTask");}
            status.Text="Layout and settings saved on display";status.ForeColor=accent;
        }
        catch(Exception ex){config=original;MessageBox.Show(this,ex.Message,"Settings",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        finally{changing=false;apply.Enabled=true;toggle.Enabled=true;}
    }
    async Task TaskCommand(string command)
    {
        string escaped=TaskName.Replace("'","''");
        using var process=Process.Start(new ProcessStartInfo("powershell.exe") {Arguments=$"-NoProfile -NonInteractive -Command \"$ErrorActionPreference='Stop'; {command} -TaskName '{escaped}'\"",UseShellExecute=false,CreateNoWindow=true});
        if(process==null)throw new Exception("Unable to open Task Scheduler");await process.WaitForExitAsync();if(process.ExitCode!=0)throw new Exception("The background task is unavailable. Install the startup task first.");
    }
    async Task ToggleAsync()
    {
        if(changing)return;changing=true;toggle.Enabled=false;
        try{if(sending){File.WriteAllText(Path.Combine(root,"stop.signal"),"");await Task.Delay(3500);await TaskCommand("Stop-ScheduledTask");status.Text="Sending paused";sending=false;toggle.Text="Start sending";}else{await TaskCommand("Start-ScheduledTask");status.Text="Starting sender…";}}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Background sender");}
        finally{changing=false;toggle.Enabled=true;}
    }
}
