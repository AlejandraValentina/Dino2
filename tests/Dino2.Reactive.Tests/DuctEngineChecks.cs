using System.Text.Json;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;
using Dino2.Storage;

internal static class DuctEngineChecks
{
    private const string Output = "artifacts/stage4";

    internal static void Run()
    {
        Directory.CreateDirectory(Output);
        var configuration = FourStrokeFiles.Load("examples/4t-ducts.json").Current;
        var ducts = configuration.Ducts!;
        var variant = configuration with { Ducts = ducts with { Intake = ducts.Intake with { LengthM = .55 } } };
        var original = EngineSimulation.Run(configuration);
        var modified = EngineSimulation.Run(variant);
        var reservoirs = EngineSimulation.Run(configuration with { Ducts = null });
        Check(original.Model == GasPath.ModelId && reservoirs.Model == ThermochemicalOptions.EquilibriumModelId, "Model dispatch");
        foreach (var point in original.Points.Concat(modified.Points)) CheckBalances(point, configuration);
        var comparison = EngineComparison.Compare(original, modified);
        Check(comparison.Count == original.Points.Count, "Missing A/B RPM");
        Check(comparison.Max(p => Math.Abs(p.FillingDifference)) > .0005, $"Filling response {comparison.Max(p => Math.Abs(p.FillingDifference)):G6}");

        var path = Path.Combine(Output, "duct-length-ab.json");
        FourStrokeFiles.Save(path, new(variant, configuration));
        var restored = FourStrokeFiles.Load(path);
        Check(restored.Original == configuration && restored.Current == variant, "Geometry A/B not preserved");
        FourStrokeFiles.Export(Path.Combine(Output, "v3-reservoirs.csv"), reservoirs);
        FourStrokeFiles.Export(Path.Combine(Output, "v4-base.csv"), original);
        FourStrokeFiles.Export(Path.Combine(Output, "v4-long-intake.csv"), modified);
        FourStrokeFiles.ExportTrace(Path.Combine(Output, "v4-base-4000.trace.csv"), original, 4000);
        FourStrokeFiles.ExportTrace(Path.Combine(Output, "v4-long-intake-4000.trace.csv"), modified, 4000);
        FourStrokeFiles.ExportComparison(Path.Combine(Output, "length-comparison.csv"), original, modified);

        var basePoint = original.Points.Single(p => p.Rpm == 4000);
        var variantPoint = modified.Points.Single(p => p.Rpm == 4000);
        Check(basePoint.Trace.Max(p => p.ExhaustPortPressurePa) - basePoint.Trace.Min(p => p.ExhaustPortPressurePa) > 1e4,
            "No exhaust pressure transient.");
        Check(basePoint.Trace.Any(s => s.ExhaustFlowKgPerS < -.01), "Exhaust never delivers gas to its duct.");
        var single = configuration with { Speeds = new(4000, 4000, 1) };
        var repeat = RunPoint(single);
        Check(repeat.NetWorkJ == basePoint.NetWorkJ && repeat.VolumetricEfficiency == basePoint.VolumetricEfficiency, "Run changed with identical settings.");
        var exhaustVariant = RunPoint(single with { Ducts = ducts with { Exhaust = ducts.Exhaust with { ReservoirDiameterM = .05 } } });
        Check(Math.Abs(exhaustVariant.NetWorkJ - basePoint.NetWorkJ) > .05, "Exhaust taper has no discernible effect.");

        var grids = new List<object>();
        for (int scale = 1; scale <= 4; scale *= 2)
        {
            var mesh = ducts with
            {
                Intake = ducts.Intake with { Cells = ducts.Intake.Cells * scale },
                Exhaust = ducts.Exhaust with { Cells = ducts.Exhaust.Cells * scale }
            };
            var a = scale == 1 ? basePoint : RunPoint(single with { Ducts = mesh });
            var b = scale == 1 ? variantPoint : RunPoint(single with { Ducts = mesh with { Intake = mesh.Intake with { LengthM = .55 } } });
            grids.Add(new { intake = mesh.Intake.Cells, exhaust = mesh.Exhaust.Cells, a = Metrics(a), b = Metrics(b),
                deltaPowerW = b.BrakePowerW - a.BrakePowerW, deltaVe = b.VolumetricEfficiency - a.VolumetricEfficiency });
        }

        var temporal = RunPoint(single with { Ducts = ducts with { Courant = ducts.Courant / 2 },
            Solver = single.Solver with { MaximumStepDeg = single.Solver.MaximumStepDeg / 2 } });
        Check(Math.Abs(temporal.NetWorkJ - basePoint.NetWorkJ) < .5, "Work too sensitive to temporal refinement.");
        var morePeriodic = RunPoint(single with { Solver = single.Solver with { PeriodicTolerance = 1e-8 } });
        Check(Math.Abs(morePeriodic.NetWorkJ - basePoint.NetWorkJ) < .01, "Periodic tolerance changes work.");
        var summary = new
        {
            grids, temporal = Metrics(temporal), tightenedPeriodicity = Metrics(morePeriodic), exhaustTaper = Metrics(exhaustVariant),
            benchmark = new
            {
                original.ElapsedMilliseconds, variantMilliseconds = modified.ElapsedMilliseconds, referenceMilliseconds = reservoirs.ElapsedMilliseconds,
                steps = original.Points.Sum(p => p.AcceptedSteps), cellEvaluations = original.Points.Sum(p => p.GasDynamics!.CellEvaluations)
            }
        };
        File.WriteAllText(Path.Combine(Output, "coupled-study.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Metrics(FourStrokePoint point) => new
    {
        point.NetWorkJ, point.BrakePowerW, point.VolumetricEfficiency, point.PeakPressurePa,
        point.Cycles, point.PeriodicError, point.ElapsedMilliseconds, point.GasDynamics
    };

    private static FourStrokePoint RunPoint(FourStrokeConfiguration configuration)
    {
        var point = EngineSimulation.Run(configuration).Points.Single();
        CheckBalances(point, configuration);
        return point;
    }

    private static void CheckBalances(FourStrokePoint point, FourStrokeConfiguration configuration)
    {
        var duct = point.GasDynamics ?? throw new Exception("Missing duct balance.");
        Check(Math.Abs(duct.MassResidualKg) < 1e-12 && duct.MaximumSpeciesResidualKg < 1e-12 &&
            Math.Abs(duct.EnergyResidualJ) < 1e-6, $"Duct balance: {duct}");
        Check(Math.Abs(point.EnergyResidualJ) < 1e-6 && point.Chemistry!.MaximumElementResidualKg < 1e-12,
            "Cylinder chemistry balance failed.");
        Check(duct.PeriodicError < configuration.Solver.PeriodicTolerance, "Duct state failed periodicity.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
