using Dino2.Core.FourStroke;

namespace Dino2.Core.GasDynamics;

internal readonly record struct PortTrace(double ThroatPressurePa, double ThroatTemperatureK, double ThroatMach,
    double MixedPressurePa, double MixedVelocityMps, double MixedTemperatureK, int Iterations)
{
    internal double ThroatMassKgPerS { get; init; }
}

internal static class PortBoundary
{
    internal static double Flux(GasEquation gas, double totalArea, double openArea, ReadOnlySpan<double> chamber,
        GasState reservoir, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux) =>
        Flux(gas, totalArea, openArea, chamber, reservoir, edge, pipe, flux, out _);

    internal static double Flux(GasEquation gas, double totalArea, double openArea, ReadOnlySpan<double> chamber,
        GasState reservoir, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux, out PortTrace? trace)
    {
        trace = null;
        double frozenWall = gas.WallPressure(pipe, true);
        var intoTube = pipe with { Velocity = -pipe.Velocity };
        var pipeThermo = new PortThermodynamics(pipe, edge, gas.SpeciesCount);
        var wave = new PortWave(intoTube, pipeThermo);
        if (openArea <= 0)
        {
            double pressure = wave.Wall(frozenWall).Pressure;
            flux.Clear();
            flux[gas.Momentum] = pressure * totalArea;
            return pressure;
        }
        ValidateDonor(gas, reservoir, chamber, "volumen");
        ValidateDonor(gas, pipe, edge, "conducto");
        var wall = wave.Wall(frozenWall);
        if (wall.Pressure == reservoir.Pressure)
        {
            flux.Clear();
            flux[gas.Momentum] = wall.Pressure * totalArea;
            return wall.Pressure;
        }
        double alpha = Math.Min(1, openArea / totalArea);
        bool feeding = reservoir.Pressure > wall.Pressure;
        var thermo = feeding ? new PortThermodynamics(reservoir, chamber, gas.SpeciesCount) : pipeThermo;
        var (mixed, throat, iterations) = feeding ? Supply(reservoir, thermo, wave, wall.Pressure, alpha) :
            Discharge(intoTube, thermo, wave, reservoir.Pressure, wall, alpha);

        ValidatePlane(throat, thermo, "garganta", alpha, reservoir, pipe);
        ValidatePlane(mixed, thermo, "sección media", alpha, reservoir, pipe);
        var donor = feeding ? reservoir : pipe;
        var composition = feeding ? chamber : edge;
        // A single mass flux for the two domains and both planes. The mixed
        // speed is reconstructed from continuity if p cannot represent the
        // last sub-ULP pressure increment next to the wall.
        double j = alpha * throat.MassPerArea(thermo.R);
        double velocity = j / mixed.Density(thermo.R);
        double mass = -j * totalArea;
        double h0 = (donor.Energy + donor.Pressure) / donor.Density;
        if (!feeding)
            h0 += thermo.EnthalpyChange(pipe.Temperature, mixed.Temperature) + .5 * (velocity * velocity - pipe.Velocity * pipe.Velocity);
        if (!double.IsFinite(h0) || !double.IsFinite(mass * h0))
            throw new NumericalStateException("Entalpía total no finita en la interfaz.");
        for (int i = 0; i < gas.SpeciesCount; i++) flux[i] = mass * (composition[i] / donor.Density);
        flux[gas.Tag] = mass * (composition[gas.Tag] / donor.Density);
        flux[gas.Energy] = mass * h0;
        flux[gas.Momentum] = totalArea * mixed.Pressure - mass * velocity;
        trace = new(throat.Pressure, throat.Temperature, Math.Abs(throat.Speed) / Math.Sqrt(thermo.SoundSquared(throat.Temperature)),
            mixed.Pressure, -velocity, mixed.Temperature, iterations) { ThroatMassKgPerS = mass };
        return mixed.Pressure;
    }

