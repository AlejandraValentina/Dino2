using System.Diagnostics;
using System.Text.Json;
using Dino2.Core.FourStroke;
using Dino2.Core.Studies;
using Dino2.Core.Thermochemistry;
using Dino2.Storage;

string output = Path.Combine("artifacts", "stage5");
Directory.CreateDirectory(output);
var rpm = new RpmRange(3000, 3000, 1);
var basis = ThermochemicalOptions.EquilibriumExample with { Speeds = rpm };
var definition = new StudyDefinition(basis, [new(StudyParameter.Spark, 10, 30, 10)], rpm, new(ObjectiveKind.PeakPower));

if (args is ["--benchmark"])
{
    Benchmark();
    return 0;
}

var tests = new (string, Action)[]
{
    ("Finite factorial grid, engineering units, model capabilities and hard limits", Plan),
    ("Objectives with known answers, constraint and specific fuel consumption", Objectives),
    ("Known interacting objective, ranking and empirical resolution classes", KnownSurface),
    ("Physical studies deterministic in parallel; base immutable and reused", Physical),
    ("Invalid combinations, nonperiodicity and cancellation retain distinct states", Failures),
    ("Study and diagnostics round trip, CSV and corrupted data rejection", Storage),
    ("MUSCL length remeshing and verified 0D candidate", ModelAndRefinement)
};
int errors = 0;
foreach (var (label, run) in tests)
{
    try { run(); Console.WriteLine("PASS " + label); }
    catch (Exception ex) { errors++; Console.Error.WriteLine("FAIL " + label + ": " + ex); }
}
Console.WriteLine($"{tests.Length - errors}/{tests.Length} study test groups passed.");
return errors == 0 ? 0 : 1;

void Plan()
{
    var plan = StudyPlan.Create(definition with
    {
        Axes = [new(StudyParameter.IntakeLift, 6, 8, 2), new(StudyParameter.Compression, 9, 11, 1)],
        Speeds = new(2000, 4000, 1000)
    });
    Check(plan.Variants.Count == 7 && plan.DistinctCurves == 6 && plan.OperatingPoints == 18, "Cartesian count or base reuse");
    Check(plan.Variants[1].Configuration.IntakeValve.MaximumLiftM == .006 &&
        plan.Variants[6].Configuration.Geometry.CompressionRatio == 11, "Unit conversion or enumeration");
    var cube = StudyPlan.Create(definition with
    {
        Axes = [new(StudyParameter.Spark, 10, 20, 10), new(StudyParameter.Compression, 9, 10, 1),
            new(StudyParameter.ExhaustLift, 6, 7, 1)]
    });
    Check(cube.Variants.Count == 9 && cube.Variants[8].Configuration == cube.Variants[0].Configuration, "Three-dimensional grid");
    Reject(() => StudyPlan.Create(definition with { Axes = [new(StudyParameter.Spark, 0, 10, 3)] }));
    Reject(() => StudyPlan.Create(definition with { Speeds = new(2000, 4001, 1000) }));
    Reject(() => StudyPlan.Create(definition with { Axes = [new(StudyParameter.IntakeLength, 300, 400, 50)] }));
    Reject(() => StudyPlan.Create(definition with
    {
        Axes = [new(StudyParameter.Spark, 0, 10, 1), new(StudyParameter.Compression, 5, 14, 1)]
    }));
    Reject(() => StudyPlan.Create(definition with { Speeds = new(500, 9900, 100) }));
    Reject(() => StudyPlan.Create(definition with { Axes = [new(StudyParameter.Spark, 0, 10, 5), new(StudyParameter.Spark, 0, 10, 5)] }));
    Reject(() => StudyPlan.Create(definition with { Axes = [new(StudyParameter.Spark, double.NaN, 10, 5)] }));
}

