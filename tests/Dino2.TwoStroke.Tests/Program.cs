using System.Diagnostics;
using System.Globalization;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Studies;
using Dino2.Core.Thermochemistry;
using Dino2.Core.TwoStroke;
using Dino2.Storage;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Contains("--stage10"))
{
    Stage10Checks.Run(args.Skip(1).ToArray());
    return;
}
var example = TwoStrokeConfiguration.Example;
{
    // Synthetic leaf only: compare the reduced stiffness/frequency with Euler-Bernoulli cantilever references.
    var leaf = new ReedLeaf(.025, .018, .00025, 1200, 4.5e9, .04, .004, .0002);
    leaf.Validate();
    double ei = leaf.YoungModulusPa * leaf.SecondMomentM4;
    double force = .02;
    double staticDeflection = force * Math.Pow(leaf.LengthM, 3) / (3 * ei);
    Near(force / leaf.TipStiffnessNPerM, staticDeflection, 1e-15, "cantilever tip stiffness");
    double exactCantileverHz = Math.Pow(1.875104068711961, 2) * Math.Sqrt(ei /
        (leaf.DensityKgM3 * leaf.WidthM * leaf.ThicknessM * Math.Pow(leaf.LengthM, 4))) / (2 * Math.PI);
    Near(leaf.NaturalFrequencyHz, exactCantileverHz, exactCantileverHz * .015, "first cantilever mode");
    double equilibrium = force / leaf.TipStiffnessNPerM;
    var relaxed = ReedDynamics.Advance(leaf, new(0, 0), force / leaf.EffectivePressureAreaM2, 1.0);
    Near(relaxed.LiftM, equilibrium, equilibrium * .025, "damped static equilibrium");
    var impact = ReedDynamics.Advance(leaf, new(leaf.LimitM * .99, 2), 0, .001);
    Check(impact.LiftM <= leaf.LimitM && impact.LiftM >= 0, "reed stop non-penetration");
    Check(ReedDynamics.StoredEnergy(leaf, impact) <= ReedDynamics.StoredEnergy(leaf, new(leaf.LimitM * .99, 2)),
        "perfectly inelastic stop created mechanical energy");
    Console.WriteLine($"PASS synthetic cantilever leaf: k={leaf.TipStiffnessNPerM:R} N/m f1={leaf.NaturalFrequencyHz:R} Hz");
}
TwoStrokeMapPortsChecks();
var config = example with { Speeds = new(5000, 5000, 1) };
var definition = new TwoStrokeStudyDefinition(config, [new(TwoStrokeParameter.DiffuserLength, 250, 290, 40)],
    new(ObjectiveKind.PeakTorque));
if (args is ["--stage7", var campaign])
{
    Stage7Campaign.Run(campaign);
    return;
}

if (args.Contains("--stage7-profile"))
{
    Console.WriteLine("run,rpm,total_ms,duct_prepare_ms,interface_ms,duct_balance_ms,cylinder_ms,crankcase_ms,evaluations,cycles,steps,shaft_w,allocated");
    double Ms(long ticks) => TwoStrokeProfile.Milliseconds(ticks);
    for (int i = 0; i < 4; i++)
    {
        long alloc = GC.GetAllocatedBytesForCurrentThread();
        var (point, profile) = TwoStrokeSimulator.ProfileOperatingPoint(config, 5000);
        Console.WriteLine($"{i},{point.Rpm},{Ms(profile.Total):F3},{Ms(profile.DuctPreparation):F3},{Ms(profile.InterfaceFlux):F3},{Ms(profile.DuctBalance):F3},{Ms(profile.CylinderThermo):F3},{Ms(profile.CrankcaseThermo):F3},{profile.Evaluations},{point.Cycles},{point.TotalAcceptedSteps},{point.ShaftPowerW:R},{GC.GetAllocatedBytesForCurrentThread() - alloc}");
    }
    return;
}

if (args.Contains("--sensitivity"))
{
    var cases = new (string label, TwoStrokeConfiguration caseConfig)[]
    {
        ("reference", config),
        ("case_160cm3", config with { CrankcaseMinimumM3 = .000160 }),
        ("reed_700N_m", config with { Reed = config.Reed with { StiffnessNPerM = 700 } }),
        ("exhaust_roof_30mm", config with { Exhaust = config.Exhaust with { RoofM = .030 } }),
        ("transfer_throat_450mm2", config with { Transfer = config.Transfer with { ThroatM2 = .000450 } }),
        ("compression_11.5", config with { Geometry = config.Geometry with { CompressionRatio = 11.5 } }),
        ("fresh_extraction_0.75", config with { FreshExtractionRatio = .75 }),
        ("periodic_tol_1e-5", config with { Numerics = config.Numerics with { PeriodicTolerance = 1e-5 } })
    };
    Console.WriteLine("case,shaft_w,torque_nm,trapped_mg,delivery_ratio,scavenging,periodic_error,cycles");
    foreach (var (label, caseConfig) in cases)
    {
        var p = TwoStrokeSimulator.Simulate(caseConfig, 5000, false);
        Balance(p);
        Console.WriteLine($"{label},{p.ShaftPowerW:R},{p.ShaftTorqueNm:R},{p.FreshTrappedKg * 1e6:R},{p.DeliveryRatio:R},{p.ScavengingEfficiency:R},{p.PeriodicError:R},{p.Cycles}");
    }
    return;
}

