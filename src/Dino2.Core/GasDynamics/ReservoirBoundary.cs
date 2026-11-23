namespace Dino2.Core.GasDynamics;

// The pipe coordinate points inward. Pressure is static for discharge and stagnation for incoming gas.
// Wave matching freezes gamma at the respective reference states. The advected total enthalpy
// of inflow is the exterior value, including the chemical datum.
internal static class ReservoirBoundary
{
    internal static double Flux(GasEquation equation, ExteriorBoundary mode, ReadOnlySpan<double> exterior,
        GasState ambient, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux, bool allowUpstreamShock = false)
    {
        if (mode != ExteriorBoundary.PressureReservoir)
        {
            equation.Flux(exterior, ambient, edge, pipe, flux);
            return pipe.Pressure;
        }
        if (pipe.Velocity <= -pipe.SoundSpeed &&
            (!allowUpstreamShock || !ShockEntersPipe(pipe, ambient.Pressure)))
        {
            equation.PhysicalFlux(edge, pipe, flux);
            return pipe.Pressure;
        }

        Span<double> face = stackalloc double[equation.Size];
        Span<double> fractions = stackalloc double[equation.SpeciesCount];
        double pressure = ambient.Pressure, velocity = PipeVelocity(pipe, pressure);
        double temperature;
        var donor = pipe;
        var values = edge;
        bool inflow = velocity > 0;
        if (inflow)
        {
            donor = ambient;
            values = exterior;
            double gamma = ambient.Gamma;
            double cp = gamma * ambient.GasConstant / (gamma - 1);
            double lo = 0, hi = Math.Sqrt(2 * gamma * ambient.GasConstant * ambient.Temperature / (gamma + 1));
            hi = Math.Min(hi, velocity);
            for (int i = 0; i < 18 && hi - lo > 1e-6; i++)
            {
                double speed = (lo + hi) / 2;
                double p = pressure * Math.Pow(1 - speed * speed / (2 * cp * ambient.Temperature), gamma / (gamma - 1));
                if (PipeVelocity(pipe, p) > speed) lo = speed;
                else hi = speed;
            }
            velocity = (lo + hi) / 2;
            temperature = ambient.Temperature - velocity * velocity / (2 * cp);
            pressure *= Math.Pow(temperature / ambient.Temperature, gamma / (gamma - 1));
        }
        else
        {
            double gamma = pipe.Gamma;
            double g = (gamma - 1) / gamma;
            temperature = pipe.Temperature * Math.Pow(pressure / pipe.Pressure, g);
            if (pressure > pipe.Pressure)
            {
                double ratio = pressure / pipe.Pressure;
                double b = (gamma - 1) / (gamma + 1);
                temperature = pipe.Temperature * ratio * (1 + b * ratio) / (ratio + b);
            }
            else if (velocity < -Math.Sqrt(gamma * pipe.GasConstant * temperature))
            {
                double a = (2 * pipe.SoundSpeed - (gamma - 1) * pipe.Velocity) / (gamma + 1);
                temperature = pipe.Temperature * Math.Pow(a / pipe.SoundSpeed, 2);
                pressure = pipe.Pressure * Math.Pow(temperature / pipe.Temperature, 1 / g);
                velocity = -a;
            }
        }
        for (int i = 0; i < fractions.Length; i++) fractions[i] = values[i] / donor.Density;
        var state = equation.EncodeFace(face, fractions, pressure, temperature, velocity, values[equation.Tag] / donor.Density);
        equation.PhysicalFlux(face, state, flux);
        if (inflow)
            flux[equation.Energy] = state.Density * velocity * (ambient.Energy + ambient.Pressure) / ambient.Density;
        return pressure;
    }

    // Solve a boundary whose interior lies on the left. Only axial momentum is invariant under reflection of the flux.
    internal static double RightFlux(GasEquation gas, ReadOnlySpan<double> plenum, GasState reservoir,
        ReadOnlySpan<double> edge, GasState interior, Span<double> flux)
    {
        Span<double> mirrored = stackalloc double[gas.Size];
        edge.CopyTo(mirrored);
        mirrored[gas.Momentum] = -mirrored[gas.Momentum];
        double pressure = Flux(gas, ExteriorBoundary.PressureReservoir, plenum, reservoir, mirrored,
            interior with { Velocity = -interior.Velocity }, flux, allowUpstreamShock: true);
        for (int i = 0; i < flux.Length; i++)
            if (i != gas.Momentum) flux[i] = -flux[i];
        return pressure;
    }

    private static bool ShockEntersPipe(GasState pipe, double backPressure)
    {
        if (backPressure <= pipe.Pressure) return false;
        double strength = (pipe.Gamma + 1) / (2 * pipe.Gamma) * (backPressure / pipe.Pressure - 1);
        return pipe.Velocity + pipe.SoundSpeed * Math.Sqrt(1 + strength) > 0;
    }

    private static double PipeVelocity(GasState pipe, double pressure)
    {
        double ratio = pressure / pipe.Pressure, gamma = pipe.Gamma;
        if (ratio <= 1)
            return pipe.Velocity + 2 * pipe.SoundSpeed / (gamma - 1) *
                (Math.Pow(ratio, (gamma - 1) / (2 * gamma)) - 1);
        double b = (gamma - 1) / (gamma + 1);
        return pipe.Velocity + (pressure - pipe.Pressure) *
            Math.Sqrt(2 / ((gamma + 1) * pipe.Density * (pressure + b * pipe.Pressure)));
    }
}
