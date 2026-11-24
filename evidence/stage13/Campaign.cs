using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dino2.Core;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Multicylinder;
using Dino2.Core.Thermochemistry;
using Dino2.Core.TwoStroke;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string group = args[0], folder = Path.GetFullPath(args[1]);
Directory.CreateDirectory(folder);
Console.WriteLine($"Campaña {group} | commit {Environment.GetEnvironmentVariable("DINO2_STAGE13_COMMIT") ?? "sin sello"}");
var json = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var jobs = new List<Action>();
void Save(string key, object value) =>
    File.WriteAllText(Path.Combine(folder, key + ".json"), JsonSerializer.Serialize(value, json));
void Batch() => Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 3 }, task => task());
FourStrokeConfiguration Audit(double duration = 40)
{
    var baseConfig = ThermochemicalOptions.EquilibriumExample;
    return baseConfig with
    {
        Thermochemistry = baseConfig.Thermochemistry! with { BurnLaw = BurnLaw.PrescribedWiebe },
        Combustion = baseConfig.Combustion with { DurationDeg = duration },
        Ducts = GasPath.RecoveryExample, Speeds = new(4500, 4500, 1),
        Solver = baseConfig.Solver with { PeriodicTolerance = .0001, MaximumCycles = 125 }
    };
}
void Mono(string key, FourStrokeConfiguration configuration, int mask)
{
    ResearchSwitches.Mask = mask;
    ResearchSwitches.Trace = new(); var result = EngineSimulation.Run(configuration); var throat = ResearchSwitches.Trace; ResearchSwitches.Trace = null;
    Save(key, new { mask, configuration, result, throat });
    var p = result.Points.SingleOrDefault();
    Console.WriteLine(FormattableString.Invariant($"{key}: {p?.BrakeTorqueNm:F5} Nm, VE={p?.VolumetricEfficiency:F5}, Wg={p?.GrossWorkJ:F4}, Wp={p?.PumpingWorkJ:F4}, fuel={p?.FuelMassKg:E5}, residual={p?.Chemistry?.SocResidualFraction:F5}, ms={result.ElapsedMilliseconds:F0} | {result.Failures.FirstOrDefault()}"));
}
ExteriorBoundary Boca(bool mode) => mode ? ExteriorBoundary.PressureReservoir : ExteriorBoundary.HistoricalRiemann;
void Network(string key, int mask, bool outer, double volume, int scale = 1, bool tight = false, double phase = 360, double trunkLength = .38, int exteriorExhaust = -1)
{
    ResearchSwitches.Mask = mask;
    var c = MultiConfiguration.Example(2, Boca(outer));
    DuctGeometry Grid(DuctGeometry d) => d with { Cells = d.Cells * scale };
    var cylinder = c.Cylinder;
    var ducts = cylinder.Ducts!;
    var manifold = c.Manifolds!;
    c = c with
    {
        Cylinder = cylinder with
        {
            Speeds = new(4000, 4000, 1),
            Solver = cylinder.Solver with { MaximumStepDeg = (tight ? .25 : .5) / scale, PeriodicTolerance = tight ? 1e-6 : .0004 / (scale * scale), MaximumCycles = tight ? 140 : 65 },
            Ducts = ducts with { Intake = Grid(ducts.Intake), Exhaust = Grid(ducts.Exhaust),
                Courant = tight ? .20 : ducts.Courant, ExhaustExterior = exteriorExhaust < 0 ? null : Boca(exteriorExhaust == 1) }
        },
        Phases = new(phase),
        Manifolds = new(manifold.Intake with { VolumeM3 = volume,
            Trunk = Grid(manifold.Intake.Trunk with { LengthM = trunkLength,
                Cells = Math.Max(8, (int)Math.Round(trunkLength / .38 * 8)) }) },
            manifold.Exhaust with { Trunk = Grid(manifold.Exhaust.Trunk) })
    };
    var result = MultiSimulator.Run(c);
    Save(key, new { mask, outer, volume, scale, tight, phase, trunkLength, exteriorExhaust, result });
    var p = result.Points.Single();
    Console.WriteLine(FormattableString.Invariant($"{key}: {p.Status}, {p.Metrics?.BrakePowerW:F5} W, VE={p.Metrics?.VolumetricEfficiency:F5}, cyc={p.Cycles}, res={p.PeriodicError:E3}, ms={p.ElapsedMilliseconds:F0} {p.Detail}"));
}
void Two(string key, TwoStrokeConfiguration c)
{
    var clock = System.Diagnostics.Stopwatch.StartNew();
    var result = TwoStrokeSimulator.Sweep(c);
    Save(key, new { c, result });
    var p = result.Runs.Single();
    Console.WriteLine(FormattableString.Invariant($"{key}: {p.Status} T={p.Point?.ShaftTorqueNm:F6} trap={p.Point?.FreshTrappedKg:E5} TE={p.Point?.TrappingEfficiency:F5} periodic={p.Point?.PeriodicError:E3} cycles={p.Convergence?.Cycles} accepted={p.Convergence?.AcceptedSteps} rejected={p.Convergence?.RejectedSteps} wall_ms={clock.Elapsed.TotalMilliseconds:F0} workers_max=3 {p.Failure}"));
}
TwoStrokeConfiguration T2(int rpm, int intakeScale, int exhaustScale) =>
    TwoStrokeConfiguration.RecoveryExample with
    {
        Speeds = new(rpm, rpm, 1),
        IntakeDuct = TwoStrokeConfiguration.RecoveryExample.IntakeDuct with { Cells = 12 * intakeScale },
        Expansion = TwoStrokeConfiguration.RecoveryExample.Expansion with { Cells = 32 * exhaustScale },
        Numerics = TwoStrokeConfiguration.RecoveryExample.Numerics with
        { PeriodicTolerance = 5e-5 / Math.Max(intakeScale, exhaustScale), MaximumStepDeg = .65 / Math.Max(intakeScale, exhaustScale), MaximumCycles = 200 }
    };