if (args.Contains("--benchmark"))
{
    var study = TwoStrokeStudy.Create(definition);
    Console.WriteLine($"logical_cpus={Environment.ProcessorCount}; runtime={Environment.Version}");
    var rpmInput = example;
    var serialTimer = Stopwatch.StartNew();
    var serial = TwoStrokeSimulator.Sweep(rpmInput, false);
    Console.WriteLine($"rpm_serial_ms={serialTimer.Elapsed.TotalMilliseconds:F2}; all_periodic={serial.Complete}");
    serialTimer.Restart();
    var parallel = new TwoStrokePoint[serial.Runs.Count];
    Parallel.For(0, parallel.Length, new ParallelOptions { MaxDegreeOfParallelism = 3 },
        i => parallel[i] = TwoStrokeSimulator.Simulate(rpmInput, serial.Runs[i].Rpm, false));
    Console.WriteLine($"rpm_three_workers_ms={serialTimer.Elapsed.TotalMilliseconds:F2}; working_set={Process.GetCurrentProcess().WorkingSet64}");
    for (int i = 0; i < parallel.Length; i++)
        Near(parallel[i].ShaftPowerW, serial.Runs[i].Point!.ShaftPowerW, 0, "parallel RPM reproducibility");
    TwoStrokeStudyRun? reference = null;
    foreach (int workers in new[] { 1, 2, 4 })
    {
        var run = study.Run(workers);
        Console.WriteLine($"workers={workers}; study_ms={run.Milliseconds:F2}; process_working_set={Process.GetCurrentProcess().WorkingSet64}");
        foreach (var row in run.Evaluations)
        {
            var point = row.Sweep?.Runs.Single().Point;
            Console.WriteLine($"id={row.Id}; status={row.Status}; ms={row.Milliseconds:F2}; shaft_w={point?.ShaftPowerW:F6}; steps={point?.Steps}; total_steps={point?.TotalAcceptedSteps}; rejected={point?.TotalRejectedSteps}; cycles={point?.Cycles}; trap_kg={point?.FreshTrappedKg:E7}");
            if (reference is not null)
                Near(reference.Evaluations[row.Id].Sweep!.Runs[0].Point!.ShaftPowerW, point!.ShaftPowerW, 0, "parallel reproducibility");
        }
        reference ??= run;
    }
    var twoAxis = TwoStrokeStudy.Create(definition with
    {
        Axes = [.. definition.Axes, new(TwoStrokeParameter.SparkAdvance, 19, 21, 2)]
    });
    var grid = twoAxis.Run(2);
    Console.WriteLine($"two_axes_ms={grid.Milliseconds:F2}; curves={grid.Evaluations.Count}; converged={grid.Evaluations.Count(r => r.Status == StudyStatus.Completed)}; allocated_bytes_total={GC.GetTotalAllocatedBytes(true)}; peak_working_set={Process.GetCurrentProcess().PeakWorkingSet64}");
    foreach (var row in grid.Evaluations)
        Console.WriteLine($"grid_id={row.Id}; shaft_w={row.Sweep?.Runs[0].Point?.ShaftPowerW:G10}; ms={row.Milliseconds:F2}");
    var result = study.Verify(reference!, 2, 0, progress: new Immediate<string>(Console.WriteLine));
    Console.WriteLine($"verification={result.Verdict}; ms={result.Milliseconds:F2}; band_nm={result.ObservedBand:G9}");
    foreach (var level in result.Levels) Console.WriteLine(level);
    foreach (var (scheme, scale, temporal, equilibrium) in new[]
    {
        (DuctScheme.FirstOrder, 1, 1.0, true), (DuctScheme.Muscl, 1, .5, true),
        (DuctScheme.Muscl, 1, 1.0, false)
    })
    {
        var merged = TwoStrokeStudy.Refine(config, scale);
        merged = merged with
        {
            Combustion = merged.Combustion with { ProductEquilibrium = equilibrium },
            Numerics = merged.Numerics with { Scheme = scheme, Courant = merged.Numerics.Courant * temporal, MaximumStepDeg = merged.Numerics.MaximumStepDeg * temporal }
        };
        var timer = Stopwatch.StartNew();
        var p = TwoStrokeSimulator.Simulate(merged, 5000, false);
        Console.WriteLine($"scheme={scheme}; scale={scale}; time_factor={temporal}; equilibrium={equilibrium}; shaft_w={p.ShaftPowerW:F6}; peak_k={p.PeakTemperatureK:F4}; fresh_kg={p.FreshTrappedKg:E7}; cycles={p.Cycles}; ms={timer.Elapsed.TotalMilliseconds:F2}");
    }
    return;
}

