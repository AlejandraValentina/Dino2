using System.Diagnostics;
using System.Text.Json;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;
using Dino2.Storage;

internal static class DuctClosureChecks
{
    private const string Output = "artifacts/stage4-closure";

    internal static void Run()
    {
        Directory.CreateDirectory(Output);
        var prior = FourStrokeFiles.Load("examples/4t-ducts-ab.json");
        var a = prior.Original!;
        var b = prior.Current;
        Check(a.Ducts!.Scheme == DuctScheme.FirstOrder && a.ModelIdentifier == GasPath.ModelId, "Old snapshot changed identity.");
        FourStrokeConfiguration UseMuscl(FourStrokeConfiguration c) => c with { Ducts = c.Ducts! with { Scheme = DuctScheme.Muscl } };
        var current = new FourStrokeProject(UseMuscl(b), UseMuscl(a));
        FourStrokeFiles.Save(Path.Combine(Output, "ducts-muscl-ab.json"), current);
        Check(FourStrokeFiles.Load(Path.Combine(Output, "ducts-muscl-ab.json")) == current, "MUSCL project does not round-trip.");

        var rows = new List<object>();
        foreach (var scheme in new[] { DuctScheme.FirstOrder, DuctScheme.Muscl })
        foreach (var cells in (scheme == DuctScheme.Muscl ? new[] { 12, 24, 48, 96 } : new[] { 24, 48, 96 }))
        {
            var result = RunPair(a, b, cells, scheme, .45, 1e-6);
            rows.Add(new { scheme = scheme.ToString(), cells, result });
            Console.WriteLine($"{scheme} {cells} cells: delta {result.DeltaPowerW:F4} W, VE {result.DeltaVe:G6}, {result.Seconds:F2} s");
        }

        var timeBase = RunPair(a, b, 24, DuctScheme.Muscl, .45, 1e-6);
        var halfTime = RunPair(a, b, 24, DuctScheme.Muscl, .225, 1e-6, .25);
        var tight = RunPair(a, b, 24, DuctScheme.Muscl, .45, 1e-8);
        Check(Math.Abs(tight.DeltaPowerW - timeBase.DeltaPowerW) < .02, "Periodic residual dominates A/B.");
        Check(Math.Abs(halfTime.DeltaPowerW - timeBase.DeltaPowerW) < 2, "Time resolution dominates A/B.");
        var wide = current.Original! with { Ducts = current.Original!.Ducts! with
        {
            Exhaust = current.Original.Ducts.Exhaust with { ReservoirDiameterM = .05 }
        } };
        var taper = new[] { 24, 48, 96 }.Select(cells => RunPair(current.Original!, wide, cells, DuctScheme.Muscl, .45, 1e-6)).ToArray();
        var benchmark = new List<object>();
        foreach (var scheme in new[] { DuctScheme.FirstOrder, DuctScheme.Muscl })
        {
            var duct = a.Ducts! with { Scheme = scheme };
            long before = GC.GetAllocatedBytesForCurrentThread();
            var result = EngineSimulation.Run(a with { Ducts = duct });
            benchmark.Add(new { scheme = scheme.ToString(), result.ElapsedMilliseconds,
                allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before,
                rhs = result.Points.Sum(p => p.RhsEvaluations), cells = result.Points.Sum(p => p.GasDynamics!.CellEvaluations) });
            FourStrokeFiles.Export(Path.Combine(Output, $"{scheme}-sweep.csv"), result);
            FourStrokeFiles.ExportTrace(Path.Combine(Output, $"{scheme}-4000.trace.csv"), result, 4000);
        }

        var curveA = EngineSimulation.Run(current.Original!);
        var curveB = EngineSimulation.Run(current.Current);
        FourStrokeFiles.ExportComparison(Path.Combine(Output, "muscl-comparison.csv"), curveA, curveB);
        File.WriteAllText(Path.Combine(Output, "campaign.json"),
            JsonSerializer.Serialize(new { rows, timeBase, halfTime, tight, taper, benchmark }, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void Regression()
    {
        var template = FourStrokeFiles.Load("examples/4t-ducts-muscl-ab.json").Current;
        var duct = template.Ducts!;
        var a = template with
        {
            Speeds = new(4000, 4000, 1),
            Ducts = duct with { Intake = duct.Intake with { Cells = 8 }, Exhaust = duct.Exhaust with { Cells = 8 } }
        };
        var b = a with { Ducts = a.Ducts! with { Intake = a.Ducts.Intake with { LengthM = .35 } } };
        var left = EngineSimulation.Run(a);
        var right = EngineSimulation.Run(b);
        Check(DuctResolution.CanEvaluate(left, right) && left.Model == GasPath.MusclModelId, "MUSCL dispatch or eligibility failed.");
        Check(!DuctResolution.CanEvaluate(left, right with { Configuration = b with { Intake = b.Intake with { PressurePa = 90000 } } }),
            "Different operating conditions accepted as geometry comparison.");
        var verification = DuctResolution.Evaluate(left, right, 4000);
        Check(Math.Abs(verification.BrakeTorqueNm.Fine - verification.BrakePowerW.Fine * 60 / (2 * Math.PI * 4000)) < 1e-12,
            "Power and torque use different comparisons.");
        Check(!MeshSignal.From(5, -4, 3).AboveObservedVariation && !MeshSignal.From(1, 2, 3.5).AboveObservedVariation &&
            MeshSignal.From(30, 33, 34).AboveObservedVariation, "Evidence classification inconsistent.");

        var stressed = a with
        {
            Intake = a.Intake with { PressurePa = 55000 },
            Exhaust = a.Exhaust with { PressurePa = 150000 },
            Combustion = a.Combustion with { HeatReleaseEfficiency = 0 },
            Ducts = a.Ducts! with { Exhaust = a.Ducts.Exhaust with { ReservoirDiameterM = .06 } }
        };
        var motored = EngineSimulation.Run(stressed).Points.Single();
        Check(motored.BrakePowerW < 0 && motored.GasDynamics!.MaximumSpeciesResidualKg < 1e-12 &&
            motored.GasDynamics.MomentumResidualNs < 1e-9 && motored.GasDynamics.EnergyResidualJ < 1e-6,
            "Throttled motored case violates balances.");
        Check(motored.Trace.All(s => s.PressurePa > 0 && s.IntakePortPressurePa > 0 && s.ExhaustPortPressurePa > 0),
            "Motored pressure negative.");
        try
        {
            EngineSimulation.Run(a with { Solver = a.Solver with { MaximumCycles = 3 } });
            throw new Exception("A deliberately unconverged run published a point.");
        }
        catch (ArithmeticException) { }
    }

    private static Pair RunPair(FourStrokeConfiguration a, FourStrokeConfiguration b, int cells, DuctScheme scheme,
        double cfl, double periodic, double angularStep = .5)
    {
        FourStrokePoint Solve(FourStrokeConfiguration c)
        {
            var duct = c.Ducts!;
            var point = ReactiveSimulator.Simulate(c with
            {
                Speeds = new(4000, 4000, 1),
                Solver = c.Solver with { PeriodicTolerance = periodic, MaximumStepDeg = angularStep },
                Ducts = duct with { Courant = cfl, Scheme = scheme, Intake = duct.Intake with { Cells = cells },
                    Exhaust = duct.Exhaust with { Cells = cells * 4 / 3 } }
            }, 4000);
            var d = point.GasDynamics!;
            Check(d.MaximumSpeciesResidualKg < 1e-12 && d.EnergyResidualJ < 1e-6 &&
                d.MomentumResidualNs < 1e-9 && d.PeriodicError < periodic, "Duct audit failed.");
            Check(point.Chemistry!.MaximumElementResidualKg < 1e-12 && Math.Abs(point.EnergyResidualJ) < 1e-6, "Chamber audit failed.");
            return point;
        }
        var timer = Stopwatch.StartNew();
        var left = Solve(a);
        var right = Solve(b);
        return new(right.BrakePowerW - left.BrakePowerW, right.VolumetricEfficiency - left.VolumetricEfficiency,
            right.NetWorkJ - left.NetWorkJ, left.BrakePowerW, right.BrakePowerW, timer.Elapsed.TotalSeconds,
            left.GasDynamics!, right.GasDynamics!);
    }

    private sealed record Pair(double DeltaPowerW, double DeltaVe, double DeltaWorkJ, double PowerA,
        double PowerB, double Seconds, DuctDiagnostics A, DuctDiagnostics B);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
