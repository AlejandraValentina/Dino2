using System.Diagnostics;
using Dino2.Core.FourStroke;
using Dino2.Core.Numerics;
using Dino2.Storage;

var tests = new List<(string, Action)>();
void Test(string name, Action action) => tests.Add((name, action));
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
void Near(double value, double expected, double tolerance = 1e-8) =>
    Assert(double.IsFinite(value) && Math.Abs(value - expected) <= tolerance, $"Expected {expected:R}, got {value:R}");
var c = FourStrokeConfiguration.Example;
var single = c with { Speeds = new(3000, 3000, 1) };
FourStrokePoint? baseline = null;
Test("Slider-crank endpoints, analytic derivative and displacement", () =>
{
    var g = c.Geometry;
    Near(SliderCrank.At(g, 0).VolumeM3, g.DisplacementM3 / 9, 1e-15);
    Near(SliderCrank.At(g, 180).VolumeM3 - SliderCrank.At(g, 0).VolumeM3, g.DisplacementM3, 1e-15);
    for (double a = 10; a < 720; a += 17)
        Near(SliderCrank.At(g, a).VolumeDerivativeM3PerDeg,
            (SliderCrank.At(g, a + .0001).VolumeM3 - SliderCrank.At(g, a - .0001).VolumeM3) / .0002, 1e-12);
});
Test("NASA sensible energy derivative, inversion and continuity", () =>
{
    foreach (double t in new[] { 250.0, 300, 700, 999, 1000, 1001, 1800, 3500, 5900 })
    {
        Near(AirProperties.Temperature(AirProperties.InternalEnergy(t)), t, 1e-7);
        if (t != 1000) Near((AirProperties.InternalEnergy(t + .001) - AirProperties.InternalEnergy(t - .001)) / .002,
            AirProperties.Cp(t) - AirProperties.R, 2e-6);
        Assert(AirProperties.Gamma(t) > 1 && AirProperties.Gamma(t) < 1.5, "Invalid gamma");
    }
    Near(AirProperties.InternalEnergy(1000 + 1e-7), AirProperties.InternalEnergy(1000 - 1e-7), .001);
    Assert(AirProperties.Cp(1800) > AirProperties.Cp(300), "cp is not temperature dependent");
});
Test("Valve lift periodicity and curtain/throat geometry", () =>
{
    var v = c.IntakeValve;
    Near(ValveFlow.Lift(v, v.OpensDeg), 0);
    Near(ValveFlow.Lift(v, v.ClosesDeg), 0);
    Near(ValveFlow.Lift(v, (v.OpensDeg + v.ClosesDeg) / 2), v.MaximumLiftM);
    Near(ValveFlow.Lift(v, 0), ValveFlow.Lift(v, 720));
    Assert(ValveFlow.EffectiveArea(v, 100) <= v.DischargeCoefficient * v.Count * Math.PI * v.DiameterM * v.DiameterM / 4, "Throat exceeded");
});
Test("Independent sonic mass flux, choking and reverse flow", () =>
{
    const double p = 200000, t = 320, a = .0001;
    double gamma = AirProperties.Gamma(t);
    double sonicT = t * 2 / (gamma + 1);
    double sonicP = p * Math.Pow(2 / (gamma + 1), gamma / (gamma - 1));
    double expected = sonicP / (AirProperties.R * sonicT) * Math.Sqrt(gamma * AirProperties.R * sonicT) * a;
    Near(ValveFlow.MassRate(a, p, t, 50000, t), expected, 1e-12);
    Near(ValveFlow.MassRate(a, p, t, 10000, t), expected, 1e-12);
    Near(ValveFlow.MassRate(a, 50000, t, p, t), -expected, 1e-12);
    Near(ValveFlow.MassRate(a, p, t, p, t), 0);
    Near(ValveFlow.MassRate(0, p, t, 10000, t), 0);
});
Test("Normalized finite Wiebe heat integrates to supplied energy", () =>
{
    double sum = 0;
    const int n = 100000;
    for (int i = 0; i < n; i++) sum += HeatRelease.RatePerDegree(340 + (i + .5) * 40 / n, 340, 40, 2) * 40 / n;
    Near(sum, 1, 1e-9);
    Near(HeatRelease.Fraction(-1, 2), 0);
    Near(HeatRelease.Fraction(1, 2), 1);
});
Test("RK4 independent exponential solution and fourth-order refinement", () =>
{
    double Error(int n)
    {
        double[] y = [1.0], result = new double[1], workspace = new double[5];
        for (int i = 0; i < n; i++)
        {
            RungeKutta.Step((x, s, d) => d[0] = s[0], (double)i / n, y, 1.0 / n, result, workspace);
            y[0] = result[0];
        }
        return Math.Abs(y[0] - Math.E);
    }
    double ratio = Error(10) / Error(20);
    Assert(ratio > 14 && ratio < 18, "RK4 does not converge at fourth order");
});
Test("Closed adiabatic cylinder conservation and independent reversible compression", () =>
{
    var noFlow = single with { IntakeValve = c.IntakeValve with { MaximumLiftM = 0 },
        ExhaustValve = c.ExhaustValve with { MaximumLiftM = 0 }, Thermal = new(450, 0) };
    var equations = new CylinderEquations(noFlow, 3000);
    double[] y = new double[10], next = new double[10], work = new double[50];
    y[0] = .0005; y[1] = y[0] * AirProperties.InternalEnergy(320);
    double initialU = y[1];
    for (int i = 0; i < 3600; i++)
    {
        double angle = 180 + i * .05;
        RungeKutta.Step(equations.Evaluate, angle, y, .05, next, work);
        Array.Copy(next, y, 10);
    }
    Near(y[0], .0005, 1e-15);
    Near(y[1] - initialU + y[3], 0, 1e-9);
    // Entropy integral: ∫cv(T)/T dT = R ln(Vstart/Vend).
    double tf = AirProperties.Temperature(y[1] / y[0]);
    const int steps = 100000;
    double ds = 0, dt = (tf - 320) / steps;
    for (int i = 0; i < steps; i++)
    {
        double t = 320 + (i + .5) * dt;
        ds += (AirProperties.Cp(t) - AirProperties.R) / t * dt;
    }
    Near(ds, AirProperties.R * Math.Log(10), 2e-5);
});
Test("Hohenberg pressure units and friction coefficient units", () =>
{
    Near(LossModels.HeatTransferCoefficient(.0004, 1e5, 400, 8),
        130 * Math.Pow(.0004, -.06) * Math.Pow(400, -.4) * Math.Pow(9.4, .8), 1e-10);
    Near(LossModels.FrictionMep(c.Friction, 5e6, 8), 50000 + .006 * 5e6 + 9000 * 8 + 45 * 64);
});
Test("Periodic firing cycle, mass/energy closure and work decomposition", () =>
{
    baseline = FourStrokeSimulator.Simulate(single, 3000);
    var p = baseline;
    Near(p.NetWorkJ, 458.65001144748067, .001);
    Near(p.BrakePowerW, 9752.096036828396, .1);
    Near(p.VolumetricEfficiency, .8723947406799374, 1e-5);
    Near(p.MassResidualKg, 0, 1e-12);
    Near(p.EnergyResidualJ, 0, 1e-7);
    Near(p.GrossWorkJ + p.PumpingWorkJ, p.NetWorkJ, 1e-10);
    Near(p.HeatReleasedJ, p.FuelMassKg * c.Combustion.LowerHeatingValueJPerKg * c.Combustion.HeatReleaseEfficiency, .01);
    Assert(p.PeriodicError < single.Solver.PeriodicTolerance && p.Cycles >= 4, "Periodicity not reached");
    Assert(p.VolumetricEfficiency > .2 && p.VolumetricEfficiency < 1.2, "Implausible reference filling");
    Assert(p.PumpingWorkJ < 0 && p.FrictionWorkJ > 0 && p.BrakePowerW < p.IndicatedPowerW, "Loss signs wrong");
    Assert(p.Trace.All(s => s.MassKg > 0 && s.FreshMassKg >= -1e-10 && s.FreshMassKg <= s.MassKg + 1e-10), "Mass fraction outside physical interval");
});
Test("Grid/time refinement and independent trace work quadrature", () =>
{
    var a = FourStrokeSimulator.Simulate(single with { Solver = c.Solver with { MaximumStepDeg = 1 } }, 3000);
    var b = baseline ?? FourStrokeSimulator.Simulate(single, 3000);
    var fine = FourStrokeSimulator.Simulate(single with { Solver = c.Solver with { MaximumStepDeg = .25, RelativeTolerance = 2e-8 } }, 3000);
    double e1 = Math.Abs(a.NetWorkJ - fine.NetWorkJ), e2 = Math.Abs(b.NetWorkJ - fine.NetWorkJ);
    Console.WriteLine($"Grid W[1,.5,.25]={a.NetWorkJ:R},{b.NetWorkJ:R},{fine.NetWorkJ:R}; errors={e1:E3},{e2:E3}");
    Assert(e2 < .003 * Math.Abs(fine.NetWorkJ), "Work not grid-converged within 0.3%");
    double integrated = 0;
    for (int i = 1; i < fine.Trace.Count; i++) integrated += (fine.Trace[i].PressurePa + fine.Trace[i - 1].PressurePa) / 2 * (fine.Trace[i].VolumeM3 - fine.Trace[i - 1].VolumeM3);
    Near(integrated, fine.NetWorkJ, .03);
});
Test("Reproducibility, RPM response, valve lift and spark sensitivity", () =>
{
    var p = baseline ?? FourStrokeSimulator.Simulate(single, 3000);
    Near(FourStrokeSimulator.Simulate(single, 3000).NetWorkJ, p.NetWorkJ, 0);
    var lift = FourStrokeSimulator.Simulate(single with { IntakeValve = c.IntakeValve with { MaximumLiftM = .004 } }, 3000);
    var spark = FourStrokeSimulator.Simulate(single with { Combustion = c.Combustion with { SparkAdvanceDeg = 5 } }, 3000);
    Assert(Math.Abs(lift.BrakePowerW - p.BrakePowerW) > 20, "Lift has no physical effect");
    Assert(Math.Abs(spark.BrakePowerW - p.BrakePowerW) > 20, "Spark has no physical effect");
    var fast = FourStrokeSimulator.Simulate(c with { Speeds = new(6000, 6000, 1) }, 6000);
    Assert(Math.Abs(fast.IndicatedTorqueNm - p.IndicatedTorqueNm) > .1, "Torque independent of RPM");
    Console.WriteLine($"Sensitivity: base={p.BrakePowerW:F3}W lift4mm={lift.BrakePowerW:F3}W spark5deg={spark.BrakePowerW:F3}W torque6000={fast.BrakeTorqueNm:F4}Nm");
});
Test("Cancellation and explicit nonconvergence failures", () =>
{
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    try { FourStrokeSimulator.Run(single, cancel.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { }
    try { FourStrokeSimulator.Run(single with { Solver = c.Solver with { MaximumCycles = 3 } }); throw new Exception("Nonconvergence accepted"); }
    catch (ArithmeticException) { }
});
Test("Motoring, limiting RPM and valve timing regression", () =>
{
    var motored = FourStrokeSimulator.Simulate(single with { Combustion = c.Combustion with { LowerHeatingValueJPerKg = 0 } }, 3000);
    Assert(motored.BrakePowerW < 0 && double.IsNaN(motored.BrakeEfficiency), "Motoring was clamped or efficiency invented");
    foreach (int rpm in new[] { 500, 10000 })
    {
        var point = FourStrokeSimulator.Simulate(c with { Speeds = new(rpm, rpm, 1) }, rpm);
        Assert(double.IsFinite(point.BrakePowerW) && point.PeriodicError < c.Solver.PeriodicTolerance, "Limiting RPM failed");
        Near(point.MassResidualKg, 0, 1e-12); Near(point.EnergyResidualJ, 0, 1e-7);
    }
    var late = FourStrokeSimulator.Simulate(single with { IntakeValve = c.IntakeValve with { ClosesDeg = 260 } }, 3000);
    Assert(Math.Abs(late.NetWorkJ - baseline!.NetWorkJ) > 1, "Valve distribution has no effect");
});
Test("4T projects retain A/B, reject missing or incompatible data, and export traceability", () =>
{
    string directory = Path.Combine(Path.GetTempPath(), "Dino2-4T-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
    try
    {
        string path = Path.Combine(directory, "project.json");
        var b = single with { Geometry = c.Geometry with { CompressionRatio = 11 } };
        FourStrokeFiles.Save(path, new(b, single));
        var loaded = FourStrokeFiles.Load(path);
        Assert(loaded.Current == b && loaded.Original == single, "A or B changed on disk");
        string good = File.ReadAllText(path);
        foreach (var bad in new[] { "{}", "null", "{", good.Replace("\"maximumLiftM\": 0.008,", ""),
            good.Replace("four-stroke-0d-air-v1", "otto-air-standard-v1"), good.Replace("\"boreM\": 0.08", "\"boreM\": -1"),
            good.Replace("\"maximumLiftM\"", "\"unknownLift\"") })
        {
            File.WriteAllText(path, bad);
            bool rejected = false;
            try { FourStrokeFiles.Load(path); }
            catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or System.Text.Json.JsonException) { rejected = true; }
            Assert(rejected, "Bad JSON was accepted");
        }
        FourStrokeFiles.Save(path, new(single, null));
        try { FourStrokeFiles.Save(path, new(single with { Geometry = c.Geometry with { BoreM = 0 } }, null)); }
        catch (ArgumentException) { }
        Assert(FourStrokeFiles.Load(path).Current == single, "Failed save damaged original");
        var result = new FourStrokeResult(single, [baseline!], 0);
        FourStrokeFiles.Export(Path.Combine(directory, "curve.csv"), result);
        FourStrokeFiles.ExportTrace(Path.Combine(directory, "trace.csv"), result, 3000);
        FourStrokeFiles.ExportComparison(Path.Combine(directory, "ab.csv"), result, result);
        Assert(File.ReadAllText(Path.Combine(directory, "curve.csv")).Contains("configuration_SI="), "Missing input provenance");
        Assert(EngineComparison.Compare(result, result).All(p => p.PowerDifferenceW == 0), "Identity A/B does not vanish");
        Assert(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Temp files leaked");
    }
    finally { Directory.Delete(directory, true); }
});
Test("Measured full sweep and reusable numerical artifacts", () =>
{
    string output = "artifacts/4t-validation"; Directory.CreateDirectory(output);
    FourStrokeSimulator.Run(c); // tier/JIT warmup before measurements
    var elapsed = new List<double>(); var allocations = new List<long>();
    FourStrokeResult? result = null;
    for (int i = 0; i < 3; i++)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        result = FourStrokeSimulator.Run(c);
        elapsed.Add(result.ElapsedMilliseconds);
        allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
    }
    FourStrokeFiles.Export(Path.Combine(output, "baseline.csv"), result!);
    FourStrokeFiles.ExportTrace(Path.Combine(output, "baseline-3000.trace.csv"), result!, 3000);
    var lift = FourStrokeSimulator.Run(c with { IntakeValve = c.IntakeValve with { MaximumLiftM = .004 } });
    var spark = FourStrokeSimulator.Run(c with { Combustion = c.Combustion with { SparkAdvanceDeg = 5 } });
    FourStrokeFiles.Export(Path.Combine(output, "lift-4mm.csv"), lift);
    FourStrokeFiles.Export(Path.Combine(output, "spark-5deg.csv"), spark);
    FourStrokeFiles.ExportComparison(Path.Combine(output, "lift-4mm.comparison.csv"), result!, lift);
    FourStrokeFiles.ExportComparison(Path.Combine(output, "spark-5deg.comparison.csv"), result!, spark);
    var study = new List<object>();
    var mediumGrid = new Dictionary<int, FourStrokePoint>();
    var fineGrid = new Dictionary<int, FourStrokePoint>();
    foreach (double h in new[] { 2.0, 1, .5, .25, .125 })
    foreach (int rpm in new[] { 1000, 3000, 6000 })
    {
        var point = FourStrokeSimulator.Simulate(c with { Speeds = new(rpm, rpm, 1), Solver = c.Solver with { MaximumStepDeg = h, RelativeTolerance = 2e-8 } }, rpm);
        study.Add(new { h, rpm, point.NetWorkJ, point.PeakPressurePa, point.VolumetricEfficiency, point.PeriodicError, point.AcceptedSteps });
        if (h == .5) mediumGrid[rpm] = point;
        if (h == .125) fineGrid[rpm] = point;
    }
    foreach (int rpm in fineGrid.Keys)
    {
        var medium = mediumGrid[rpm]; var fine = fineGrid[rpm];
        Assert(Math.Abs(medium.NetWorkJ - fine.NetWorkJ) < .003 * Math.Abs(fine.NetWorkJ), "Work sensitivity exceeds 0.3%");
        Assert(Math.Abs(medium.PeakPressurePa - fine.PeakPressurePa) < .005 * fine.PeakPressurePa, "Peak-pressure sensitivity exceeds 0.5%");
        Assert(Math.Abs(medium.VolumetricEfficiency - fine.VolumetricEfficiency) < .001, "Filling sensitive to discretization");
    }
    var periodicStudy = new List<object>();
    foreach (double tolerance in new[] { 1e-4, 1e-6, 1e-8 })
    {
        var point = FourStrokeSimulator.Simulate(single with { Solver = c.Solver with { PeriodicTolerance = tolerance } }, 3000);
        periodicStudy.Add(new { tolerance, point.Cycles, point.PeriodicError, point.NetWorkJ });
    }
    var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(Path.Combine(output, "numerical-study.json"), System.Text.Json.JsonSerializer.Serialize(new { grid = study, periodic = periodicStudy }, options));
    var benchmark = new { runtime = Environment.Version.ToString(), logicalProcessors = Environment.ProcessorCount,
        elapsedMs = elapsed, medianMs = elapsed.Order().ElementAt(1), allocatedBytes = allocations,
        rpmPoints = result!.Points.Count, acceptedSteps = result.Points.Sum(p => p.AcceptedSteps), rejectedSteps = result.Points.Sum(p => p.RejectedSteps),
        rhsEvaluations = result.Points.Sum(p => p.RhsEvaluations) };
    File.WriteAllText(Path.Combine(output, "benchmark.json"), System.Text.Json.JsonSerializer.Serialize(benchmark, options));
    Console.WriteLine("BENCHMARK " + System.Text.Json.JsonSerializer.Serialize(benchmark));
    var kernels = new Dictionary<string, double>();
    const int iterations = 200000;
    double sink = 0;
    void Measure(string name, Action<int> action)
    {
        for (int i = 0; i < 10000; i++) action(i);
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) action(i);
        kernels[name] = timer.Elapsed.TotalMilliseconds;
    }
    Measure("energy_inversion", i => sink += AirProperties.Temperature(AirProperties.InternalEnergy(300 + i % 2800)));
    Measure("compressible_flow", i => sink += ValveFlow.MassRate(.0001, 100000, 320, 30000 + i % 60000, 700));
    Measure("slider_crank", i => sink += SliderCrank.At(c.Geometry, i % 720).VolumeM3);
    var equations = new CylinderEquations(c, 3000);
    double[] state = new double[10], rates = new double[10]; state[0] = .0004; state[1] = state[0] * AirProperties.InternalEnergy(400);
    Measure("whole_rhs_at_intake", i => { equations.Evaluate(100, state, rates); sink += rates[1]; });
    File.WriteAllText(Path.Combine(output, "kernel-benchmark.json"), System.Text.Json.JsonSerializer.Serialize(new { iterations, milliseconds = kernels, sink }, options));
});

if (args is ["--muscl-only"]) { DuctChecks.VerifyMuscl(); Console.WriteLine("PASS MUSCL waves"); return 0; }
if (args is ["--duct-only"]) { DuctChecks.Run(); Console.WriteLine("PASS independent duct benchmarks"); return 0; }
tests.Add(("Independent duct Sod, rigid reflection, variable area balance and temporal order", DuctChecks.Run));
tests.Add(("MUSCL wave propagation and reflection, strong shock, area and refinement", DuctChecks.VerifyMuscl));
int failures = 0;
foreach (var (name, action) in tests)
{
    var timer = Stopwatch.StartNew();
    try { action(); Console.WriteLine($"PASS {name} ({timer.Elapsed.TotalMilliseconds:F1} ms)"); }
    catch (Exception e) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + e); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} 4T test groups passed.");
return failures == 0 ? 0 : 1;