var g = config.Geometry;
Near(TwoStrokeGeometry.Travel(g, 0), 0, 1e-15, "TDC travel");
Near(TwoStrokeGeometry.Travel(g, 180), g.StrokeM, 1e-15, "BDC travel");
Near(TwoStrokeGeometry.CrankcaseVolume(g, config.CrankcaseMinimumM3, 180), config.CrankcaseMinimumM3, 1e-15, "minimum case");
Near(config.PrimaryCompressionRatio, TwoStrokeGeometry.CrankcaseVolume(g, config.CrankcaseMinimumM3, 0) / config.CrankcaseMinimumM3, 1e-12, "primary ratio");
Check(config.TrappedCompressionRatio < g.CompressionRatio, "trapped compression");
double evo = config.Exhaust.Opening(g);
Near(config.Exhaust.RoofM, TwoStrokeGeometry.Travel(g, evo), 1e-13, "opening");
Near(config.Exhaust.Area(TwoStrokeGeometry.Travel(g, 360 - evo)), 0, 1e-15, "closing");
Near(config.Transfer.Area(config.Transfer.RoofM + .002), config.Transfer.Cd * config.Transfer.WidthM * .002, 1e-15, "window area");
Near(config.Exhaust.Area(2), config.Exhaust.Cd * Math.Min(config.Exhaust.ThroatM2, config.Exhaust.WidthM * config.Exhaust.HeightM), 1e-15, "saturated area");
{
    var height = config.Transfer.HeightM;
    var halfWidth = config.Transfer.WidthM;
    var triangle = config.Transfer with
    {
        WidthProfile = [new(0, 0), new(height / 2, halfWidth), new(height, 0)],
        WidthProfileSource = "synthetic-triangle.csv", WidthProfileSha256 = new string('c', 64)
    };
    Near(triangle.GeometricOpeningArea(config.Transfer.RoofM + height), halfWidth * height / 2, 1e-15,
        "analytic triangular port area");
    Near(triangle.Area(config.Transfer.RoofM + height / 2), config.Transfer.Cd * halfWidth * height / 4, 1e-15,
        "partial contour integration");
    Near(triangle.Area(config.Transfer.RoofM), 0, 0, "contour closure");
    var rectangle = config.Transfer with
    {
        WidthProfile = [new(0, halfWidth), new(height, halfWidth)],
        WidthProfileSource = "synthetic-rectangle.csv", WidthProfileSha256 = new string('d', 64)
    };
    Near(rectangle.Area(config.Transfer.RoofM + height / 3), config.Transfer.Cd * halfWidth * height / 3, 1e-15,
        "rectangular contour matches historical width law");
    string csv = Path.Combine(Path.GetTempPath(), "dino2-port-width-" + Guid.NewGuid().ToString("N") + ".csv");
    try
    {
        File.WriteAllText(csv, $"height_mm,width_mm\n0,0\n{height * 500}, {halfWidth * 1000:R}\n{height * 1000:R},0\n");
        var imported = PistonPortWidthProfileFile.Load(csv, height, halfWidth);
        Check(imported.Points.SequenceEqual(triangle.WidthProfile!) && imported.Sha256.Length == 64,
            "CSV contour import and hash");
    }
    finally { File.Delete(csv); }
}
Near(config.Reed.EffectiveArea(-10000), 0, 0, "reed reverse");
Near(config.Reed.EffectiveArea(config.Reed.OpeningPressurePa), 0, 0, "reed crack");
double lift = config.Reed.ThroatM2 * 1000 / config.Reed.StiffnessNPerM;
Near(config.Reed.EffectiveArea(1000 + config.Reed.OpeningPressurePa), config.Reed.Cd * config.Reed.PerimeterM * lift, 1e-16, "reed static");
SegmentedMesh(config.Expansion);
ExpansionReflection();
Stage7Campaign.CheckAnalyticLimits();

