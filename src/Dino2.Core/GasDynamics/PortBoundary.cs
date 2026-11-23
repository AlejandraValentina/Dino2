namespace Dino2.Core.GasDynamics;

internal readonly record struct PortTrace(double ThroatPressurePa, double ThroatTemperatureK, double ThroatMach,
    double MixedPressurePa, double MixedVelocityMps, double MixedTemperatureK, int Iterations);

// Compact contraction and mixing with frozen gamma for wave matching.
// Tube coordinate points toward the 0D volume. The algebra below uses its opposite.
internal static class PortBoundary
{
    private const int Iterations = 24;
    private readonly record struct Flow(double Pressure, double Temperature, double Speed)
    {
        public double MassPerArea(double r) => Pressure * Speed / (r * Temperature);
    }

    internal static double Flux(GasEquation gas, double totalArea, double openArea, ReadOnlySpan<double> chamber,
        GasState reservoir, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux) =>
        Flux(gas, totalArea, openArea, chamber, reservoir, edge, pipe, flux, out _);

    internal static double Flux(GasEquation gas, double totalArea, double openArea, ReadOnlySpan<double> chamber,
        GasState reservoir, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux, out PortTrace? trace)
    {
        trace = null;
        if (openArea >= totalArea)
        {
            double pressure = ReservoirBoundary.RightFlux(gas, chamber, reservoir, edge, pipe, flux);
            for (int i = 0; i < flux.Length; i++) flux[i] *= totalArea;
            return pressure;
        }
        double wall = gas.WallPressure(pipe, true);
        if (openArea <= 0 || wall == reservoir.Pressure)
        {
            flux.Clear();
            flux[gas.Momentum] = wall * totalArea;
            return wall;
        }

        var inward = pipe with { Velocity = -pipe.Velocity };
        var apertureRatio = openArea / totalArea;
        bool feeding = reservoir.Pressure > wall;
        var (mixed, throat, iterations) = feeding ? Supply(reservoir, inward, apertureRatio) :
            Discharge(inward, reservoir.Pressure, wall, apertureRatio);
        GasState donor = feeding ? reservoir : pipe;
        var composition = feeding ? chamber : edge;
        double mass = -mixed.MassPerArea(donor.GasConstant) * totalArea;
        for (int i = 0; i < gas.SpeciesCount; i++) flux[i] = mass * (composition[i] / donor.Density);
        flux[gas.Tag] = mass * (composition[gas.Tag] / donor.Density);
        double h0 = (donor.Energy + donor.Pressure) / donor.Density;
        if (!feeding)
        {
            // Upwind enthalpy at the mixed section on the downstream side of the pipe wave.
            // Anchor the frozen-gamma increment at the thermal/chemical datum of the donor.
            double cp = donor.Gamma * donor.GasConstant / (donor.Gamma - 1);
            h0 += cp * (mixed.Temperature - donor.Temperature) +
                .5 * (mixed.Speed * mixed.Speed - donor.Velocity * donor.Velocity);
        }
        flux[gas.Energy] = mass * h0;
        flux[gas.Momentum] = totalArea * mixed.Pressure - mass * mixed.Speed;
        trace = new(throat.Pressure, throat.Temperature,
            Math.Abs(throat.Speed) / Math.Sqrt(donor.Gamma * donor.GasConstant * throat.Temperature),
            mixed.Pressure, -mixed.Speed, mixed.Temperature, iterations);
        return mixed.Pressure;
    }

