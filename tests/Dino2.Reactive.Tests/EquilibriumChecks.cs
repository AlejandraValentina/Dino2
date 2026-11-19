using System.Text.Json;
using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;
using Dino2.Storage;

internal static class EquilibriumChecks
{
    private const string Output = "artifacts/stage3";
    private static readonly FourStrokeConfiguration Example = ThermochemicalOptions.EquilibriumExample;
    private static readonly FourStrokeConfiguration Single = Example with { Speeds = new(3000, 3000, 1) };
    private static FourStrokePoint? baseline;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Near(double actual, double expected, double tolerance) =>
        Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance,
            $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}");

    private static void Save(string path, object data)
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, path), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void ClosedVessels()
    {
        var cases = new List<object>();
        foreach (double phi in new[] { .8, .95, 1 })
        foreach (var (t, p) in new[] { (300.0, 1e5), (600, 2e6), (800, 3e6) })
        {
            double afr = SpeciesThermodynamics.StoichiometricAfr / phi;
            double[] fresh = SpeciesThermodynamics.FreshMixture(afr);
            double[] complete = [.. SpeciesThermodynamics.ExhaustMixture(afr), 0, 0];
            double volume = SpeciesThermodynamics.GasConstant(fresh) * t / p;
            double energy = SpeciesThermodynamics.Energy(fresh, t);
            double[] composition = new double[7];
            double temperature = ProductEquilibrium.Solve(complete, energy, volume, composition);
            double pressure = SpeciesThermodynamics.GasConstant(composition) * temperature / volume;
            double frozenT = SpeciesThermodynamics.Temperature(complete, energy);
            Require(temperature < frozenT && composition[5] > 0 && composition[6] > 0 &&
                composition.All(x => x >= 0), "Unexpected dissociation");
            Near(composition.Sum(), 1, 2e-13);
            Near(SpeciesThermodynamics.Energy(composition, temperature), energy, 2e-6);
            for (int e = 0; e < 4; e++)
                Near(Enumerable.Range(0, 7).Sum(i => (composition[i] - complete[i]) * SpeciesThermodynamics.ElementMassFraction(i, e)), 0, 2e-13);
            var recombined = new double[7];
            double cooledEnergy = SpeciesThermodynamics.Energy(complete, 950);
            double cooled = ProductEquilibrium.Solve(composition, cooledEnergy, volume * 8, recombined);
            Near(cooled, 950, .01);
            Require(recombined[5] < 1e-4 * composition[5], "Expansion did not allow recombination");
            cases.Add(new { phi, t, p, volume, energy, temperature, pressure, frozenT, composition });
        }
        Save("closed-vessels.json", cases);
    }

    internal static void Cycle()
    {
        var result = EngineSimulation.Run(Single);
        var point = result.Points.Single();
        baseline = point;
        Require(result.Model == ThermochemicalOptions.EquilibriumModelId, "Wrong model");
        Near(point.NetWorkJ, 472.636, .02);
        Near(point.MassResidualKg, 0, 1e-13);
        Near(point.EnergyResidualJ, 0, 1e-7);
        Near(point.Chemistry!.MaximumSpeciesResidualKg, 0, 1e-13);
        Near(point.Chemistry.MaximumElementResidualKg, 0, 1e-13);
        Require(point.PeriodicError < Single.Solver.PeriodicTolerance, "Nonperiodic cycle");
        foreach (var state in point.Trace)
        {
            double[] masses = state.Composition.ToArray(true);
            Near(masses.Sum(), state.MassKg, 1e-14);
            Near(SpeciesThermodynamics.Energy(masses, state.TemperatureK), state.InternalEnergyJ, 1e-8);
        }
        var peakCo = point.Trace.MaxBy(s => s.Composition.CarbonMonoxideKg)!;
        var evo = point.Trace.Single(s => s.AngleDeg == Single.ExhaustValve.OpensDeg);
        Require(peakCo.AngleDeg < evo.AngleDeg && peakCo.Composition.CarbonMonoxideKg > 10 * evo.Composition.CarbonMonoxideKg,
            "Trace does not show closed expansion recombination");
        Require(point.Trace.Last().Composition.CarbonMonoxideKg > 0, "CO not transported into next cycle");
        double work = 0;
        for (int i = 1; i < point.Trace.Count; i++)
        {
            var a = point.Trace[i - 1]; var b = point.Trace[i];
            work += .5 * (a.PressurePa + b.PressurePa) * (b.VolumeM3 - a.VolumeM3);
        }
        Near(work, point.NetWorkJ, .03);
        FourStrokeFiles.ExportTrace(Path.Combine(Output, "v3-3000.trace.csv"), result, 3000);
        var reference = EngineSimulation.Run(Single with { Thermochemistry = ThermochemicalOptions.Default });
        FourStrokeFiles.ExportTrace(Path.Combine(Output, "v2-3000.trace.csv"), reference, 3000);
        var path = Path.Combine(Output, "v2-v3-project.json");
        FourStrokeFiles.Save(path, new(Single, reference.Configuration));
        var read = FourStrokeFiles.Load(path);
        Require(read.Current == Single && read.Original == reference.Configuration, "Model provenance lost");
        Near(EngineComparison.Compare(reference, result).Single().PowerDifferenceW,
            point.BrakePowerW - reference.Points.Single().BrakePowerW, 0);
    }

    internal static void Convergence()
    {
        var entries = new List<object>();
        foreach (double step in new[] { 1, .5, .25, .125 })
        {
            var settings = Single.Solver with { MaximumStepDeg = step, RelativeTolerance = 2e-8, PeriodicTolerance = 1e-8 };
            var point = EngineSimulation.Run(Single with { Solver = settings }).Points.Single();
            Near(point.NetWorkJ, baseline!.NetWorkJ, .005);
            Near(point.PeakPressurePa, baseline.PeakPressurePa, 10000);
            Require(point.PeriodicError < settings.PeriodicTolerance, "Periodic refinement failed");
            entries.Add(new { step, point.NetWorkJ, point.PeakPressurePa, point.PeakTemperatureK, point.PeriodicError, point.Cycles });
        }
        Save("convergence.json", entries);
    }

    internal static void Sensitivities()
    {
        var previous = Example with { Thermochemistry = ThermochemicalOptions.Default };
        var v1 = EngineSimulation.Run(previous with { Thermochemistry = null });
        var v2 = EngineSimulation.Run(previous);
        var v3 = EngineSimulation.Run(Example);
        FourStrokeFiles.Export(Path.Combine(Output, "v1-matched.csv"), v1);
        FourStrokeFiles.Export(Path.Combine(Output, "v2-matched.csv"), v2);
        FourStrokeFiles.Export(Path.Combine(Output, "v3.csv"), v3);
        FourStrokeFiles.ExportComparison(Path.Combine(Output, "v2-v3.comparison.csv"), v2, v3);
        double[] times = new double[3];
        long[] bytes = new long[3];
        for (int i = 0; i < times.Length; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            times[i] = EngineSimulation.Run(Example).ElapsedMilliseconds;
            bytes[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Save("benchmark.json", new { times, bytes, rhs = v3.Points.Sum(p => p.RhsEvaluations) });

        var rows = new List<object>();
        var refinement = new List<object>();
        var fine = Example.Solver with { MaximumStepDeg = .25, RelativeTolerance = 2e-8, PeriodicTolerance = 1e-8 };
        var at3500 = new RpmRange(3500, 3500, 1);
        var fineBase = EngineSimulation.Run(Example with { Speeds = at3500, Solver = fine }).Points.Single();
        foreach (var (name, variant) in new (string, FourStrokeConfiguration)[]
        {
            ("spark5deg", Example with { Combustion = Example.Combustion with { SparkAdvanceDeg = 5 } }),
            ("compression12", Example with { Geometry = Example.Geometry with { CompressionRatio = 12 } }),
            ("intakeLift4mm", Example with { IntakeValve = Example.IntakeValve with { MaximumLiftM = .004 } }),
            ("duration+10%", Example with { Combustion = Example.Combustion with { DurationS = 1.1 * Example.Combustion.DurationS } }),
            ("turbulence+20%", Example with { Thermochemistry = Example.Thermochemistry! with { TurbulenceToPistonSpeed = .36 } }),
            ("conversion95%", Example with { Combustion = Example.Combustion with { HeatReleaseEfficiency = .95 } }),
            ("Hohenberg+20%", Example with { Thermal = Example.Thermal with { HohenbergMultiplier = 1.2 } }),
            ("frictionConstant+20kPa", Example with { Friction = Example.Friction with { ConstantPa = Example.Friction.ConstantPa + 20000 } })
        })
        {
            var speeds = new RpmRange(1000, 6000, 2500);
            FourStrokePoint? resultAt3500 = null;
            foreach (var config in new[] { variant with { Thermochemistry = variant.Thermochemistry! with { Products = ProductChemistry.Frozen } }, variant })
            {
                var result = EngineSimulation.Run(config with { Speeds = speeds });
                foreach (var point in result.Points)
                {
                    var reference = config.Thermochemistry!.UsesEquilibrium ? v3 : v2;
                    if (ReferenceEquals(reference, v3) && point.Rpm == 3500) resultAt3500 = point;
                    var a = reference.Points.Single(p => p.Rpm == point.Rpm);
                    Near(point.EnergyResidualJ, 0, 1e-7);
                    Near(point.Chemistry!.MaximumElementResidualKg, 0, 1e-13);
                    if (name == "frictionConstant+20kPa")
                    {
                        Near(point.NetWorkJ, a.NetWorkJ, 0);
                        Near(point.BrakePowerW - a.BrakePowerW, -20000 * config.Geometry.DisplacementM3 * point.Rpm / 120, 1e-9);
                    }
                    rows.Add(new { name, result.Model, point.Rpm, deltaBrakeW = point.BrakePowerW - a.BrakePowerW,
                        deltaWorkJ = point.NetWorkJ - a.NetWorkJ, point.PeakTemperatureK, point.PeakPressurePa, point.WallHeatJ,
                        point.Chemistry.BurnDurationDeg, point.Chemistry.Ca50Deg });
                }
            }
            var finePoint = EngineSimulation.Run(variant with { Speeds = at3500, Solver = fine }).Points.Single();
            var coarsePoint = resultAt3500 ?? throw new InvalidOperationException("Missing variant result at 3500 RPM");
            double coarseDelta = coarsePoint.BrakePowerW - v3.Points.Single(p => p.Rpm == 3500).BrakePowerW;
            double fineDelta = finePoint.BrakePowerW - fineBase.BrakePowerW;
            Near(coarseDelta, fineDelta, .1);
            refinement.Add(new { name, coarseDelta, fineDelta, errorW = coarseDelta - fineDelta });
        }
        Save("sensitivities.json", rows);
        Save("sensitivity-refinement.json", refinement);
    }
}