var watch = Stopwatch.StartNew();
var pointA = TwoStrokeSimulator.Simulate(config, 5000);
Console.WriteLine($"2T reference {watch.Elapsed.TotalMilliseconds:F1} ms, {pointA.Cycles} cycles, {pointA.Steps} steps, {pointA.ShaftPowerW:G10} W.");
Check(pointA.PeriodicError <= config.Numerics.PeriodicTolerance && pointA.Cycles > 3, "periodic");
Check(pointA.Trace.Count > 250 && pointA.Trace.Zip(pointA.Trace.Skip(1)).All(x => x.First.AngleDeg < x.Second.AngleDeg), "trace sorted");
{
    var tracePoint = pointA with
    {
        Trace = [pointA.Trace[0] with { ExhaustMapCd = .17, ExhaustMapMassResidualKgPerS = 2e-12 }]
    };
    string csvPath = Path.GetTempFileName();
    try
    {
        TwoStrokeFiles.ExportTrace(csvPath, tracePoint);
        var csvRows = File.ReadAllLines(csvPath);
        Check(csvRows[0].EndsWith(",exhaust_map_cd,exhaust_map_mass_residual_kg_s", StringComparison.Ordinal) &&
            csvRows[1].Split(',')[^2] == "0.17" && csvRows[1].Split(',')[^1] == "2E-12",
            "2T mapped Cd and residual are traceable in CSV export");
    }
    finally { File.Delete(csvPath); }
}
{
    var height = config.Transfer.HeightM;
    var port = config.Transfer with
    {
        WidthProfile = [new(0, 0), new(height / 2, config.Transfer.WidthM), new(height, 0)],
        WidthProfileSource = "synthetic-area-time.csv", WidthProfileSha256 = new string('f', 64)
    };
    var profiled = TwoStrokeSimulator.Simulate(config with { Transfer = port }, 5000, false);
    Check(double.IsFinite(profiled.ShaftPowerW) && Math.Abs(profiled.ShaftPowerW - pointA.ShaftPowerW) > .1,
        "contour geometry changes a complete 2T cycle");
    Console.WriteLine($"PORT CONTOUR 2T 5000 RPM reference={pointA.ShaftPowerW:F5} W profile={profiled.ShaftPowerW:F5} W · synthetic verification, not experiment");
    var profiledConfiguration = config with { Transfer = port };
    var profiledSweep = new TwoStrokeSweep(profiledConfiguration,
        [new(5000, profiled, null, TwoStrokeRunStatus.Periodic)]);
    string contourProject = Path.Combine(Path.GetTempPath(), "dino2-contour-project-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        TwoStrokeFiles.Save(contourProject, new(profiledConfiguration, null, profiledSweep));
        var restored = TwoStrokeFiles.Load(contourProject);
        Check(restored.Current == profiledConfiguration && restored.CurrentResult!.Configuration == profiledConfiguration,
            "profiled port project and matching result survive integrity round trip");
    }
    finally { File.Delete(contourProject); }
}
Check(pointA.Trace.All(s => s.ReedKgPerS >= -1e-12 && s.FreshCylinderKg >= -1e-12 &&
    s.FreshCylinderKg <= s.CylinderMassKg + 1e-12), "admissible marker and reed");
Check(pointA.Trace.Any(s => s.ReedKgPerS > 0) && pointA.Trace.Any(s => s.TransferKgPerS > 0) &&
    pointA.Trace.Any(s => s.ExhaustOutKgPerS < 0) && pointA.Trace.Any(s => s.ExhaustOutKgPerS > 0), "aerodynamic exchange");
Check(pointA.PeakTemperatureK > 1800 && pointA.FuelBurnedKg > 0, "firing");
Check(pointA.FreshTrappedKg > 0 && pointA.FreshExhaustOutKg > 0 && pointA.ResidualMassKg > 0, "scavenging");
Near(pointA.FreshTrappedKg, pointA.FreshDeliveredKg + pointA.FreshExhaustReturnKg -
    pointA.FreshTransferReturnKg - pointA.FreshExhaustOutKg, 1e-13, "scavenging accounting");
Near(pointA.ShaftPowerW, pointA.ShaftTorqueNm * 2 * Math.PI * pointA.Rpm / 60, 1e-10, "one revolution per cycle");
Balance(pointA);
Near(pointA.FreshAccountingResidualKg, 0, 1e-13, "net fresh accounting");
Near(pointA.DeliveryRatio * pointA.TrappingPerTransferDelivery!.Value, pointA.ChargingEfficiency, 1e-13, "charge and delivery identity");
var pointRepeat = TwoStrokeSimulator.Simulate(config, 5000, false);
Check(pointA with { Trace = pointRepeat.Trace } == pointRepeat, "deterministic point including all diagnostics");
var variant = config with { Expansion = config.Expansion with { DiffuserM = .29, Cells = 33 }, FreshExtractionRatio = .75 };
var pointB = TwoStrokeSimulator.Simulate(variant, 5000, false);
Check(Math.Abs(pointB.FreshTrappedKg - pointA.FreshTrappedKg) > 1e-8, "geometry plus nonuniform exhaust sampling");
Balance(pointB);
var motored = config with { Combustion = config.Combustion with { BurnFraction = 0 } };
var cold = TwoStrokeSimulator.Simulate(motored, 5000, false);
Check(cold.FuelBurnedKg == 0 && cold.PeakTemperatureK < pointA.PeakTemperatureK && cold.ShaftPowerW < 0 && cold.BsfcGPerKwh is null, "motored limit");
Balance(cold);

var failure = TwoStrokeSimulator.Sweep(config with { Numerics = config.Numerics with { MaximumCycles = 3 } });
Check(!failure.Complete && failure.Runs[0].Status == TwoStrokeRunStatus.NonConverged &&
    failure.Runs[0].Point is null && failure.Runs[0].Convergence is { Cycles: 3, Residual: > 0, PreviousResidual: > 0 },
    "nonconvergence diagnostic and unpublished power");
