using System.Globalization;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Multicylinder;
using Dino2.Core.Thermochemistry;
using Dino2.Core.Studies;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
static void Require(bool valid, string label)
{
    if (!valid) throw new Exception(label);
}

static MultiConfiguration At(int count, bool shared = true)
{
    var c = MultiConfiguration.Example(count);
    return c with { Cylinder = c.Cylinder with { Speeds = new(4000, 4000, 1) }, Manifolds = shared ? c.Manifolds : null };
}

static MultiPoint Check(MultiConfiguration configuration, bool trace = true)
{
    long allocation = GC.GetTotalAllocatedBytes(true);
    var p = MultiSimulator.Run(configuration, trace, CancellationToken.None).Points.Single();
    var balance = p.Balance;
    Console.WriteLine(FormattableString.Invariant($"N={configuration.Phases.Count},shared={configuration.Manifolds is not null},rpm={p.Rpm},status={p.Status},cycles={p.Cycles},periodic={p.PeriodicError:G6},W={p.Metrics?.NetWorkJ:G9},P={p.Metrics?.BrakePowerW:G9},fill={p.Metrics?.VolumetricEfficiency:G7},mass={balance?.MassKg:E3},elements={balance?.MaximumElementKg:E3},U={balance?.TotalEnergyJ:E3},cells={p.CellEvaluations},steps={p.AcceptedSteps},rejected={p.RejectedSteps},ms={p.ElapsedMilliseconds:F0},bytes={GC.GetTotalAllocatedBytes(true) - allocation},detail={p.Detail}"));
    Require(p.Status == MultiStatus.Converged, $"Esperaba periodicidad: {p.Detail}");
    var metrics = p.Metrics!;
    Require(p.PeriodicError < configuration.Cylinder.Solver.PeriodicTolerance, "Periodicidad global");
    Require(Math.Abs(balance!.MassKg) < 2e-12 && balance.MaximumElementKg < 2e-12 &&
        balance.NitrogenResidualKg < 2e-12 && balance.MaximumInterfaceSpeciesResidualKg < 2e-12 &&
        balance.MaximumInterfaceEnergyResidualJ < 2e-6 && Math.Abs(balance.TotalEnergyJ) < 2e-6 &&
        balance.MaximumRunnerMomentumNs < 1e-10, "Balances globales");
    Require(Math.Abs(metrics.BrakePowerW - metrics.BrakeTorqueNm * p.Rpm * Math.PI / 30) < 1e-9 &&
        Math.Abs(metrics.IndicatedPowerW - p.Cylinders.Sum(x => x.IndicatedPowerW)) < 1e-9, "Trabajo, potencia y torque");
    if (trace)
    {
        Require(p.Cycle[0].CrankAngleDeg == 0 && p.Cycle[^1].CrankAngleDeg == 720 &&
            p.Cylinders.All(c => c.Trace.Count == p.Cycle.Count), "Reloj de muestreo común");
        double integral = 0;
        for (int i = 1; i < p.Cycle.Count; i++)
            integral += (p.Cycle[i - 1].IndicatedTorqueNm + p.Cycle[i].IndicatedTorqueNm) * .5 *
                (p.Cycle[i].CrankAngleDeg - p.Cycle[i - 1].CrankAngleDeg) * Math.PI / 180;
        Require(Math.Abs(integral / metrics.NetWorkJ - 1) < .004, "Integral independiente del par indicado");
    }
    return p;
}

Require(FiringPhases.Inline(4).FiringOrder().SequenceEqual([1, 3, 4, 2]), "Orden 4 en línea");
Require(new FiringPhases(270).FiringOrder().SequenceEqual([1, 2]), "Encendido irregular");
Require(MultiSimulator.Wrap(-360) == 360 && MultiSimulator.Wrap(720) == 0, "Fase modular");
ManifoldChecks.Run();
if (args is ["--junction"]) return;

if (args is ["--benchmark", var countText] && int.TryParse(countText, out int count) && count is >= 1 and <= 4)
{
    var c = At(Math.Max(count, 2));
    var times = new double[3];
    var bytes = new long[3];
    for (int i = 0; i < times.Length; i++)
    {
        long before = GC.GetTotalAllocatedBytes(true);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (count == 1) ReactiveSimulator.Simulate(c.Cylinder, 4000);
        else Require(MultiSimulator.Run(c).Points.Single().Status == MultiStatus.Converged, "Benchmark periódico");
        times[i] = clock.Elapsed.TotalMilliseconds;
        bytes[i] = GC.GetTotalAllocatedBytes(true) - before;
    }
    var process = System.Diagnostics.Process.GetCurrentProcess();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { count, times, bytes, process.PeakWorkingSet64,
        Environment.ProcessorCount, runtime = Environment.Version.ToString() }));
    return;
}

if (args is ["--probe"])
{
    for (int i = 2; i <= 4; i++) Check(At(i));
    return;
}

