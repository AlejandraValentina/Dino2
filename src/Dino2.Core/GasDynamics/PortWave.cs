using Dino2.Core.FourStroke;

namespace Dino2.Core.GasDynamics;

internal readonly record struct PortState(double Pressure, double Temperature, double Speed)
{
    internal double Density(double r) => Pressure / (r * Temperature);
    internal double MassPerArea(double r) => Density(r) * Speed;
}

// Right-running wave in the coordinate pointing from the opening into the tube.
internal readonly struct PortWave(GasState gas, PortThermodynamics thermo)
{
    internal double LowerPressure => thermo.IsentropicPressure(gas.Pressure, gas.Temperature, PortThermodynamics.MinimumTemperature);

    internal double UpperPressure
    {
        get
        {
            // Solve Hugoniot for p/p1 at the hot endpoint, without extrapolating cp.
            double t = gas.Temperature, limit = PortThermodynamics.MaximumTemperature;
            double b = limit - t - 2 * thermo.EnthalpyChange(t, limit) / thermo.R;
            double d = Math.Sqrt(b * b + 4 * t * limit);
            return gas.Pressure * (b >= 0 ? 2 * limit / (d + b) : (d - b) / (2 * t));
        }
    }

    internal double SpeedFloor(double pressure) =>
        8 * (Math.Abs(Math.BitIncrement(pressure) - pressure) / (gas.Density * gas.SoundSpeed) +
            Math.Abs(Math.BitIncrement(gas.Velocity) - gas.Velocity));

    internal void Certify(PortState downstream)
    {
        double change = downstream.Pressure - gas.Pressure;
        if (change == 0) return;
        double relative = change / gas.Pressure;
        double logarithm = Math.Abs(relative) < .5 ? PortThermodynamics.LogOnePlus(relative) :
            Math.Log(downstream.Pressure / gas.Pressure);
        double entropy = thermo.EntropyChange(gas.Temperature, downstream.Temperature) - thermo.R * logarithm;
        if (!double.IsFinite(entropy) || (change < 0 ? Math.Abs(entropy) > 2e-8 * thermo.R : entropy < -2e-8 * thermo.R))
            throw new NumericalStateException($"Residuo de entropía en la onda del conducto: {entropy:R} J/(kg·K).");
    }

    internal PortState At(double pressure) => At(pressure, pressure - gas.Pressure);

    internal PortState At(double pressure, double change) => At(pressure, change, out _);

    internal PortState NearWall(PortState wall, double pressure, double delta)
    {
        At(wall.Pressure, wall.Pressure - gas.Pressure, out double slope);
        double temperature = wall.Temperature + delta * thermo.R * wall.Temperature / (wall.Pressure * thermo.Cp(wall.Temperature));
        PortThermodynamics.RequireTemperature(temperature, "onda del conducto");
        return new(pressure, temperature, delta * slope);
    }

    private PortState At(double pressure, double change, out double speedSlope)
    {
        speedSlope = 1 / (gas.Density * gas.SoundSpeed);
        if (change == 0) return new(pressure, gas.Temperature, gas.Velocity);
        if (!thermo.Perfect && Math.Abs(change) < gas.Pressure * 1e-8)
        {
            double acoustic = change / (gas.Density * gas.SoundSpeed);
            double t = gas.Temperature + change / (gas.Density * thermo.Cp(gas.Temperature));
            PortThermodynamics.RequireTemperature(t, "onda del conducto");
            return new(pressure, t, gas.Velocity + acoustic);
        }
        if (change < 0)
        {
            double log = change > -.5 * gas.Pressure
                ? PortThermodynamics.LogOnePlus(change / gas.Pressure) : Math.Log(pressure / gas.Pressure);
            double t;
            try { t = thermo.IsentropicTemperature(gas.Temperature, log); }
            catch (ModelDomainException) when (pressure == LowerPressure) { t = PortThermodynamics.MinimumTemperature; }
            speedSlope = thermo.R * t / (pressure * Math.Sqrt(thermo.SoundSquared(t)));
            double jump = thermo.Perfect
                ? 2 * gas.SoundSpeed / (gas.Gamma - 1) * PortThermodynamics.ExpMinusOne((gas.Gamma - 1) / (2 * gas.Gamma) * log)
                : thermo.AcousticIntegral(gas.Temperature, t);
            return new(pressure, t, gas.Velocity + jump);
        }
        // Hugoniot in p,T: Δh = Δp(v1+v2)/2. Its derivative at fixed p2
        // is cp(T2) − Δp R/(2 p2). The compressive branch has v2 < v1.
        double ratio = pressure / gas.Pressure, b = (gas.Gamma - 1) / (gas.Gamma + 1);
        double frozenCompression = (change / gas.Pressure) * (1 - b) / (ratio + b);
        double deltaT = gas.Temperature * (change / gas.Pressure - ratio * frozenCompression);
        if (!thermo.Perfect)
        {
            double factor = .5 * change * thermo.R / pressure;
            double heat = .5 * change / gas.Density + factor * gas.Temperature;
            bool solved = false;
            double lo = 0, hi = PortThermodynamics.MaximumTemperature - gas.Temperature;
            if (deltaT > hi)
            {
                if (pressure > UpperPressure) throw new ModelDomainException("Interfaz fuera de 200–6000 K en la onda de choque del conducto.");
                deltaT = hi;
            }
            for (int i = 0; i < 48; i++)
            {
                double t = gas.Temperature + deltaT;
                double residual = thermo.EnthalpyChange(gas.Temperature, t) - factor * deltaT - heat;
                double correction = residual / (thermo.Cp(t) - factor);
                double next = deltaT - correction;
                if (Math.Abs(correction) <= 2e-13 * t)
                {
                    if (next >= 0 && next <= hi) deltaT = next;
                    solved = true;
                    break;
                }
                if (residual < 0) lo = deltaT; else hi = deltaT;
                if (next > hi && pressure > UpperPressure)
                    throw new ModelDomainException("Interfaz fuera de 200–6000 K en la onda de choque del conducto.");
                deltaT = next > lo && next < hi ? next : lo + (hi - lo) * .5;
            }
            if (!solved) throw new NumericalStateException("No se resolvió la curva de Hugoniot de la interfaz.");
        }
        double temperature = gas.Temperature + deltaT;
        PortThermodynamics.RequireTemperature(temperature, "onda de choque del conducto");
        double v1 = 1 / gas.Density, v2 = thermo.R * temperature / pressure;
        double temperatureSlope = .5 * (v1 + v2 / ratio) / (thermo.Cp(temperature) - .5 * change * thermo.R / pressure);
        double v2Slope = (thermo.R * temperatureSlope - v2) / pressure;
        double compression = v1 * (change / gas.Pressure - deltaT / gas.Temperature) / ratio;
        double deltaSpeed = Math.Sqrt(change * compression);
        speedSlope = (compression - change * v2Slope) / (2 * deltaSpeed);
        return new(pressure, temperature, gas.Velocity + deltaSpeed);
    }

    internal PortState Wall(double frozenPressure, bool allowBound = false)
    {
        if (gas.Velocity == 0) return new(gas.Pressure, gas.Temperature, 0);
        double p = frozenPressure, lo = 0, hi = double.PositiveInfinity;
        for (int i = 0; i < 60; i++)
        {
            PortState f;
            double slope;
            try
            {
                if (p <= 0) throw new ModelDomainException("Pared fuera de dominio.");
                f = At(p, p - gas.Pressure, out slope);
            }
            catch (ModelDomainException)
            {
                bool low = gas.Velocity > 0;
                if (low) lo = LowerPressure; else hi = UpperPressure;
                p = low ? lo : hi;
                f = At(p, p - gas.Pressure, out slope);
                if (low ? f.Speed > SpeedFloor(p) : f.Speed < -SpeedFloor(p))
                    return allowBound ? f : throw new ModelDomainException("Interfaz fuera de 200–6000 K en la onda de pared.");
            }
            if (Math.Abs(f.Speed) <= SpeedFloor(p)) return f with { Speed = 0 };
            if (f.Speed < 0) lo = p; else hi = p;
            double dp = f.Speed / slope;
            double next = dp < p / 2 ? p - dp : p * Math.Exp(-dp / p);
            if (next > lo && next < hi) p = next;
            else if (double.IsFinite(hi)) p = lo + (hi - lo) * .5;
            else p = p * 2;
        }
        throw new NumericalStateException($"No se resolvió la presión de inversión de la interfaz: p={gas.Pressure:R}, u={gas.Velocity:R}, T={gas.Temperature:R}, pared={p:R}.");
    }

    internal PortState SonicOutflow()
    {
        double t = gas.Temperature * Math.Pow((2 - (gas.Gamma - 1) * gas.Velocity / gas.SoundSpeed) / (gas.Gamma + 1), 2);
        if (!thermo.Perfect)
        {
            double lo = PortThermodynamics.MinimumTemperature, hi = gas.Temperature;
            t = Math.Clamp(t, lo, hi); // initial iterate, not a trace
            for (int i = 0; i < 48; i++)
            {
                double a = Math.Sqrt(thermo.SoundSquared(t));
                double residual = gas.Velocity + thermo.AcousticIntegral(gas.Temperature, t) + a;
                if (Math.Abs(residual) <= 2e-12 * a) break;
                if (residual > 0) hi = t; else lo = t;
                double next = t - residual / (thermo.Cp(t) / a + a / (2 * t));
                if (next < PortThermodynamics.MinimumTemperature &&
                    gas.Velocity + thermo.AcousticIntegral(gas.Temperature, PortThermodynamics.MinimumTemperature) +
                    Math.Sqrt(thermo.SoundSquared(PortThermodynamics.MinimumTemperature)) > 0)
                    throw new ModelDomainException("Interfaz fuera de 200–6000 K en la rarefacción sónica del conducto.");
                t = next > lo && next < hi ? next : lo + (hi - lo) * .5;
                if (i == 47) throw new NumericalStateException("No se resolvió la rarefacción sónica de la interfaz.");
            }
        }
        PortThermodynamics.RequireTemperature(t, "onda sónica del conducto");
        return new(thermo.IsentropicPressure(gas.Pressure, gas.Temperature, t), t, -Math.Sqrt(thermo.SoundSquared(t)));
    }

    internal PortState StationaryShock()
    {
        double m = -gas.Velocity / gas.SoundSpeed;
        double p = gas.Pressure * (1 + 2 * gas.Gamma / (gas.Gamma + 1) * (m * m - 1));
        if (thermo.Perfect || p == gas.Pressure) return At(p);
        // At zero shock speed the downstream state lies on the upstream
        // Rayleigh line at the unchanged laboratory mass flux.
        double j = -gas.Density * gas.Velocity, j2 = j * j;
        double lo = gas.Pressure, hi = Math.Min(UpperPressure, gas.Pressure + j2 / gas.Density);
        if (ShockSpeed(At(hi)) < -SpeedFloor(hi))
            throw new ModelDomainException("Interfaz fuera de 200–6000 K en el choque estacionario del conducto.");
        p = Math.Min(p, hi);
        for (int i = 0; i < 64; i++)
        {
            var s = At(p);
            double residual = ShockSpeed(s);
            if (Math.Abs(residual) <= SpeedFloor(p) + 2e-11 * gas.SoundSpeed) return s;
            if (residual < 0) lo = p; else hi = p;
            p = (lo + hi) * .5;
        }
        throw new NumericalStateException("No convergió la masa del choque estacionario de la interfaz.");
    }

    internal double ShockSpeed(PortState downstream)
    {
        if (downstream.Pressure <= gas.Pressure) return gas.Velocity + gas.SoundSpeed;
        return gas.Velocity + (downstream.Pressure - gas.Pressure) /
            (gas.Density * (downstream.Speed - gas.Velocity));
    }
}