var invalidOperatingPoint = config with { Speeds = new(5000, 10000, 5000),
    Combustion = config.Combustion with { DurationS = .002 } };
var partialDomain = TwoStrokeSimulator.Sweep(invalidOperatingPoint, false);
Check(partialDomain.Runs[0].Status == TwoStrokeRunStatus.Periodic &&
    partialDomain.Runs[1].Status == TwoStrokeRunStatus.OutsideDomain && partialDomain.Runs[1].Point is null &&
    !partialDomain.Complete, "out-of-domain RPM preserves adjacent periodic result");
var plan = TwoStrokeStudy.Create(definition with { Axes = [.. definition.Axes, new(TwoStrokeParameter.SparkAdvance, 19, 20, 1)] });
Check(plan.Variants.Count == 5 && plan.Variants[4].Configuration.Combustion.AdvanceDeg == 20 &&
    config.Expansion.DiffuserM == .27 && config.Combustion.AdvanceDeg == 19, "Cartesian and immutable baseline");
var invalidStudy = TwoStrokeStudy.Create(definition with { Base = config with { Numerics = config.Numerics with { MaximumCycles = 3 } },
    Axes = [new(TwoStrokeParameter.SparkAdvance, 19, 60, 41)] });
var failedStudy = invalidStudy.Run();
var rows = failedStudy.Evaluations;
Check(rows[0].Status == StudyStatus.NonConverged && rows[1].Milliseconds == 0 && rows[2].Status == StudyStatus.Inadmissible, "isolation and deduplication");
using var cancel = new CancellationTokenSource();
cancel.Cancel();
Check(plan.Run(token: cancel.Token).Cancelled, "study cancellation");
Check(StudyVerification.Classify([new(1, 3, 1, 2, null, null, null), new(2, 3, 1, 2, null, null, null), new(4, 3, 1, 2, null, null, null)]).Verdict == StudyVerdict.Superior, "independent classification");
var synthetic = new StudyPoint(6000, 1200, 1000, 1, .5, .000001, 12, 3e6, 2450, 8, 1e-6, 0, 0);
Near(new StudyObjective(ObjectiveKind.MinimumBsfc).Evaluate([synthetic], revolutionsPerCycle: 1).Score!.Value, 360, 1e-10, "2T BSFC scoring");