    private static (PortState Mixed, PortState Throat, int Count) Supply(
        GasState tank, PortThermodynamics thermo, PortWave wave, double wallPressure, double alpha)
    {
        double r = thermo.R, t0 = tank.Temperature;
        double ts = thermo.SonicTemperature(t0, 0), sonic = Math.Sqrt(thermo.SoundSquared(ts));
        var critical = new PortState(thermo.IsentropicPressure(tank.Pressure, t0, ts), ts, sonic);
        PortState Isentropic(double u)
        {
            double t = t0 + thermo.TemperatureChange(t0, -.5 * u * u);
            return new(thermo.IsentropicPressure(tank.Pressure, t0, t), t, u);
        }
        PortState Expanded(PortState jet)
        {
            if (alpha == 1) return jet;
            double j = alpha * jet.MassPerArea(r), k = jet.Pressure + j * jet.Speed;
            double gamma = thermo.SoundSquared(jet.Temperature) / (r * jet.Temperature);
            double m2 = jet.Speed * jet.Speed / (gamma * r * jet.Temperature);
            double factor = 1 - alpha * m2;
            double discriminant = factor * factor + 2 * (gamma + 1) * alpha * (1 - alpha) * m2;
            double u = 2 * j * r * t0 / (k + jet.Pressure * Math.Sqrt(discriminant));
            double t = t0 + thermo.TemperatureChange(t0, -.5 * u * u);
            if (!thermo.Perfect)
            {
                double lo = 0, hi = jet.Speed;
                for (int i = 0; i < 20; i++)
                {
                    double residual = u * (k - j * u) - j * r * t;
                    if (Math.Abs(residual) <= 2e-13 * Math.Max(j * r * t, 1e-12)) break;
                    if (residual > 0) hi = u; else lo = u;
                    double next = u - residual / (k - 2 * j * u + j * r * u / thermo.Cp(t));
                    u = next > lo && next < hi ? next : (lo + hi) * .5;
                    t = t0 + thermo.TemperatureChange(t0, -.5 * u * u);
                    if (i == 19) throw new NumericalStateException("No se resolvió el momento de la expansión de puerto.");
                }
            }
            return new(k - j * u, t, u);
        }
        // Positive continuation is impossible below the wall pressure. This
        // also avoids evaluating a trial rarefaction into vacuum at tiny α.
        double Incoming(PortState f) => f.Pressure <= wallPressure ? -f.Speed : wave.At(f.Pressure).Speed - f.Speed;
        var first = Expanded(critical);
        if (Incoming(first) <= 0)
        {
            double lo = 0, hi = sonic;
            int i = 0;
            for (; i < 50 && hi - lo > 2e-11 * hi; i++)
            {
                double u = (lo + hi) * .5;
                if (Incoming(Expanded(Isentropic(u))) > 0) lo = u; else hi = u;
            }
            var jet = Isentropic((lo + hi) * .5);
            return (Expanded(jet), jet, i);
        }
        double criticalMass = alpha * critical.MassPerArea(r);
        PortState Choked(double u)
        {
            double t = t0 + thermo.TemperatureChange(t0, -.5 * u * u);
            return new(criticalMass * r * t / u, t, u);
        }
        var sonicFace = new PortState(alpha * critical.Pressure, ts, sonic);
        if (Incoming(sonicFace) >= 0) return (sonicFace, critical, 0);
        double low = first.Speed, high = sonic;
        int count = 0;
        for (; count < 42 && high - low > 2e-11 * high; count++)
        {
            double u = (low + high) * .5;
            if (Incoming(Choked(u)) > 0) low = u; else high = u;
        }
        return (Choked((low + high) * .5), critical, count);
    }

