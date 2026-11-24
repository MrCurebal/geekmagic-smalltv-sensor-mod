using System.Text;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

using var mutex = new Mutex(true, @"Local\GeekMagicPcMonitor", out bool owner);
if (!owner) return;
var root = AppContext.BaseDirectory;
var config = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(root, "config.json")))!;
if (string.IsNullOrWhiteSpace(config.ApiKey) || config.ApiKey == "CHANGE_ME") throw new InvalidOperationException("Configure the display address and API key first.");
using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(config.Device), Timeout = TimeSpan.FromSeconds(8) };
var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true };
var stop = Path.Combine(root, "stop.signal");
if (File.Exists(stop)) File.Delete(stop);
bool once = args.Contains("--once");
void Log(string message)
{
    var path = Path.Combine(root, "monitor.log");
    if (File.Exists(path) && new FileInfo(path).Length > 250000) File.WriteAllText(path, "");
    File.AppendAllText(path, $"{DateTime.Now:O} {message}\n");
}
void Update(IHardware hardware) { hardware.Update(); foreach (var sub in hardware.SubHardware) Update(sub); }
IEnumerable<ISensor> Sensors(IHardware hardware) => hardware.Sensors.Concat(hardware.SubHardware.SelectMany(Sensors));
float? Value(IHardware? hardware, SensorType type, params string[] names)
{
    if (hardware is null) return null;
    foreach (var name in names)
    {
        var sensor = Sensors(hardware).FirstOrDefault(s => s.SensorType == type && s.Name == name && s.Value.HasValue);
        if (sensor is not null && float.IsFinite(sensor.Value!.Value)) return sensor.Value;
    }
    return null;
}
float? Temperature(float? value) => value > 0 && value < 125 ? value : null;
try
{
    computer.Open();
    foreach (var hardware in computer.Hardware) Update(hardware);
    await Task.Delay(1500);
    do
    {
        try
        {
            foreach (var hardware in computer.Hardware) Update(hardware);
            var cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            var gpu = computer.Hardware.FirstOrDefault(h => h.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel);
            var board = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
            var memory = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && h.Name == "Total Memory") ?? computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
            var used = Value(memory, SensorType.Data, "Memory Used");
            var available = Value(memory, SensorType.Data, "Memory Available");
            var reading = new Reading(DateTimeOffset.Now,
                Temperature(Value(cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package")), Value(cpu, SensorType.Load, "CPU Total"),
                Temperature(Value(gpu, SensorType.Temperature, "GPU Core")), Value(gpu, SensorType.Load, "GPU Core", "D3D 3D"),
                Value(memory, SensorType.Load, "Memory"), used, used + available,
                Temperature(Value(board, SensorType.Temperature, "System #1", "System")));
            var json = JsonSerializer.Serialize(reading);
            File.WriteAllText(Path.Combine(root, "readings.json"), json);
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/metrics");
            request.Headers.Add("X-Api-Key", config.ApiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var ack = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!ack.RootElement.GetProperty("ok").GetBoolean()) throw new InvalidOperationException("Display rejected the metrics.");
            Log($"OK CPU={reading.CpuTemp} GPU={reading.GpuTemp} RAM={reading.RamLoad} CASE={reading.CaseTemp} bytes={Encoding.UTF8.GetByteCount(json)}");
        }
        catch (Exception exception) { Log("ERROR " + exception.Message); if (once) throw; }
        if (once) break;
        for (int i = 0; i < Math.Clamp(config.IntervalSeconds, 1, 60) * 2 && !File.Exists(stop); i++) await Task.Delay(500);
    } while (!File.Exists(stop));
}
finally { computer.Close(); }

record Settings(string Device, string ApiKey, int IntervalSeconds = 2);
record Reading(DateTimeOffset Time, float? CpuTemp, float? CpuLoad, float? GpuTemp, float? GpuLoad, float? RamLoad, float? RamUsed, float? RamTotal, float? CaseTemp);