void Objectives()
{
    StudyPoint[] curve = [Point(1000, 1000, 10), Point(2000, 3000, 15), Point(4000, 9000, 20)];
    Near(new StudyObjective(ObjectiveKind.PeakPower).Evaluate(curve).Score, 9000);
    Near(new StudyObjective(ObjectiveKind.MeanPower).Evaluate(curve).Score, 14000.0 / 3);
    Near(new StudyObjective(ObjectiveKind.PeakTorque).Evaluate(curve).Score, 20);
    Near(new StudyObjective(ObjectiveKind.MeanTorque, new(2000, 4000)).Evaluate(curve).Score, 17.5);
    var compromise = new StudyObjective(ObjectiveKind.HighTorqueLimitedLowLoss, new(2000, 4000), new(1000, 1000), 1);
    var baseCurve = curve.Select(p => p.Rpm == 1000 ? p with { BrakeTorqueNm = 12 } : p).ToArray();
    var excluded = compromise.Evaluate(curve, baseCurve);
    Check(!excluded.Eligible && excluded.LowMarginNm == -1, "Compromise applies low torque constraint");
    Check(compromise.Evaluate(curve, curve).Eligible, "Reference meets its own constraint");
    var consumption = new StudyObjective(ObjectiveKind.MinimumBsfc);
    Near(consumption.Evaluate([Point(3000, 6000, 10) with { FuelMassKgPerCycle = .00002 }]).Score, 300);
    Check(!consumption.Evaluate([Point(3000, 0, 10)]).Eligible, "BSFC undefined without positive power");
    Reject(() => StudyPlan.Create(definition with { Objective = new(ObjectiveKind.PeakPower, new(3000, 4000)) }));
    Reject(() => StudyPlan.Create(definition with { Base = FourStrokeConfiguration.Example, Objective = consumption }));
}

void KnownSurface()
{
    var plan = StudyPlan.Create(definition with
    {
        Axes = [new(StudyParameter.Spark, 10, 30, 10), new(StudyParameter.Compression, 9, 11, 1)]
    });
    var rows = plan.Variants.Skip(1).Select(v =>
    {
        double advance = v.Parameters[0].Value, compression = v.Parameters[1].Value;
        double merit = 100 - Math.Pow((advance - 20) / 10 - (compression - 11) * .3, 2) - Math.Pow(compression - 11, 2);
        return new StudyEvaluation(v.Id, StudyStatus.Completed, [Point(3000, merit, 1)], null, 0);
    }).ToArray();
    var ranked = StudyRunner.Rank(plan.Definition.Objective, rows);
    Check(ranked[0].Id == 6 && ranked[0].Objective.Score == 100, "Known interacting maximum");
    var minimum = StudyRunner.Rank(new(ObjectiveKind.MinimumBsfc), new[]
    {
        new StudyEvaluation(1, StudyStatus.Completed, [Point(3000, 100, 1) with { FuelMassKgPerCycle = .00002 }], null, 0),
        new StudyEvaluation(2, StudyStatus.Completed, [Point(3000, 120, 1) with { FuelMassKgPerCycle = .00002 }], null, 0)
    });
    Check(minimum[0].Id == 2, "Minimizing objective direction");
    StudyVerdict Verdict(params double[] d) => StudyVerification.Classify(d.Select((v, i) =>
        new StudyLevel(1 << i, null, null, v, null, null, null)).ToArray()).Verdict;
    Check(Verdict(14, 15, 15.2) == StudyVerdict.Superior &&
        Verdict(-14, -15, -15.2) == StudyVerdict.Inferior &&
        Verdict(.4, .16, .05) == StudyVerdict.Indistinguishable &&
        Verdict(1, 1.2, 3) == StudyVerdict.DiscretizationSensitive &&
        Verdict(1, -1, .1) == StudyVerdict.DiscretizationSensitive &&
        Verdict(0, 0, 0) == StudyVerdict.Indistinguishable, "Resolution labels");
}

void Physical()
{
    var plan = StudyPlan.Create(definition);
    var sequential = StudyRunner.Run(plan);
    var parallel = StudyRunner.Run(plan, 2);
    SamePoints(sequential, parallel);
    Check(sequential.ExecutedCurves == 3 && sequential.Evaluations.Count == 4, "Identical base variant ran again");
    Check(definition.Base == basis && basis.Combustion.SparkAdvanceDeg == 20, "Applied modification to base");
    foreach (var row in sequential.Evaluations)
        Check(row.Status == StudyStatus.Completed && row.Points.All(p => Math.Abs(p.EnergyResidualJ) < 1e-7), "Observed physically invalid curve");
}

