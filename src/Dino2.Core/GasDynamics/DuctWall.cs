namespace Dino2.Core.GasDynamics;

// Null wall temperature denotes an adiabatic wall. All roughness dimensions are SI.
public sealed record DuctWall(double RoughnessM, double? TemperatureK)
{
    internal void Validate(double diameter)
    {
        if (!double.IsFinite(RoughnessM) || RoughnessM < 0 || RoughnessM > .05 * diameter ||
            (TemperatureK is double t && (!double.IsFinite(t) || t is < 220 or > 1500)))
            throw new ArgumentException("Pared de conducto: rugosidad 0–5 % del diámetro y temperatura 220–1500 K.");
    }

    internal (double Momentum, double Energy, double RelaxationS) Sources(GasState gas, double diameter)
    {
        const double pr = .71;
        double t = gas.Temperature, u = gas.Velocity;
        double mu = 1.716e-5 * Math.Pow(t / 273.15, 1.5) * (273.15 + 110.4) / (t + 110.4);
        double re = gas.Density * Math.Abs(u) * diameter / mu;
        double laminar = 32 * mu / (diameter * diameter);
        double linearResistance = laminar, nu = 3.66;
        if (re > 2300)
        {
            double f = 1 / Math.Pow(-1.8 * Math.Log10(Math.Pow(RoughnessM / (3.7 * diameter), 1.11) + 6.9 / re), 2);
            double blend = Math.Min((re - 2300) / 1700, 1);
            linearResistance += blend * (f * gas.Density * Math.Abs(u) / (2 * diameter) - laminar);
            // Gnielinski smooth-pipe heat transfer. Roughness affects drag; heat roughness enhancement is unspecified.
            double smooth = 1 / Math.Pow(.79 * Math.Log(re) - 1.64, 2) / 8;
            double turbulent = smooth * (re - 1000) * pr /
                (1 + 12.7 * Math.Sqrt(smooth) * (Math.Pow(pr, 2.0 / 3) - 1));
            nu += blend * (turbulent - nu);
        }
        double heat = 0, thermalRate = 0;
        if (TemperatureK is double wall)
        {
            double cp = gas.Gamma * gas.GasConstant / (gas.Gamma - 1);
            double conductancePerVolume = 4 * nu * mu * cp / (pr * diameter * diameter);
            heat = conductancePerVolume * (wall - t);
            thermalRate = conductancePerVolume / (gas.Density * (cp - gas.GasConstant));
        }
        double rate = Math.Max(2 * linearResistance / gas.Density, thermalRate);
        return (-linearResistance * u, heat, rate > 0 ? 1 / rate : double.PositiveInfinity);
    }
}
