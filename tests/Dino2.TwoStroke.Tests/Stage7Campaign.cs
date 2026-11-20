using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Studies;
using Dino2.Core.Thermochemistry;
using Dino2.Core.TwoStroke;

internal static class Stage7Campaign
{
    private static readonly TwoStrokeConfiguration Base = TwoStrokeConfiguration.Example;
    private static readonly TwoStrokeConfiguration At5000 = Base with { Speeds = new(5000, 5000, 1) };

    internal static void Run(string campaign)
    {
        switch (campaign)
        {
            case "continuation": Continuation(); break;
            case "geometry": Geometry(); break;
            case "resolution": Resolution(); break;
            default: throw new ArgumentException($"Campaña 2T desconocida: {campaign}.");
        }
    }

    private static void Continuation()
    {
        var config = Base with { Speeds = new(7500, 10000, 500) };
        var speeds = config.Speeds.Values().ToArray();
        Console.WriteLine("path,rpm,status,cycles,last_error,previous_error,component,steps,shaft_w,trap_kg");
        void Print(string path, IEnumerable<TwoStrokeRun> results)
        {
            foreach (var run in results)
            {
                var d = run.Convergence;
                Console.WriteLine($"{path},{run.Rpm},{run.Status},{d?.Cycles},{d?.Residual:R},{d?.PreviousResidual:R},{d?.Dominant},{d?.AcceptedSteps},{run.Point?.ShaftPowerW:R},{run.Point?.FreshTrappedKg:R}");
            }
        }
        var shortBudget = config with { Numerics = config.Numerics with { MaximumCycles = 100 } };
        Print("cold_100", TwoStrokeSimulator.Sweep(shortBudget, false).Runs);
        Print("cold_160", TwoStrokeSimulator.Sweep(config, false).Runs);
        Print("ascending_160", TwoStrokeSimulator.ExploreContinuation(config, speeds));
        Print("descending_160", TwoStrokeSimulator.ExploreContinuation(config, speeds.Reverse()));
    }

    private static void Geometry()
    {
        var choices = new (string Name, TwoStrokeParameter Key, double Value)[]
        {
            ("header_190", TwoStrokeParameter.HeaderLength, 190),
            ("diffuser_290", TwoStrokeParameter.DiffuserLength, 290),
            ("belly_150", TwoStrokeParameter.BellyLength, 150),
            ("cone_220", TwoStrokeParameter.ConeLength, 220),
            ("stinger_19", TwoStrokeParameter.StingerDiameter, 19),
            ("intake_160", TwoStrokeParameter.IntakeLength, 160),
            ("exhaust_roof_30", TwoStrokeParameter.ExhaustRoof, 30),
            ("transfer_roof_42", TwoStrokeParameter.TransferRoof, 42),
            ("compression_11.5", TwoStrokeParameter.Compression, 11.5),
            ("advance_21", TwoStrokeParameter.SparkAdvance, 21)
        };
        var variants = choices.Select(x => (x.Name, Variant(At5000, x.Key, x.Value))).ToList();
        variants.Insert(0, ("base", At5000));
        variants.Add(("tail_330K", At5000 with { Tail = At5000.Tail with { TemperatureK = 330 } }));
        variants.Add(("extraction_0.75", At5000 with { FreshExtractionRatio = .75 }));
        int[] speeds = [4000, 5000, 7500];
        var rows = new string[variants.Count * speeds.Length];
        Parallel.For(0, rows.Length, new ParallelOptions { MaxDegreeOfParallelism = 3 }, i =>
        {
            var (name, c) = variants[i / speeds.Length];
            int rpm = speeds[i % speeds.Length];
            var r = TwoStrokeSimulator.Sweep(c with { Speeds = new(rpm, rpm, 1) }).Runs.Single();
            if (r.Point is not { } p) { rows[i] = $"{name},{rpm},{r.Status}"; return; }
            double open = c.Exhaust.Opening(c.Geometry), close = 360 - open;
            var exchange = p.Trace.Where(s => s.AngleDeg >= open && s.AngleDeg <= close).ToArray();
            var returnPeak = exchange.Where(s => s.AngleDeg >= 180).MaxBy(s => s.ExhaustPortPressurePa);
            double tOpening = exchange[0].CylinderTemperatureK;
            rows[i] = FormattableString.Invariant($"{name},{rpm},{r.Status},{p.ShaftPowerW:R},{p.ShaftTorqueNm:R},{p.FreshDeliveredKg * 1e6:R},{p.FreshTrappedKg * 1e6:R},{p.NetFreshExhaustKg * 1e6:R},{p.FreshTransferReturnKg * 1e6:R},{p.ResidualMassKg * 1e6:R},{exchange.Min(s => s.ExhaustPortPressurePa) / 1000:R},{returnPeak.ExhaustPortPressurePa / 1000:R},{returnPeak.AngleDeg:R},{tOpening:R},{p.Cycles},{p.Balance.EnergyJ:R}");
        });
        Console.WriteLine("case,rpm,status,shaft_w,torque_nm,delivered_mg,trapped_mg,net_exhaust_mg,transfer_return_mg,residual_mg,min_port_kpa,post_bdc_peak_kpa,post_bdc_peak_deg,cylinder_evo_k,cycles,energy_balance_j");
        foreach (string row in rows) Console.WriteLine(row);
    }