switch (group)
{
    case "mono":
        foreach (int mask in Enumerable.Range(0, 4))
            jobs.Add(() => Mono($"mono-{mask}", Audit(), mask));
        break;
    case "four-seconds":
        foreach (double length in new[] { .25, .40, .65, .85 })
        foreach (int rpm in new[] { 3000, 4500, 6000 })
            jobs.Add(() =>
            {
                var a = Audit();
                Mono($"seconds-{length}-{rpm}", a with
                {
                    Speeds = new(rpm, rpm, 1),
                    Combustion = a.Combustion with { DurationDeg = null, DurationS = 40.0 / (6 * 4500) },
                    Ducts = a.Ducts! with { Courant = .20, Exhaust = a.Ducts.Exhaust with { LengthM = length } },
                    Solver = a.Solver with { MaximumStepDeg = .15, PeriodicTolerance = 1e-6 }
                }, 3);
            });
        break;
    case "network":
    case "network-fine":
    case "network-time":
        foreach (double length in new[] { .38, .55 })
        foreach (int scale in group == "network" ? new[] { 1, 2 } : new[] { 4 })
        foreach (double volume in new[] { .0024, .0036 })
            jobs.Add(() => Network($"net-{length}-{scale}-{volume}", 3, true, volume, scale, tight: group == "network-time", trunkLength: length));
        break;
    case "two":
    case "two-fine":
        foreach (int rpm in new[] {4000, 5000, 6000, 7000, 8000, 9000, 10000})
        foreach (int scale in group == "two" ? new[] { 1, 2, 4 } : new[] { 8 })
            jobs.Add(() => Two($"mesh-{rpm}-{scale}-{scale}", T2(rpm, scale, scale)));
        if (group == "two")
        foreach (int rpm in new[] {4000, 9000})
        foreach (var (i, e) in new[] {(1,2), (2,1)})
            jobs.Add(() => Two($"mesh-{rpm}-{i}-{e}", T2(rpm, i, e)));
        break;
    case "two-pairs":
        foreach (var (rpm, trim) in new[] { (5000, .0000001), (5000, .00001), (9000, .040), (9500, .00001) })
            jobs.Add(() =>
            {
                var a = TwoStrokeConfiguration.RecoveryExample with { Speeds = new(rpm, rpm, 1) };
                var expansion = a.Expansion with { DiffuserM = a.Expansion.DiffuserM + trim };
                expansion = expansion with { Cells = (int)Math.Round(a.Expansion.Cells * expansion.LengthM / a.Expansion.LengthM) };
                var b = a with { Expansion = expansion };
                var pa = TwoStrokeSimulator.Simulate(a, rpm, false);
                var pb = TwoStrokeSimulator.Simulate(b, rpm, false);
                var check = TwoStrokeComparison.Verify(a, b, pa, pb);
                Save($"pair-{rpm}-{trim}", new { a, b, pa, pb, check });
                Console.WriteLine($"RPM {rpm}, difusor +{trim:G} m, {check.Verdict}");
            });
        break;
    default: throw new ArgumentException(group);
}
Batch();