    private static (PortState Mixed, PortState Throat, int Count) Discharge(
        GasState pipe, PortThermodynamics thermo, PortWave wave, double back, PortState wall, double alpha)
    {
        double r = thermo.R;
        PortState Contracted(PortState bulk, double overBack)
        {
            double pressureLog = overBack < .5 * bulk.Pressure
                ? PortThermodynamics.LogOnePlus(-overBack / bulk.Pressure) : Math.Log(back / bulk.Pressure);
            double t = thermo.IsentropicTemperature(bulk.Temperature, pressureLog);
            double kinetic = .5 * bulk.Speed * bulk.Speed - thermo.IsentropicEnthalpyChange(bulk.Temperature, t, pressureLog);
            if (kinetic <= 0) return new(back, t, 0); // outside the physical bracket for nonzero discharge
            if (2 * kinetic <= thermo.SoundSquared(t)) return new(back, t, -Math.Sqrt(2 * kinetic));
            double ts = thermo.SonicTemperature(bulk.Temperature, .5 * bulk.Speed * bulk.Speed);
            return new(thermo.IsentropicPressure(bulk.Pressure, bulk.Temperature, ts), ts, -Math.Sqrt(thermo.SoundSquared(ts)));
        }
        double Balance(PortState face, PortState throat) => alpha * throat.MassPerArea(r) - face.MassPerArea(r);
        bool incidentSupersonic = -pipe.Velocity >= pipe.SoundSpeed;
        var minimum = incidentSupersonic ? wave.StationaryShock(wall.Pressure) : wave.SonicOutflow();
        if (incidentSupersonic && Balance(minimum, Contracted(minimum, minimum.Pressure - back)) <= 0)
        {
            var free = new PortState(pipe.Pressure, pipe.Temperature, pipe.Velocity);
            if (alpha == 1) return (free, free, 0);
            double stagnationKinetic = free.Speed * free.Speed * .5;
            double slow = Math.Sqrt(thermo.SoundSquared(thermo.SonicTemperature(free.Temperature, stagnationKinetic)));
            double fast = -pipe.Velocity, target = -free.MassPerArea(r) / alpha;
            PortState AtSpeed(double u)
            {
                double t = free.Temperature + thermo.TemperatureChange(free.Temperature, stagnationKinetic - .5 * u * u);
                return new(thermo.IsentropicPressure(free.Pressure, free.Temperature, t), t, -u);
            }
            int i = 0;
            for (; i < 42 && fast - slow > 2e-12 * fast; i++)
            {
                double u = (slow + fast) * .5;
                if (-AtSpeed(u).MassPerArea(r) > target) slow = u; else fast = u;
            }
            return (free, AtSpeed((slow + fast) * .5), i);
        }
        if (alpha == 1)
        {
            var full = back > minimum.Pressure ? wave.At(back) : minimum;
            return (full, full, 0);
        }

        // No nonzero subsonic contraction reaches a pressure higher than
        // the bulk static pressure. This tightens the bracket at reversal.
        double lo = Math.Max(0, minimum.Pressure - back), wallExcess = wall.Pressure - back, hi = wallExcess;
        double pressureChange = back - pipe.Pressure;
        PortState mixed = wall, throat = Contracted(wall, hi);
        for (int i = 1; i <= 96; i++)
        {
            double excess = lo + (hi - lo) * .5;
            if (excess == lo || excess == hi)
            {
                double precision = Math.Abs(Math.BitIncrement(mixed.Pressure) - mixed.Pressure);
                double impulse = precision / Math.Sqrt(thermo.SoundSquared(mixed.Temperature));
                if (Math.Abs(Balance(mixed, throat)) <= 32 * impulse) return (mixed, throat, i);
                throw new NumericalStateException($"Residuo de caudal del puerto {Balance(mixed, throat):R} kg/(m²·s), p={mixed.Pressure:R}, p pared={wall.Pressure:R}, p volumen={back:R}, α={alpha:R}.");
            }
            double p = back + excess;
            double fromWall = excess - wallExcess;
            mixed = -fromWall <= 1e-9 * wall.Pressure
                ? wave.NearWall(wall, p, fromWall) : wave.At(p, pressureChange + excess);
            throat = Contracted(mixed, excess);
            double discrepancy = Balance(mixed, throat);
            double scale = Math.Max(Math.Abs(mixed.MassPerArea(r)), Math.Abs(alpha * throat.MassPerArea(r)));
            if (Math.Abs(discrepancy) <= 2e-9 * scale) return (mixed, throat, i);
            if (discrepancy > 0) lo = excess; else hi = excess;
        }
        throw new NumericalStateException($"No convergió el balance local de caudal: p={mixed.Pressure:R}, p pared={wall.Pressure:R}, p volumen={back:R}, α={alpha:R}.");
    }

    private static void ValidateDonor(GasEquation equation, GasState s, ReadOnlySpan<double> values, string plane)
    {
        if (!(s.Density > 0 && s.Pressure > 0 && s.Temperature > 0 && s.SoundSpeed > 0) ||
            !double.IsFinite(s.Energy) || !double.IsFinite(s.GasConstant) || !double.IsFinite(s.Velocity))
            throw new NumericalStateException($"Estado no físico en la interfaz, plano {plane}.");
        for (int i = 0; i < equation.SpeciesCount; i++)
            if (!double.IsFinite(values[i]) || values[i] < -1e-12)
                throw new NumericalStateException($"Especie inadmisible en la interfaz, plano {plane}, especie {i}.");
        if (s.Temperature is < 200 or > 6000)
            throw new ModelDomainException($"Interfaz fuera de 200–6000 K en {plane}: {s.Temperature:G8} K, {s.Pressure:G8} Pa.");
    }

    private static void ValidatePlane(PortState state, PortThermodynamics thermo, string plane,
        double alpha, GasState reservoir, GasState pipe)
    {
        double a2 = thermo.SoundSquared(state.Temperature);
        if (!(state.Pressure > 0 && state.Temperature > 0 && a2 > 0) ||
            !double.IsFinite(state.Pressure) || !double.IsFinite(a2) || !double.IsFinite(state.Speed))
            throw new NumericalStateException($"Estado no físico en la interfaz, plano {plane}.");
        if (state.Temperature is < 200 or > 6000)
            throw new ModelDomainException($"Interfaz fuera de 200–6000 K en {plane}: {state.Temperature:G8} K, " +
                $"{state.Pressure:G8} Pa, área relativa {alpha:G6}, p volumen={reservoir.Pressure:G8} Pa, " +
                $"Mach incidente={pipe.Velocity / pipe.SoundSpeed:G6}.");
    }
}
