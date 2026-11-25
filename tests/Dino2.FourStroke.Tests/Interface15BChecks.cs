using Dino2.Core.GasDynamics;

internal static class Interface15BChecks
{
    private static readonly double[] Perfect = [1];
    private const double R = 287, Gamma = 1.4;

    internal static void Run()
    {
        var forward = MakeMap("forward");
        var reverse = MakeMap("reverse");
        var gas = new GasEquation(1);
        double[] reservoirValues = new double[gas.Size], pipeValues = new double[gas.Size], flux = new double[gas.Size];

        var reservoir = gas.Encode(reservoirValues, Perfect, 150000, 400, 0, .7);
        var pipe = gas.Encode(pipeValues, Perfect, 100000, 320, 0, .1);
        double face = PortBoundary.MappedFlux(gas, .001, .0006, .0006, .65, "curtain_area", forward, reverse,
            true, reservoirValues, reservoir, pipeValues, pipe, flux, out var trace);
        Check(trace is { FlowFromReservoir: true, MapCd: not null, MapMassResidualKgPerS: not null, MapIterations: > 0 },
            "forward direction, accepted-state map query and scalar root");
        VerifyIndependent(gas, forward, true, reservoir, pipe, flux, trace!.Value);
        Check(face > 0 && trace.Value.MixedPressurePa > 0 && trace.Value.ThroatPressurePa > 0,
            "accepted throat and mixed pressure are physical");

        reservoir = gas.Encode(reservoirValues, Perfect, 100000, 300, 0, .2);
        pipe = gas.Encode(pipeValues, Perfect, 180000, 420, 0, .8);
        face = PortBoundary.MappedFlux(gas, .001, .0006, .0006, .65, "curtain_area", forward, reverse,
            true, reservoirValues, reservoir, pipeValues, pipe, flux, out trace);
        Check(trace is { FlowFromReservoir: false, MapCd: not null }, "reverse direction selected its independent map");
        VerifyIndependent(gas, reverse, false, reservoir, pipe, flux, trace!.Value);
        Check(face > 0, "reverse discharge returned a finite interface pressure");

        reservoir = gas.Encode(reservoirValues, Perfect, 150000, 400, 0, .7);
        pipe = gas.Encode(pipeValues, Perfect, 100000, 320, 0, .1);
        var narrow = MakeMap("forward", .98, 1);
        try
        {
            PortBoundary.MappedFlux(gas, .001, .0006, .0006, .65, "curtain_area", narrow, null,
                true, reservoirValues, reservoir, pipeValues, pipe, flux, out _);
            throw new Exception("Cd map silently extrapolated beyond its pressure-ratio coverage.");
        }
        catch (ArgumentOutOfRangeException) { }

        Console.WriteLine("PASS ports-v4 mapped Cd: implicit accepted-state pressure ratio, forward/reverse, independent mass/enthalpy checks, explicit coverage rejection");
    }

    private static DischargeMap MakeMap(string direction, double r0 = .2, double r1 = 1)
    {
        var metadata = new DischargeMapMetadata("curtain_area", "upstream_stagnation_to_downstream_static", direction,
            "opening_fraction,pressure_ratio,dimensionless", "synthetic analytic ports-v4 test", 
            "ports-v4-equivalent-capacity-v1", "synthetic");
        double Cd(double opening, double ratio) => .025 + .025 * opening + .015 * ratio;
        return new(metadata, new DischargeMapPoint[]
        {
            new(0, r0, Cd(0,r0)), new(0,r1,Cd(0,r1)),
            new(1, r0, Cd(1,r0)), new(1,r1,Cd(1,r1))
        });
    }

    private static void VerifyIndependent(GasEquation gas, DischargeMap map, bool fromReservoir,
        GasState reservoir, GasState pipe, ReadOnlySpan<double> flux, PortTrace trace)
    {
        double p0 = fromReservoir ? reservoir.Pressure : pipe.Pressure;
        double t0 = fromReservoir ? reservoir.Temperature : pipe.Temperature;
        double downstream = fromReservoir ? trace.MixedPressurePa : reservoir.Pressure;
        double cd = map.Evaluate(.65, downstream / p0);
        double ratio = downstream / p0;
        double critical = Math.Pow(2 / (Gamma + 1), Gamma / (Gamma - 1));
        double psi = ratio <= critical
            ? Math.Sqrt(Gamma) * Math.Pow(2 / (Gamma + 1), (Gamma + 1) / (2 * (Gamma - 1)))
            : Math.Sqrt(2 * Gamma / (Gamma - 1) * (Math.Pow(ratio, 2 / Gamma) - Math.Pow(ratio, (Gamma + 1) / Gamma)));
        double referenceMass = cd * .0006 * p0 / Math.Sqrt(R * t0) * psi;
        Near(Math.Abs(trace.ThroatMassKgPerS), referenceMass, Math.Max(2e-9, referenceMass * 2e-6), "independent isentropic mass reference");
        Near(Math.Abs(flux[0]), Math.Abs(trace.ThroatMassKgPerS), 1e-12, "single accepted species mass flux");
        double donor = fromReservoir ? reservoir.Density : pipe.Density;
        double donorEnergy = fromReservoir ? reservoir.Energy : pipe.Energy;
        double donorPressure = fromReservoir ? reservoir.Pressure : pipe.Pressure;
        Near(flux[gas.Energy] / trace.ThroatMassKgPerS, (donorEnergy + donorPressure) / donor,
            Math.Abs((donorEnergy + donorPressure) / donor) * .02,
            "accepted donor stagnation enthalpy");
        Check(Math.Abs(trace.MapMassResidualKgPerS ?? double.PositiveInfinity) <= 2e-9, "map solver mass residual");
    }

    private static void Near(double actual, double expected, double tolerance, string message)
    {
        if (!double.IsFinite(actual) || Math.Abs(actual - expected) > tolerance)
            throw new Exception($"{message}: {actual:R} versus {expected:R}, tolerance {tolerance:R}.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
