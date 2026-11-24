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
    internal PortState At(double pressure) => At(pressure, pressure - gas.Pressure);

    internal PortState At(double pressure, double change) => At(pressure, change, out _);

    internal PortState NearWall(PortState wall, double pressure, double delta)
    {
        At(wall.Pressure, wall.Pressure - gas.Pressure, out double slope);
        return new(pressure, wall.Temperature + delta * thermo.R * wall.Temperature / (wall.Pressure * thermo.Cp(wall.Temperature)), delta * slope);
    }

    private PortState At(double pressure, double change, out double speedSlope)
    {
        speedSlope = 1 / (gas.Density * gas.SoundSpeed);
        if (change == 0) return new(pressure, gas.Temperature, gas.Velocity);
        if (!thermo.Perfect && Math.Abs(change) < gas.Pressure * 1e-8)
        {
            double acoustic = change / (gas.Density * gas.SoundSpeed);
            return new(pressure, gas.Temperature + change / (gas.Density * thermo.Cp(gas.Temperature)), gas.Velocity + acoustic);
        }
        if (change < 0)
        {
            double log = change > -.5 * gas.Pressure
                ? PortThermodynamics.LogOnePlus(change / gas.Pressure) : Math.Log(pressure / gas.Pressure);
            double t = thermo.IsentropicTemperature(gas.Temperature, log);
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
            for (int i = 0; i < 14; i++)
            {
                double t = gas.Temperature + deltaT;
                double correction = (thermo.EnthalpyChange(gas.Temperature, t) - factor * deltaT - heat) / (thermo.Cp(t) - factor);
                deltaT -= correction;
                if (Math.Abs(correction) <= 2e-13 * t) { solved = true; break; }
            }
            if (!solved) throw new NumericalStateException("No se resolvió la curva de Hugoniot de la interfaz.");
        }
        double temperature = gas.Temperature + deltaT;
        double v1 = 1 / gas.Density, v2 = thermo.R * temperature / pressure;
        double temperatureSlope = .5 * (v1 + v2 / ratio) / (thermo.Cp(temperature) - .5 * change * thermo.R / pressure);
        double v2Slope = (thermo.R * temperatureSlope - v2) / pressure;
        double compression = v1 * (change / gas.Pressure - deltaT / gas.Temperature) / ratio;
        double deltaSpeed = Math.Sqrt(change * compression);
        speedSlope = (compression - change * v2Slope) / (2 * deltaSpeed);
        return new(pressure, temperature, gas.Velocity + deltaSpeed);
    }

    internal PortState Wall(double frozenPressure)
    {
        if (gas.Velocity == 0) return new(gas.Pressure, gas.Temperature, 0);
        if (frozenPressure == 0) return new(0, 0, 0); // vacuum at an isolated wall
        if (thermo.Perfect)
        {
            var f = At(frozenPressure);
            return f with { Speed = 0 };
        }
        double p = frozenPressure;
        for (int i = 0; i < 25; i++)
        {
            var f = At(p, p - gas.Pressure, out double slope);
            if (Math.Abs(f.Speed) <= 8e-16 * gas.SoundSpeed) return f with { Speed = 0 };
            double dp = f.Speed / slope;
            if (Math.Abs(dp) <= 4e-13 * p) return f with { Speed = 0 };
            // Newton with the local impedance as slope. Advance in log pressure
            // when the linear step would leave the positive domain.
            double next = dp < p / 2 ? p - dp : p * Math.Exp(-dp / p);
            if (next == p) return f with { Speed = 0 };
            p = next;
        }
        throw new NumericalStateException($"No se resolvió la presión de inversión de la interfaz: p={gas.Pressure:R}, u={gas.Velocity:R}, T={gas.Temperature:R}, pared={p:R}.");
    }

    internal PortState SonicOutflow()
    {
        double t = gas.Temperature * Math.Pow((2 - (gas.Gamma - 1) * gas.Velocity / gas.SoundSpeed) / (gas.Gamma + 1), 2);
        if (!thermo.Perfect)
        {
            for (int i = 0; i < 16; i++)
            {
                double a = Math.Sqrt(thermo.SoundSquared(t));
                double step = (gas.Velocity + thermo.AcousticIntegral(gas.Temperature, t) + a) /
                    (thermo.Cp(t) / a + a / (2 * t));
                t -= step;
                if (Math.Abs(step) <= 2e-13 * gas.Temperature) break;
                if (i == 15) throw new NumericalStateException("No se resolvió la rarefacción sónica de la interfaz.");
            }
        }
        return new(thermo.IsentropicPressure(gas.Pressure, gas.Temperature, t), t, -Math.Sqrt(thermo.SoundSquared(t)));
    }

    internal PortState StationaryShock(double wallPressure)
    {
        double m = -gas.Velocity / gas.SoundSpeed;
        double p = gas.Pressure * (1 + 2 * gas.Gamma / (gas.Gamma + 1) * (m * m - 1));
        if (thermo.Perfect || p == gas.Pressure) return At(p);
        // At zero shock speed the downstream state lies on the upstream
        // Rayleigh line at the unchanged laboratory mass flux.
        double j = -gas.Density * gas.Velocity, j2 = j * j;
        double lo = gas.Pressure, hi = Math.Min(wallPressure, gas.Pressure + j2 / gas.Density);
        for (int i = 0; i < 40; i++)
        {
            var s = At(p);
            double residual = (s.Density(thermo.R) * s.Speed + j) / j;
            if (Math.Abs(residual) < 2e-11) return s;
            if (residual < 0) lo = p; else hi = p;
            p = (lo + hi) * .5;
        }
        return At(p);
    }
}