void Failures()
{
    var longBurn = basis with
    {
        Combustion = basis.Combustion with { DurationS = .005 },
        Thermochemistry = basis.Thermochemistry! with { BurnLaw = BurnLaw.PrescribedWiebe }
    };
    var operating = new RpmRange(4000, 4000, 1);
    var plan = StudyPlan.Create(new(longBurn, [new(StudyParameter.ExhaustOpen, 420, 540, 120)], operating, new(ObjectiveKind.PeakPower)));
    var result = StudyRunner.Run(plan);
    Check(result.Evaluations[1].Status == StudyStatus.Inadmissible &&
        result.Evaluations[2].Status == StudyStatus.Completed && result.Evaluations[1].Points.Count == 0, "Failure isolation");
    var nonPeriodic = StudyRunner.Run(StudyPlan.Create(definition with
    {
        Base = basis with { Solver = basis.Solver with { MaximumCycles = 3 } }
    }));
    Check(nonPeriodic.Evaluations.All(e => e.Status == StudyStatus.NonConverged), "Typed nonconvergence");
    using var cancel = new CancellationTokenSource();
    var partial = StudyRunner.Run(StudyPlan.Create(definition), cancellation: cancel.Token,
        progress: new InlineProgress<StudyProgress>(e => { if (e.Evaluated == 2) cancel.Cancel(); }));
    Check(partial.Cancelled && partial.Evaluations.Count == 2 && partial.Evaluations.All(e => e.Points.Count == 1),
        "Partial results not safe");
    var path = Path.Combine(output, "partial.dino-study.json");
    StudyFiles.Save(path, new(partial, []));
    StudyFiles.Export(Path.Combine(output, "partial.csv"), StudyFiles.Load(path));
    var csv = Path.Combine(output, "partial.csv");
    Check(File.ReadAllText(csv).Contains("Pending"), "Partial CSV omits unevaluated variants");
    using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(csv) { CommentTokens = ["#"] };
    parser.SetDelimiters(",");
    int columns = parser.ReadFields()!.Length;
    while (!parser.EndOfData) Check(parser.ReadFields()!.Length == columns, "CSV columns shifted for pending rows");
}

void Storage()
{
    var plan = StudyPlan.Create(definition);
    var run = StudyRunner.Run(plan);
    var check = StudyVerification.Compare(plan, run, 1, 0);
    var path = Path.Combine(output, "spark-study.dino-study.json");
    StudyFiles.Save(path, new(run, [check]));
    var restored = StudyFiles.Load(path);
    SamePoints(run, restored.Run);
    Check(restored.Run.Definition.Base == run.Definition.Base && restored.Checks[0].Levels.SequenceEqual(check.Levels),
        "Lost scientific identity or contrast levels");
    string csv = Path.Combine(output, "spark-study.csv");
    StudyFiles.Export(csv, restored);
    Check(File.ReadAllText(csv).Contains("# resolution="), "Missing numeric resolution evidence in CSV");
    foreach (var shipped in new[] { "study-spark-v3.dino-study.json", "study-ducts-muscl.dino-study.json" })
    {
        var sample = StudyFiles.Load(Path.Combine("examples", shipped));
        Check(sample.Checks.Single().Verdict == StudyVerdict.Superior, "Packaged example lost its tested contrast");
    }
    string bad = Path.Combine(output, "bad-study.json");
    File.WriteAllText(bad, File.ReadAllText(path).Replace("\"solverModel\": \"four-stroke-isooctane-equilibrium-v3\"", "\"solverModel\": \"unknown\""));
    Reject(() => StudyFiles.Load(bad));
}

void ModelAndRefinement()
{
    var engine = FourStrokeFiles.Load("examples/4t-ducts-muscl-ab.json").Original!;
    var plan = StudyPlan.Create(new(engine, [new(StudyParameter.IntakeLength, 350, 550, 200)], new(4000, 4000, 1), new(ObjectiveKind.PeakTorque)));
    Check(plan.Variants[2].Configuration.Ducts!.Intake.Cells == 38 && plan.Variants[2].Configuration.Ducts!.Intake.LengthM == .55,
        "Length variant must match axial spacing");
    var duct = StudyRunner.Run(plan, 2);
    Check(duct.ExecutedCurves == 2 && duct.Evaluations.All(e => e.Status == StudyStatus.Completed && e.Points[0].DuctMomentumResidualNs < 1e-9),
        "MUSCL study does not reuse baseline or close balances");
    StudyFiles.Save(Path.Combine(output, "duct-study.dino-study.json"), new(duct, []));
    var crowded = engine with { Ducts = engine.Ducts! with
    {
        Intake = engine.Ducts.Intake with { LengthM = .08, Cells = 256 },
        Exhaust = engine.Ducts.Exhaust with { LengthM = .08, Cells = 256 }
    } };
    Reject(() => StudyPlan.Create(definition with { Base = crowded, Speeds = new(500, 500, 1) }));
    var coarse = StudyRunner.Run(StudyPlan.Create(definition));
    var comparison = StudyVerification.Compare(StudyPlan.Create(definition), coarse, 1, 2);
    var concurrent = StudyVerification.Compare(StudyPlan.Create(definition), coarse, 1, 2, workers: 2);
    Check(concurrent.Verdict == comparison.Verdict && concurrent.Levels.SequenceEqual(comparison.Levels),
        "Parallel refinement changed scores");
    Check(comparison.Levels.Count == 3 && comparison.Levels.All(l => l.Advantage.HasValue), "Missing full-refinement scores");
}

