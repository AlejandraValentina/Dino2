namespace Dino2.Core.GasDynamics;

// The pipe coordinate points inward. Pressure is static for discharge and stagnation for incoming gas.
// Wave matching freezes gamma at the respective reference states. The advected total enthalpy
// of inflow is the exterior value, including the chemical datum.
internal static class ReservoirBoundary
{
    internal static void Flux(GasEquation equation, ExteriorBoundary mode, ReadOnlySpan<double> exterior,
        GasState ambient, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux)
    {
        if (mode == ExteriorBoundary.HistoricalRiemann)
        {
            equation.Flux(exterior, ambient, edge, pipe, flux);
            return;
        }
        if (pipe.Velocity <= -pipe.SoundSpeed)
        {
            equation.PhysicalFlux(edge, pipe, flux);
            return;
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
