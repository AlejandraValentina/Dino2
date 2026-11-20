using System.Diagnostics;
using System.Globalization;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Multicylinder;
using Dino2.Core.Thermochemistry;
using Dino2.Core.TwoStroke;

internal static class Stage11Campaign
{
    private static readonly TwoStrokeConfiguration Two = TwoStrokeConfiguration.Example;
    private static readonly DuctWall IntakeWall = new(.00002, 320), ExhaustWall = new(.00004, 500);

    internal static void Run(string mode)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var jobs = new List<Action>();
        var four = ThermochemicalOptions.EquilibriumExample;
        var legacy = GasPath.Example with { Scheme = DuctScheme.Muscl };
        var physical = legacy with { Exterior = ExteriorBoundary.PressureReservoir };
        var disk = four with
        {
            Thermochemistry = four.Thermochemistry! with { BurnLaw = BurnLaw.PrescribedWiebe },
            Ducts = physical, Speeds = new(3000, 6000, 1500),
            Solver = four.Solver with { PeriodicTolerance = .0001, MaximumCycles = 125 }
        };

        if (mode == "geometry")
        {
            foreach (int rpm in new[] { 4000, 5000, 6000 })
            {
                var p = Corrected(Two with { Speeds = new(rpm, rpm, 1) });
                foreach (double scale in new[] { .8, 1.25 })
                {
                    var e = p.Expansion;
                    var v = e with { HeaderM = e.HeaderM * scale, DiffuserM = e.DiffuserM * scale, BellyM = e.BellyM * scale,
                        ConeM = e.ConeM * scale, StingerM = e.StingerM * scale, Cells = (int)Math.Round(e.Cells * scale) };
                    jobs.Add(() => TwoRun($"expansion scale={scale}", p with { Expansion = v }));
                }
            }
            foreach (double ratio in new[] { .75, 1, 1.25 })
                jobs.Add(() => TwoRun($"extraction={ratio}", Corrected(Two with { Speeds = new(5000, 5000, 1), FreshExtractionRatio = ratio })));
        }
        else if (mode == "tuning")
        {
            foreach (double length in new[] { .15, .35, .70, 1.40 })
                jobs.Add(() => FourRun($"physical intake L={length}", disk with { Ducts = physical with
                {
                    ExhaustExterior = ExteriorBoundary.HistoricalRiemann,
                    Intake = physical.Intake with { LengthM = length, Cells = Math.Max(8, (int)Math.Round(24 * length / .35)) }
                } }));
            foreach (double length in new[] { .25, .40, .85 })
                jobs.Add(() => FourRun($"physical both exhaust L={length}", disk with { Ducts = physical with
                {
                    Intake = physical.Intake with { Wall = IntakeWall },
                    Exhaust = physical.Exhaust with { LengthM = length, Cells = Math.Max(8, (int)Math.Round(32 * length / .65)), Wall = ExhaustWall }
                } }));
        }
        else if (mode == "cause")
        {
            jobs.Add(() => FourRun("diagnostic", disk));
            jobs.Add(() => TwoRun("diagnostic x2", TwoStrokeStudy.Refine(Corrected(Two with { Speeds = new(8000, 8000, 1) }), 2)));
        }
        else if (mode == "engines")
        {
            foreach (double length in new[] { .15, .35, .70, 1.40 })
                foreach (var boundary in Enum.GetValues<ExteriorBoundary>())
                {
                    var path = physical with { Exterior = boundary, Intake = physical.Intake with
                        { LengthM = length, Cells = Math.Max(8, (int)Math.Round(24 * length / .35)) } };
                    jobs.Add(() => FourRun($"suction L={length} {boundary}", disk with { Ducts = path }));
                }
            jobs.Add(() => FourRun("4T walls", disk with { Ducts = physical with
            {
                Intake = physical.Intake with { Wall = IntakeWall }, Exhaust = physical.Exhaust with { Wall = ExhaustWall }
            } }));
            jobs.Add(() => FourRun("4T angles", disk with { Combustion = disk.Combustion with { DurationDeg = 54 } }));
            jobs.Add(() => FourRun("conditioned high RPM", four with { Ducts = legacy, Speeds = new(7500, 9000, 1500) }));
            jobs.Add(() => MultiRuns(false)); jobs.Add(() => MultiRuns(true));
            foreach (int rpm in new[] { 4000, 5000, 6000 })
            {
                var c = Two with { Speeds = new(rpm, rpm, 1) };
                jobs.Add(() => TwoRun("old", c));
                var p = Corrected(c);
                jobs.Add(() => TwoRun("pressure", p));
                jobs.Add(() => TwoRun("walls", p with { IntakeDuct = p.IntakeDuct with { Wall = IntakeWall },
                    Expansion = p.Expansion with { Wall = ExhaustWall } }));
                jobs.Add(() => TwoRun("angles", p with { Combustion = p.Combustion with { DurationDeg = 33 } }));
            }
        }
        else if (mode is "mesh" or "fine")
        {
            foreach (int rpm in new[] { 6000, 8000 })
                foreach (int scale in (mode == "fine" ? new[] { 8 } : new[] { 1, 2, 4 }))
                {
                    var c = Corrected(Two with { Speeds = new(rpm, rpm, 1) });
                    c = c with
                    {
                        IntakeDuct = c.IntakeDuct with { Cells = c.IntakeDuct.Cells * scale },
                        Expansion = c.Expansion with { Cells = c.Expansion.Cells * scale },
                        Numerics = c.Numerics with { MaximumStepDeg = .65 / scale, PeriodicTolerance = 5e-5 / scale, MaximumCycles = 200 }
                    };
                    jobs.Add(() => TwoRun($"x{scale}", c));
                }
            if (mode == "mesh")
                foreach (int rpm in new[] { 6000, 8000 })
                {
                    var c = Corrected(Two with { Speeds = new(rpm, rpm, 1) });
                    jobs.Add(() => TwoRun("CFL .2", c with { Numerics = c.Numerics with { Courant = .2 } }));
                    jobs.Add(() => TwoRun("periodic 1e-6", c with { Numerics = c.Numerics with { PeriodicTolerance = 1e-6, MaximumCycles = 200 } }));
                }
        }
        else throw new ArgumentException("Campaña: engines, mesh o fine.");
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = mode == "engines" ? 3 : 2 }, job => job());
    }

    private static TwoStrokeConfiguration Corrected(TwoStrokeConfiguration c) =>
        c with { Numerics = c.Numerics with { Exterior = ExteriorBoundary.PressureReservoir } };

    private static void FourRun(string label, FourStrokeConfiguration c)
    {
        var result = EngineSimulation.Run(c);
        Console.WriteLine($"4T {label} · {result.ElapsedMilliseconds:F0} ms " +
            string.Join(" | ", result.Points.Select(p => $"{p.Rpm}:T={p.BrakeTorqueNm:F5},VE={p.VolumetricEfficiency:F5},Ppk={p.PeakPressurePa:F0},Tpk={p.PeakTemperatureK:F1},cycle={p.Cycles},cells={p.GasDynamics?.CellEvaluations},U={p.GasDynamics?.EnergyResidualJ:E3}")) +
            string.Join(" | ", result.Failures.Select(f => $"{f.Rpm} {f.Kind} {f.Message}")));
    }

    private static void TwoRun(string label, TwoStrokeConfiguration c)
    {
        var clock = Stopwatch.StartNew();
        long memory = GC.GetAllocatedBytesForCurrentThread();
        var result = TwoStrokeSimulator.Sweep(c, false).Runs.Single();
        var p = result.Point;
        Console.WriteLine($"2T {label} {result.Rpm}: {result.Status} T={p?.ShaftTorqueNm:F6} K={p?.PeakTemperatureK:F1} trap={p?.FreshTrappedKg:E5} " +
            $"TE={p?.TrappingEfficiency:F5} cycles={p?.Cycles} steps={p?.TotalAcceptedSteps} residual={p?.PeriodicError:E3} U={p?.Balance.EnergyJ:E3} " +
            $"E={p?.Balance.SpeciesKg.ToArray(true).Max(Math.Abs):E3} ms={clock.ElapsedMilliseconds} bytes={GC.GetAllocatedBytesForCurrentThread()-memory} {result.Failure}");
    }

    private static void MultiRuns(bool corrected)
    {
        var c = MultiConfiguration.Example();
        var basis = c.Cylinder with { Speeds = new(4000, 4000, 1), Solver = c.Cylinder.Solver with { MaximumCycles = 120 }};
        if (corrected) basis = basis with { Ducts = basis.Ducts! with { Exterior = ExteriorBoundary.PressureReservoir } };
        var x = MultiSimulator.Run(c with { Cylinder = basis }).Points.Single();
        Console.WriteLine($"multi4 corrected={corrected} {x.Status} T={x.Metrics?.BrakeTorqueNm:F6} U={x.Balance?.TotalEnergyJ:E3} ms={x.ElapsedMilliseconds:F0} {x.Detail}");
        var two = MultiTwoStrokeConfiguration.Example();
        var b = two.Cylinder with { Speeds = new(5000, 5000, 1) };
        if (corrected) b = Corrected(b);
        var p = TwoStrokeSimulator.Sweep(two with { Cylinder = b }, false).Runs.Single();
        Console.WriteLine($"multi2 corrected={corrected} {p.Status} T={p.Point?.ShaftTorqueNm:F6} U={p.Point?.Balance.EnergyJ:E3} ms={p.Milliseconds:F0} {p.Failure}");
    }
}