void Benchmark()
{
    var motor = FourStrokeFiles.Load("examples/4t-ducts-muscl-ab.json").Original!;
    var studies = new (string Name, StudyDefinition Definition)[]
    {
        ("spark-v3", definition with { Axes = [new(StudyParameter.Spark, 5, 35, 5)],
            Speeds = new(2000, 4000, 1000), Objective = new(ObjectiveKind.MeanPower) }),
        ("length-diameter-muscl", new(motor,
            [new(StudyParameter.IntakeLength, 350, 490, 140), new(StudyParameter.IntakeOuterDiameter, 42, 50, 8)],
            new(4000, 5000, 1000), new(ObjectiveKind.MeanTorque)))
    };
    var data = new List<object>();
    foreach (var (name, item) in studies)
    {
        var plan = StudyPlan.Create(item);
        StudyRun? first = null;
        foreach (int workers in new[] { 1, 2, 4 })
        {
            long before = GC.GetTotalAllocatedBytes(true);
            var result = StudyRunner.Run(plan, workers);
            long allocated = GC.GetTotalAllocatedBytes(true) - before;
            if (first is not null) SamePoints(first, result); else first = result;
            Check(result.Evaluations.All(e => e.Status == StudyStatus.Completed), name + ": nonconverged benchmark row");
            data.Add(new { name, workers, plan.OperatingPoints, result.ExecutedCurves, result.Milliseconds, allocated,
                curveMilliseconds = result.Evaluations.Where(r => r.Milliseconds > 0).Select(r => r.Milliseconds).ToArray(),
                peakProcessBytes = Process.GetCurrentProcess().PeakWorkingSet64 });
            Console.WriteLine($"{name}, {workers}: {result.Milliseconds:F0} ms / {plan.OperatingPoints} RPM");
        }
        var best = StudyRunner.Rank(item.Objective, first!.Evaluations)[0];
        var contrast = StudyVerification.Compare(plan, first, best.Id, 0, workers: 2);
        Console.WriteLine($"{name} paired refinement: {contrast.Milliseconds:F0} ms · {contrast.Verdict}");
        StudyFiles.Save(Path.Combine(output, name + ".dino-study.json"), new(first, [contrast]));
        StudyFiles.Export(Path.Combine(output, name + ".csv"), new(first, [contrast]));
        data.Add(new { name, contrastMilliseconds = contrast.Milliseconds, contrast.Workers, verdict = contrast.Verdict.ToString(), contrast.Levels });
    }
    File.WriteAllText(Path.Combine(output, "benchmark.json"), JsonSerializer.Serialize(new
    { logicalProcessors = Environment.ProcessorCount, data }, new JsonSerializerOptions { WriteIndented = true }));
}

static StudyPoint Point(int rpm, double power, double torque) => new(rpm, power, power, torque, .9,
    .00001, 10, 1e6, 1500, 5, 1e-9, 1e-16, 1e-10);

static void SamePoints(StudyRun a, StudyRun b)
{
    Check(a.Evaluations.Count == b.Evaluations.Count && a.Evaluations.Zip(b.Evaluations)
        .All(x => x.First.Id == x.Second.Id && x.First.Status == x.Second.Status &&
                  x.First.Points.SequenceEqual(x.Second.Points)), "Case ordering or physical result changed");
}

static void Near(double? actual, double expected) =>
    Check(actual.HasValue && Math.Abs(actual.Value - expected) < 1e-8, $"Expected {expected:R}, got {actual:R}");
static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
static void Reject(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or JsonException) { return; }
    throw new Exception("Expected invalid input rejection");
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
