using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;
using Dino2.Core.TwoStroke;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
string destination = args[0];
Directory.CreateDirectory(destination);
using var input = JsonDocument.Parse(File.ReadAllText(args[1]));
var settings = input.RootElement.GetProperty("Configuration").Deserialize<FourStrokeConfiguration>(options)!;
using var process = Process.GetCurrentProcess();

void Save(string label, Func<object> solve)
{
    InterfaceAudit.Reset();
    var clock = Stopwatch.StartNew();
    var cpu = process.TotalProcessorTime;
    object outcome = solve();
    double wallMs = clock.Elapsed.TotalMilliseconds, cpuMs = (process.TotalProcessorTime-cpu).TotalMilliseconds;
    var counts = InterfaceAudit.Snapshot();
    File.WriteAllText(Path.Combine(destination, label+".json"), JsonSerializer.Serialize(new { outcome, counts, wallMs, cpuMs }, options));
    Console.WriteLine($"{label},{cpuMs:F1},{wallMs:F1},{counts.Calls},{counts.Below1E12},{counts.ChokedNear},{counts.WaveErrorMax:R},{counts.Domain},{counts.Numerical}");
}
foreach (int rpm in new[] { 1000, 4000, 4500, 5000, 6000 })
{
    var cfg = settings with { Speeds = new(rpm,rpm,1) };
    Save($"4t-{rpm}", () => EngineSimulation.Run(cfg));
}
var reference = settings with
{
    Speeds = new(4500,4500,1), Ducts = GasPath.RecoveryExample,
    Combustion = settings.Combustion with { DurationDeg = 40 },
    Solver = settings.Solver with { PeriodicTolerance = .0001, MaximumCycles = 125 }
};
Save("4t-reference", () => EngineSimulation.Run(reference));
foreach (int rpm in new[] { 4000, 5000, 8000, 9500 })
{
    var cfg = TwoStrokeConfiguration.RecoveryExample with { Speeds = new(rpm,rpm,1) };
    Save($"2t-{rpm}", () => TwoStrokeSimulator.Sweep(cfg));
}