    private static void Resolution()
    {
        var a = At5000;
        var b = Variant(a, TwoStrokeParameter.DiffuserLength, 290);
        var cases = new (string Name, TwoStrokeConfiguration A, TwoStrokeConfiguration B)[]
        {
            ("baseline", a, b),
            ("time_half", HalfTime(a), HalfTime(b)),
            ("period_tighter", Tighten(a), Tighten(b)),
            ("mesh_time_2x", TwoStrokeStudy.Refine(a, 2), TwoStrokeStudy.Refine(b, 2)),
            ("mesh_time_4x", TwoStrokeStudy.Refine(a, 4), TwoStrokeStudy.Refine(b, 4)),
            ("extraction_0.75", a with { FreshExtractionRatio = .75 }, b with { FreshExtractionRatio = .75 })
        };
        var levels = new StudyLevel?[3];
        Console.WriteLine("case,a_nm,b_nm,delta_nm,delta_w,a_trap_mg,b_trap_mg,a_cycles,b_cycles");
        foreach (var (name, ca, cb) in cases)
        {
            var pa = TwoStrokeSimulator.Simulate(ca, 5000, false);
            var pb = TwoStrokeSimulator.Simulate(cb, 5000, false);
            double delta = pb.ShaftTorqueNm - pa.ShaftTorqueNm;
            Console.WriteLine($"{name},{pa.ShaftTorqueNm:R},{pb.ShaftTorqueNm:R},{delta:R},{pb.ShaftPowerW - pa.ShaftPowerW:R},{pa.FreshTrappedKg * 1e6:R},{pb.FreshTrappedKg * 1e6:R},{pa.Cycles},{pb.Cycles}");
            int index = name switch { "baseline" => 0, "mesh_time_2x" => 1, "mesh_time_4x" => 2, _ => -1 };
            if (index >= 0) levels[index] = new(1 << index, pb.ShaftTorqueNm, pa.ShaftTorqueNm, delta, null, null, null);
        }
        var (verdict, band) = StudyVerification.Classify(levels.Select(l => l!).ToArray());
        Console.Error.WriteLine($"{verdict}, band={band:R} N·m");

        static TwoStrokeConfiguration HalfTime(TwoStrokeConfiguration c) => c with
        {
            Numerics = c.Numerics with { Courant = c.Numerics.Courant / 2, MaximumStepDeg = c.Numerics.MaximumStepDeg / 2 }
        };
        static TwoStrokeConfiguration Tighten(TwoStrokeConfiguration c) => c with
        {
            Numerics = c.Numerics with { PeriodicTolerance = c.Numerics.PeriodicTolerance / 5 }
        };
    }

    private static TwoStrokeConfiguration Variant(TwoStrokeConfiguration basis, TwoStrokeParameter parameter, double value) =>
        TwoStrokeStudy.Create(new(basis, [new(parameter, value, value, 1)], new(ObjectiveKind.PeakTorque))).Variants[1].Configuration;

    internal static void CheckAnalyticLimits()
    {
        // A homogeneous tank at fixed mass, supplied only with fresh mixture, has f = 1 - exp(-delivery).
        static double Step(double f, double h, double k)
        {
            double Slope(double x) => 1 - TwoStrokeSimulator.ExhaustFreshFraction(x, k);
            return f + h * (Slope(f) + Slope(f + h * Slope(f))) / 2;
        }
        double perfect = 0, first = 0, second = 0;
        for (int i = 0; i < 2000; i++)
        {
            perfect = Step(perfect, .001, 1);
            first = Step(first, .001, .75);
            second = Step(second, .001, 1.25);
        }
        if (Math.Abs(perfect - (1 - Math.Exp(-2))) > 1e-7 || !(first > perfect && perfect > second))
            throw new Exception("Analytical mixing and preferential-extraction limits.");

        var gas = new GasEquation(SpeciesThermodynamics.ExtendedCount);
        double[] tag = [.999, .9, .65, .3, .01, .001, 0, 0];
        var fractions = new double[] { .06, .21, .73, 0, 0, 0, 0 };
        double[] Faces(bool variation)
        {
            var mesh = new DuctMesh(new(1, .04, .04, 8), gas, DuctScheme.Muscl, boundedPassiveTag: true);
            var states = new double[mesh.Size];
            for (int i = 0; i < mesh.Cells; i++)
                gas.Encode(states.AsSpan(i * gas.Size, gas.Size), fractions, 1e5 + 200 * i * i, 300 + i, 0,
                    variation ? tag[i] : .5);
            mesh.Prepare(states);
            return Enumerable.Range(0, 2 * mesh.Cells).Select(face =>
            {
                var reconstructed = mesh.FaceValues(face);
                var decoded = gas.Decode(reconstructed);
                double f = reconstructed[gas.Tag] / decoded.Density;
                if (f < -2e-15 || f > 1 + 2e-15) throw new Exception("Unbounded passive reconstruction.");
                return decoded.Pressure;
            }).ToArray();
        }
        if (!Faces(true).SequenceEqual(Faces(false))) throw new Exception("An inert marker altered reconstructed pressure.");
        Console.WriteLine("2T analytical verification: perfect mixing and bounded passive reconstruction.");
    }
}