    private static (Flow Mixed, Flow Throat, int Count) Supply(GasState tank, GasState pipe, double alpha)
    {
        double gamma = tank.Gamma, r = tank.GasConstant, t0 = tank.Temperature;
        double cp = gamma * r / (gamma - 1), exponent = gamma / (gamma - 1);
        double sonicSpeed = Math.Sqrt(2 * gamma * r * t0 / (gamma + 1));
        Flow Isentropic(double u)
        {
            double t = t0 - u * u / (2 * cp);
            return new(tank.Pressure * Math.Pow(t / t0, exponent), t, u);
        }
        Flow Expanded(Flow jet)
        {
            double j = alpha * jet.MassPerArea(r);
            // Bernoulli in the throat followed by a sudden expansion with pressure p_throat
            // on the shoulder. K is momentum flux per unit area downstream of the shoulder.
            double k = jet.Pressure + j * jet.Speed;
            double q = j * r * t0;
            double machSquared = jet.Speed * jet.Speed / (gamma * r * jet.Temperature);
            double factor = 1 - alpha * machSquared;
            double discriminant = factor * factor + 2 * (gamma + 1) * alpha * (1 - alpha) * machSquared;
            double u = 2 * q / (k + jet.Pressure * Math.Sqrt(discriminant));
            return new(k - j * u, t0 - u * u / (2 * cp), u);
        }
        double Incoming(Flow f) => ReservoirBoundary.PipeVelocity(pipe, f.Pressure) - f.Speed;
        var critical = Isentropic(sonicSpeed);
        var first = Expanded(critical);
        if (Incoming(first) <= 0)
        {
            double lo = 0, hi = sonicSpeed;
            for (int i = 0; i < Iterations; i++)
            {
                double u = (lo + hi) * .5;
                if (Incoming(Expanded(Isentropic(u))) > 0) lo = u; else hi = u;
            }
            var jet = Isentropic((lo + hi) * .5);
            return (Expanded(jet), jet, Iterations);
        }

        // Once choked the jet delivers a fixed mass and stagnation enthalpy. Back pressure
        // can still change the bulk trace until its incoming characteristic becomes sonic.
        double criticalMass = alpha * critical.MassPerArea(r);
        Flow Choked(double u)
        {
            double t = t0 - u * u / (2 * cp);
            return new(criticalMass * r * t / u, t, u);
        }
        var sonic = Choked(sonicSpeed);
        if (Incoming(sonic) >= 0) return (sonic, critical, 0);
        double low = first.Speed, high = sonicSpeed;
        for (int i = 0; i < Iterations; i++)
        {
            double u = (low + high) * .5;
            if (Incoming(Choked(u)) > 0) low = u; else high = u;
        }
        return (Choked((low + high) * .5), critical, Iterations);
    }

    private static (Flow Mixed, Flow Throat, int Count) Discharge(GasState pipe, double back, double wall, double alpha)
    {
        double gamma = pipe.Gamma, r = pipe.GasConstant;
        double g = (gamma - 1) / gamma, cp = r / g;
        double shock = (gamma - 1) / (gamma + 1);
        double pressureRatio = Math.Pow(2 / (gamma + 1), 1 / g);
        Flow AlongWave(double p)
        {
            double ratio = p / pipe.Pressure;
            double t = pipe.Temperature * (ratio <= 1 ? Math.Pow(ratio, g) :
                ratio * (1 + shock * ratio) / (ratio + shock));
            return new(p, t, ReservoirBoundary.PipeVelocity(pipe, p));
        }
        Flow Contracted(Flow bulk)
        {
            double t0 = bulk.Temperature + bulk.Speed * bulk.Speed / (2 * cp);
            double p0 = bulk.Pressure * Math.Pow(t0 / bulk.Temperature, 1 / g);
            double p = Math.Max(p0 * pressureRatio, Math.Min(back, p0));
            double t = t0 * Math.Pow(p / p0, g);
            return new(p, t, -Math.Sqrt(2 * cp * (t0 - t)));
        }
        double Balance(Flow f) => alpha * Contracted(f).MassPerArea(r) - f.MassPerArea(r);
        double mach = -pipe.Velocity / pipe.SoundSpeed;
        double pMin = mach >= 1
            ? pipe.Pressure * (1 + 2 * gamma / (gamma + 1) * (mach * mach - 1))
            : pipe.Pressure * Math.Pow((2 + (gamma - 1) * mach) / (gamma + 1), 2 / g);
        if (mach >= 1 && Balance(AlongWave(pMin)) <= 0)
        {
            // A stationary normal shock at the entrance would leave more capacity than
            // the incoming supersonic stream needs. Keep the upstream duct trace and find
            // the supersonic throat carrying that same mass using the unshocked stagnation state.
            var free = new Flow(pipe.Pressure, pipe.Temperature, pipe.Velocity);
            double t0 = free.Temperature + free.Speed * free.Speed / (2 * cp);
            double slow = Math.Sqrt(2 * gamma * r * t0 / (gamma + 1)), fast = -pipe.Velocity;
            double target = -free.MassPerArea(r) / alpha;
            Flow AtSpeed(double u)
            {
                double t = t0 - u * u / (2 * cp);
                return new(free.Pressure * Math.Pow(t / free.Temperature, 1 / g), t, -u);
            }
            for (int i = 0; i < Iterations; i++)
            {
                double u = (slow + fast) * .5;
                if (-AtSpeed(u).MassPerArea(r) > target) slow = u; else fast = u;
            }
            return (free, AtSpeed((slow + fast) * .5), Iterations);
        }
        double lo = pMin, hi = wall;
        for (int i = 0; i < Iterations; i++)
        {
            double p = (lo + hi) * .5;
            if (Balance(AlongWave(p)) > 0) lo = p; else hi = p;
        }
        var mixed = AlongWave((lo + hi) * .5);
        return (mixed, Contracted(mixed), Iterations);
    }
}