if (args is ["--campaign"])
{
    var c = At(2);
    c = c with { Cylinder = c.Cylinder with { Solver = c.Cylinder.Solver with { MaximumCycles = 65 } } };
    var b = c with { Manifolds = c.Manifolds! with { Intake = c.Manifolds.Intake with { VolumeM3 = .0036 } } };
    var ai = MultiSimulator.Run(c);
    var bi = MultiSimulator.Run(b);
    var v = MultiComparison.Verify(ai, bi, 4000).Single();
    Console.WriteLine($"A/B {v.Verdict}; band={v.ObservedBandW:G9}; raw={v.PowerDifferenceW:G9}");
    foreach (var l in v.Levels) Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(l));
    Require(v.Verdict == StudyVerdict.Superior && v.ObservedBandW < v.Levels[^1].Advantage, "A/B a tres resoluciones");
    var timeOnly = c with { Cylinder = c.Cylinder with { Solver = c.Cylinder.Solver with { MaximumStepDeg = .125 } } };
    var loopOnly = c with { Cylinder = c.Cylinder with { Solver = c.Cylinder.Solver with { PeriodicTolerance = .000025 } } };
    var finerTime = Check(timeOnly, false);
    var stricter = Check(loopOnly, false);
    Console.WriteLine($"TIME deltaW={finerTime.Metrics!.NetWorkJ - ai.Points[0].Metrics!.NetWorkJ:G9} J; PERIODIC deltaW={stricter.Metrics!.NetWorkJ - ai.Points[0].Metrics!.NetWorkJ:G9} J");
    for (int n = 2; n <= 4; n++)
    {
        var ex = MultiConfiguration.Example(n);
        foreach (int rpm in ex.Cylinder.Speeds.Values())
            Check(ex with { Cylinder = ex.Cylinder with { Speeds = new(rpm, rpm, 1) } }, false);
    }
    return;
}

var configuration = At(2, false);
var timer = System.Diagnostics.Stopwatch.StartNew();
var mono = ReactiveSimulator.Simulate(configuration.Cylinder, 4000);
Console.WriteLine($"N=1,ms={timer.Elapsed.TotalMilliseconds:F0},P={mono.BrakePowerW:G9},W={mono.NetWorkJ:G9}");
var isolated = Check(configuration);
foreach (var c in isolated.Cylinders)
    Require(Math.Abs(c.NetWorkJ / mono.NetWorkJ - 1) < .002 &&
        Math.Abs(c.BrakePowerW / mono.BrakePowerW - 1) < .002, "Límite independiente frente al 4T de referencia");
Require(Math.Abs(isolated.Metrics!.BrakeTorqueNm / (mono.BrakeTorqueNm * 2) - 1) < .002, "Par independiente");

var shared = Check(At(2));
Require(Math.Abs(shared.Metrics!.NetWorkJ - isolated.Metrics.NetWorkJ) > 1, "Acoplamiento cambia el llenado y el trabajo");
Require(shared.Cycle.Max(s => s.IntakeJunctionPa) - shared.Cycle.Min(s => s.IntakeJunctionPa) > 100 &&
    shared.Cycle.Max(s => s.ExhaustJunctionPa) - shared.Cycle.Min(s => s.ExhaustJunctionPa) > 100, "Pulsaciones en ambas uniones");
Require(shared.Cylinders.Any(c => c.Trace.Any(s => s.IntakeFlowKgPerS < 0)), "Flujo de retorno calculado");
Check(At(3));
Check(At(4));
for (int n = 3; n <= 4; n++)
{
    var disconnected = Check(At(n, false), false);
    Require(Math.Abs(disconnected.Metrics!.NetWorkJ / (n * mono.NetWorkJ) - 1) < .002, "Límite aislado 3/4");
}
var asymmetric = Check(At(2) with { Phases = new(340) });
Require(Math.Abs(asymmetric.Cylinders[0].NetWorkJ - asymmetric.Cylinders[1].NetWorkJ) > .01 &&
    asymmetric.Cylinders[1].FiringOffsetDeg == 340, "Encendido irregular y combustión a través del corte global");
var study = new MultiStudy(new(At(2), [new(StudyParameter.IntakeLength, 380, 400, 20),
    new(StudyParameter.Spark, 19, 20, 1)], new(ObjectiveKind.PeakPower)));
var serial = study.Run();
var parallel = study.Run(2);
Require(serial.ExecutedCurves == 4 && parallel.ExecutedCurves == 4 &&
    serial.Evaluations.All(r => r.Status == StudyStatus.Completed) &&
    serial.Evaluations.SelectMany(r => r.Points).SequenceEqual(parallel.Evaluations.SelectMany(r => r.Points)) &&
    study.Definition.Base.Cylinder.Ducts!.Intake.LengthM == .38 && StudyRunner.Rank(study.Definition.Objective, serial.Evaluations).Count == 4,
    "Estudio físico factorial determinista, deduplicado y sin modificar A");
Console.WriteLine($"ESTUDIO casos={serial.Evaluations.Count}; ms1={serial.Milliseconds:F0}; ms2={parallel.Milliseconds:F0}");
Require(study.Run(cancellation: new CancellationToken(true)) is { Cancelled: true, ExecutedCurves: 0 }, "Cancelación de estudio");
try { (At(3) with { Phases = new(240, 240) }).Validate(); throw new Exception("Aceptó fases duplicadas"); }
catch (ArgumentException) { }
var shortRun = At(2);
shortRun = shortRun with { Cylinder = shortRun.Cylinder with { Solver = shortRun.Cylinder.Solver with { MaximumCycles = 3 } } };
Require(MultiSimulator.Run(shortRun).Points.Single() is { Status: MultiStatus.NotPeriodic, Metrics: null }, "No convergencia sin potencia aparente");
try { MultiSimulator.Run(shortRun, new CancellationToken(true)); throw new Exception("Cancelación ignorada"); }
catch (OperationCanceledException) { }
Console.WriteLine("PASS fases, límite monocilíndrico, 2–4 cilindros, conservación, periodicidad, no convergencia y cancelación.");