string temporary = Path.Combine(Path.GetTempPath(), $"dino2-two-stroke-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporary);
try
{
    var first = new TwoStrokeSweep(config, [new(5000, pointA, null, TwoStrokeRunStatus.Periodic)]);
    var second = new TwoStrokeSweep(variant, [new(5000, pointB, null, TwoStrokeRunStatus.Periodic)]);
    string path = Path.Combine(temporary, "test.json");
    TwoStrokeFiles.Save(path, new(variant, config, second, first));
    var reloaded = TwoStrokeFiles.Load(path);
    Check(reloaded.Current == variant && reloaded.Original == config &&
        reloaded.OriginalResult!.Runs[0].Point!.Trace.SequenceEqual(pointA.Trace), "project round trip");
    var profiled = config with { Transfer = config.Transfer with
    {
        WidthProfile = [new(0, 0), new(config.Transfer.HeightM, config.Transfer.WidthM)],
        WidthProfileSource = "synthetic-contour.csv", WidthProfileSha256 = new string('e', 64)
    } };
    string profiledPath = Path.Combine(temporary, "profiled.json");
    TwoStrokeFiles.Save(profiledPath, new(profiled, null));
    var restoredProfile = TwoStrokeFiles.Load(profiledPath).Current.Transfer;
    Check(restoredProfile.WidthProfile!.SequenceEqual(profiled.Transfer.WidthProfile!) &&
        restoredProfile.WidthProfileSha256 == profiled.Transfer.WidthProfileSha256, "contour profile persistence and integrity");
    var edited = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
    var editedPoint = edited["project"]!["currentResult"]!["runs"]![0]!["point"]!;
    editedPoint["peakPressurePa"] = pointB.PeakPressurePa + 100;
    string editedFile = Path.Combine(temporary, "tamper.json");
    File.WriteAllText(editedFile, edited.ToJsonString());
    try { TwoStrokeFiles.Load(editedFile); throw new Exception("Digest alterado aceptado."); }
    catch (InvalidDataException ex) when (ex.Message.Contains("SHA-256", StringComparison.Ordinal)) { }
    edited["format"] = "dino2-two-stroke-project-v1";
    edited.AsObject().Remove("integrity");
    editedPoint["shaftPowerW"] = pointB.IndicatedPowerW * 32;
    File.WriteAllText(editedFile, edited.ToJsonString());
    try { TwoStrokeFiles.Load(editedFile); throw new Exception("Potencia incoherente aceptada."); }
    catch (InvalidDataException ex) when (ex.Message.Contains("deben corresponder", StringComparison.Ordinal)) { }
    TwoStrokeFiles.ExportComparison(Path.Combine(temporary, "compare.csv"), first, second);
    TwoStrokeFiles.Export(Path.Combine(temporary, "curve.csv"), first);
    TwoStrokeFiles.ExportTrace(Path.Combine(temporary, "trace.csv"), pointA);
    Check(File.ReadLines(Path.Combine(temporary, "compare.csv")).Count() == 2, "comparison export");
    Check(File.ReadLines(Path.Combine(temporary, "trace.csv")).Count() == pointA.Trace.Count + 1, "trace export");
    string partial = Path.Combine(temporary, "partial.json");
    TwoStrokeFiles.Save(partial, new(invalidOperatingPoint, null, partialDomain));
    var replay = TwoStrokeFiles.Load(partial).CurrentResult!;
    Check(replay.NumericalRevision == TwoStrokeConfiguration.NumericalRevision &&
        replay.Provenance is { ResultSchema: 1 } stamp && stamp.Build.Length > 0 &&
        replay.Runs[0].Convergence == partialDomain.Runs[0].Convergence &&
        replay.Runs[1].Status == TwoStrokeRunStatus.OutsideDomain, "partial RPM persistence and numerical revision");
    string partialCsv = Path.Combine(temporary, "partial.csv");
    TwoStrokeFiles.Export(partialCsv, replay);
    using var csv = new Microsoft.VisualBasic.FileIO.TextFieldParser(partialCsv);
    csv.SetDelimiters(",");
    var header = csv.ReadFields()!;
    var successfulRow = csv.ReadFields()!;
    var unavailableRow = csv.ReadFields()!;
    Check(header.Length == successfulRow.Length && header.Length == unavailableRow.Length &&
        unavailableRow[Array.IndexOf(header, "status")] == "OutsideDomain" &&
        unavailableRow[Array.IndexOf(header, "p_shaft_w")] == "", "complete schema for failed RPM rows");
    var studyPath = Path.Combine(temporary, "study.json");
    TwoStrokeFiles.SaveStudy(studyPath, failedStudy);
    var restored = TwoStrokeFiles.LoadStudy(studyPath);
    Check(restored.Definition.Base == failedStudy.Definition.Base &&
        restored.Definition.Axes.SequenceEqual(failedStudy.Definition.Axes) &&
        restored.Evaluations.Select(row => row.Status).SequenceEqual(rows.Select(row => row.Status)), "study round trip");
    var studyCsv = Path.Combine(temporary, "study.csv");
    TwoStrokeFiles.ExportStudy(studyCsv, restored);
    Check(File.ReadLines(studyCsv).Count() == 4, "study CSV with rejected and unsteady cases");
}
finally { Directory.Delete(temporary, true); }
Console.WriteLine("2T: geometry, mesh, reed, conservation, scavenging, periodicity, determinism, variants, limits, storage and CSV passed.");

static void SegmentedMesh(ExpansionChamber expansion)
{
    var gas = new GasEquation(1);
    var mesh = new DuctMesh(new(expansion.LengthM, expansion.StingerDiameterM, expansion.InletDiameterM, expansion.Cells),
        gas, DuctScheme.Muscl, expansion.DiameterFromTail, expansion.MeshKnots());
    static double Frustum(double length, double a, double b) => Math.PI * length / 12 * (a * a + a * b + b * b);
    double apparent = Enumerable.Range(0, mesh.Cells).Sum(mesh.Volume);
    double expected = Frustum(expansion.HeaderM, expansion.InletDiameterM, expansion.InletDiameterM)
        + Frustum(expansion.DiffuserM, expansion.InletDiameterM, expansion.BellyDiameterM)
        + Frustum(expansion.BellyM, expansion.BellyDiameterM, expansion.BellyDiameterM)
        + Frustum(expansion.ConeM, expansion.BellyDiameterM, expansion.StingerDiameterM)
        + Frustum(expansion.StingerM, expansion.StingerDiameterM, expansion.StingerDiameterM);
    Near(apparent, expected, 1e-15, "segmented volume");
    var state = new double[mesh.Size];
    for (int i = 0; i < mesh.Cells; i++) gas.Encode(state.AsSpan(i * gas.Size, gas.Size), [1], 1e5, 300, 0, 0);
    var rates = new double[mesh.Size];
    var left = new double[gas.Size];
    var right = new double[gas.Size];
    left[gas.Momentum] = 1e5 * mesh.ReservoirArea;
    right[gas.Momentum] = 1e5 * mesh.ValveArea;
    mesh.Prepare(state);
    mesh.Rates(state, rates, left, right);
    Check(rates.All(v => Math.Abs(v) < 1e-7), "segmented hydrostatic balance");
}

static void TwoStrokeMapPortsChecks()
{
    var c = TwoStrokeConfiguration.RecoveryExample;
    var gas = new GasEquation(SpeciesThermodynamics.ExtendedCount);
    var forward = PortMap("forward", .16);
    var reverse = PortMap("reverse", .22);
    var port = c.Exhaust with { DischargeMap = forward, ReverseDischargeMap = reverse };
    var duct = TwoStrokeDuct.Exhaust(c, gas, null);
    double travel = port.RoofM + .65 * port.HeightM;
    var tube = new double[duct.Size];
    duct.Initialize(tube);
    var donor = new double[gas.Size];
    var fractions = TwoStrokeDuct.TailMixture(c);
    var chamber = gas.EncodeFace(donor, fractions, 185000, 410, 0, 0);
    var rate = new double[duct.Size];
    var outer = new double[gas.Size];
    var inner = new double[gas.Size];
    double dt = duct.Rates(tube, rate, chamber, donor, port.GeometricArea(travel), null, outer, inner,
        out double face, out _, out var trace, mappedPort: port, pistonTravelM: travel);
    Check(dt > 0 && face > 0 && trace is { FlowFromReservoir: true, MapCd: not null },
        "2T 1D exhaust map resolves a forward ports-v4 state");
    Near(trace!.Value.MapCd!.Value, .16, 2e-12, "2T forward exhaust map selection");
    double speciesMass = Enumerable.Range(0, gas.SpeciesCount).Sum(i => inner[i]);
    Near(speciesMass, trace.Value.ThroatMassKgPerS, Math.Abs(trace.Value.ThroatMassKgPerS) * 2e-8,
        "2T exhaust ports-v4 species mass equals accepted interface mass");
    Check(Math.Abs(trace.Value.MapMassResidualKgPerS ?? double.PositiveInfinity) < 1e-8 &&
        double.IsFinite(inner[gas.Energy]) && double.IsFinite(inner[gas.Momentum]),
        "2T map residual and conservative energy/momentum flux are finite");

    for (int i = 0; i < duct.Cells; i++)
        gas.Encode(tube.AsSpan(i * gas.Size, gas.Size), fractions, 185000, 410, 0, 0);
    Array.Clear(rate); Array.Clear(outer); Array.Clear(inner);
    var lowChamberValues = new double[gas.Size];
    var lowChamber = gas.EncodeFace(lowChamberValues, fractions, 100000, 300, 0, 0);
    duct.Rates(tube, rate, lowChamber, lowChamberValues, port.GeometricArea(travel), null, outer, inner,
        out face, out _, out trace, mappedPort: port, pistonTravelM: travel);
    Check(trace is { FlowFromReservoir: false, MapCd: not null }, "2T 1D exhaust map resolves reverse flow");
    Near(trace!.Value.MapCd!.Value, .22, 2e-12, "2T reverse exhaust map selection");
    Check(face > 0 && Math.Abs(trace.Value.MapMassResidualKgPerS ?? double.PositiveInfinity) < 1e-8,
        "2T reverse exhaust map closes mass residual");
    Console.WriteLine("PASS 2T 1D exhaust Cd map: forward/reverse ports-v4, accepted mass conservation, residual and trace values");

    var reed = c.Reed with
    {
        DischargeMap = PortMap("forward", .15, "reed_open_area"),
        ReverseDischargeMap = PortMap("reverse", .21, "reed_open_area")
    };
    var intake = TwoStrokeDuct.Intake(c, gas, fractions, null);
    var intakeTube = new double[intake.Size];
    intake.Initialize(intakeTube);
    var intakeRate = new double[intake.Size];
    Array.Clear(outer); Array.Clear(inner);
    var lowCaseValues = new double[gas.Size];
    var lowCase = gas.EncodeFace(lowCaseValues, fractions, 90000, 300, 0, 0);
    intake.Rates(intakeTube, intakeRate, lowCase, lowCaseValues, 0, reed, outer, inner,
        out face, out _, out trace, mappedReed: reed);
    Check(trace is { FlowFromReservoir: false, MapCd: not null }, "quasi-static reed map resolves forward inlet flow");
    Near(trace!.Value.MapCd!.Value, .15, 2e-12, "quasi-static reed forward map selection");
    var highCaseValues = new double[gas.Size];
    var highCase = gas.EncodeFace(highCaseValues, fractions, 115000, 330, 0, 0);
    Array.Clear(intakeRate); Array.Clear(outer); Array.Clear(inner);
    intake.Rates(intakeTube, intakeRate, highCase, highCaseValues, 0, reed, outer, inner,
        out face, out _, out trace, dynamicReedLiftM: .001, mappedReed: reed);
    Check(trace is { FlowFromReservoir: true, MapCd: not null }, "dynamic reed map resolves reverse flow while leaf is open");
    Near(trace!.Value.MapCd!.Value, .21, 2e-12, "dynamic reed reverse map selection");
    Check(face > 0 && Math.Abs(trace.Value.MapMassResidualKgPerS ?? double.PositiveInfinity) < 1e-8,
        "reed map closes accepted-state mass residual");
    Console.WriteLine("PASS 2T reed Cd maps: quasi-static and dynamic ports-v4 flow with directional selection");

    static DischargeMap PortMap(string direction, double cd, string referenceArea = "port_open_area")
    {
        var metadata = new DischargeMapMetadata(referenceArea, "upstream_stagnation_to_downstream_static",
            direction, "opening_fraction,pressure_ratio,dimensionless", "synthetic 2T ports-v4 interface test",
            "ports-v4-equivalent-capacity-v1", "synthetic");
        return new(metadata, new DischargeMapPoint[]
        {
            new(0,.2,cd), new(0,1,cd), new(1,.2,cd), new(1,1,cd)
        });
    }
}

static void ExpansionReflection()
{
    const double length = 1.2, pressure = 100000, temperature = 300, amplitude = 300;
    double sound = Math.Sqrt(1.4 * 287 * temperature), density = pressure / (287 * temperature);
    double Diameter(double x) => .04 + .04 * Math.Clamp((x - .65) / .03, 0, 1);
    var gas = new GasEquation(1);
    foreach (int cells in new[] { 96, 192 })
    {
        var mesh = new DuctMesh(new(length, .04, .08, cells), gas, DuctScheme.Muscl, Diameter, [.65, .68]);
        var u = new double[mesh.Size];
        var a = new double[mesh.Size];
        var b = new double[mesh.Size];
        var star = new double[mesh.Size];
        var xl = new double[gas.Size];
        var xr = new double[gas.Size];
        for (int i = 0; i < cells; i++)
        {
            double x = (i + .5) * mesh.Dx;
            double pulse = amplitude * Math.Exp(-.5 * Math.Pow((x - .25) / .038, 2));
            double p = pressure + pulse;
            gas.Encode(u.AsSpan(i * gas.Size, gas.Size), [1], p, temperature * Math.Pow(p / pressure, 2.0 / 7),
                pulse / (density * sound), 0);
        }

        double Apply(double[] values, double[] rate)
        {
            mesh.Prepare(values);
            xl[gas.Momentum] = gas.WallPressure(mesh.BoundaryState(values, false, xl), false) * mesh.ReservoirArea;
            xr[gas.Momentum] = gas.WallPressure(mesh.BoundaryState(values, true, xr), true) * mesh.ValveArea;
            // BoundaryState loads a conservative face into its buffer. Only its pressure force crosses a wall.
            for (int i = 0; i < gas.Size; i++)
                if (i != gas.Momentum) xl[i] = xr[i] = 0;
            return mesh.Rates(values, rate, xl, xr);
        }

        double time = 0, incident = 0, reflected = 0, turnaround = 0;
        int probe = (int)(.45 / mesh.Dx);
        while (time < .0022)
        {
            double dt = .37 * Apply(u, a);
            for (int i = 0; i < u.Length; i++) star[i] = u[i] + dt * a[i];
            Apply(star, b);
            for (int i = 0; i < u.Length; i++) u[i] += dt * .5 * (a[i] + b[i]);
            time += dt;
            double dp = gas.Decode(u.AsSpan(probe * gas.Size, gas.Size)).Pressure - pressure;
            if (time < .001) incident = Math.Max(incident, dp);
            if (time > .0013 && dp < reflected) { reflected = dp; turnaround = time; }
        }
        double ratio = reflected / incident;
        // A step in area would give R=(A1-A2)/(A1+A2)=-0.6. A finite transition and the pulse bandwidth
        // allow a lower magnitude, with the returning rarefaction timed by the round trip to the diffuser.
        Check(incident > .8 * amplitude && ratio is < -.42 and > -.68, "expansion reflects a rarefaction of the expected order");
        Check(turnaround is > .00155 and < .00195, "expansion return time");
        Console.WriteLine($"2T segmented wave: {cells} cells, R={ratio:F4}, return={turnaround:E4} s.");
    }
}

static void Balance(TwoStrokePoint point)
{
    var b = point.Balance;
    Near(b.MassKg, 0, 2e-14, "mass");
    Near(b.CarbonKg, 0, 2e-14, "carbon");
    Near(b.HydrogenKg, 0, 2e-14, "hydrogen");
    Near(b.OxygenKg, 0, 2e-14, "oxygen");
    Near(b.NitrogenKg, 0, 2e-14, "nitrogen");
    Near(b.EnergyJ, 0, 1e-8, "energy");
    Near(b.FreshMarkerKg, 0, 2e-14, "tag");
    var s = b.SpeciesKg;
    Check(new[] { s.FuelKg, s.OxygenKg, s.NitrogenKg, s.CarbonDioxideKg, s.WaterKg,
        s.CarbonMonoxideKg, s.HydrogenKg }.All(x => Math.Abs(x) < 2e-14), "species stoichiometry");
}

static void Check(bool condition, string check)
{
    if (!condition) throw new Exception(check);
}

static void Near(double value, double expected, double tolerance, string name) =>
    Check(Math.Abs(value - expected) <= tolerance, $"{name}: {value:R} vs {expected:R}");

sealed class Immediate<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
