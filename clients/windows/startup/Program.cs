using System.Security.Principal;
using System.Text.Json.Nodes;

ApplicationConfiguration.Initialize();
const string taskName="GeekMagicSensorSender";
string root=AppContext.BaseDirectory;
try
{
    var config=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"config.json")))!;
    if(string.IsNullOrWhiteSpace(config["ApiKey"]?.ToString())||config["ApiKey"]?.ToString()=="CHANGE_ME")throw new Exception("Open GeekMagicControl.exe and configure the device first.");
    string sender=Path.Combine(root,"MiniScreen.exe");if(!File.Exists(sender))throw new Exception("MiniScreen.exe is missing. Extract the entire ZIP before installing startup.");
    if(MessageBox.Show("Start the sensor sender automatically when you sign in?\n\nThe task uses administrator privileges for hardware sensors. Your Windows password is not stored.\n\nKeep this extracted folder in its current location.","GeekMagic automatic startup",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
    string user=WindowsIdentity.GetCurrent().Name;
    dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;service.Connect();
    dynamic folder=service.GetFolder("\\");dynamic task=service.NewTask(0);
    task.RegistrationInfo.Description="Sends local CPU, GPU, RAM and system sensor values to your GeekMagic SmallTV.";
    task.Principal.UserId=user;task.Principal.LogonType=3;task.Principal.RunLevel=1;
    task.Settings.Enabled=true;task.Settings.ExecutionTimeLimit="PT0S";task.Settings.DisallowStartIfOnBatteries=false;task.Settings.StopIfGoingOnBatteries=false;task.Settings.MultipleInstances=2;task.Settings.RestartCount=3;task.Settings.RestartInterval="PT1M";
    dynamic trigger=task.Triggers.Create(9);trigger.UserId=user;trigger.Delay="PT15S";
    dynamic action=task.Actions.Create(0);action.Path=sender;action.WorkingDirectory=root;
    dynamic registered=folder.RegisterTaskDefinition(taskName,task,6,user,null,3,null);registered.Run(null);
    var cfg=config.AsObject();cfg["TaskName"]=taskName;File.WriteAllText(Path.Combine(root,"config.json"),cfg.ToJsonString(new(){WriteIndented=true}));
    MessageBox.Show("Automatic startup is installed and the sender has started.\n\nOpen GeekMagicControl.exe to change the display or pause sending.","GeekMagic",MessageBoxButtons.OK,MessageBoxIcon.Information);
}
catch(Exception ex){MessageBox.Show(ex.Message+"\n\nFor CPU/system temperatures, install the PawnIO prerequisite from the official LibreHardwareMonitor project if needed.","Startup setup",MessageBoxButtons.OK,MessageBoxIcon.Information);}
