using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.GasDynamics;

// Frozen composition. Integrate the same piecewise NASA cp as the volume EOS,
// with a continuous entropy integral across the coefficient switch at 1000 K.
internal readonly struct PortThermodynamics
{
    private readonly record struct HeatCapacity(double A, double B, double C, double D, double E)
    {
        internal double At(double t) => A + t * (B + t * (C + t * (D + t * E)));

        internal double Integral(double a, double b)
        {
            double m = (a + b) * .5, s = (b - a) * .3872983346207417;
            return (b - a) * (4 * At(m) + 2.5 * (At(m - s) + At(m + s))) / 9;
        }

        internal double Entropy(double a, double b)
        {
            double a2 = a * a, b2 = b * b, sum = a + b;
            return A * LogOnePlus((b - a) / a) + (b - a) *
                (B + C * sum / 2 + D * (a2 + a * b + b2) / 3 + E * sum * (a2 + b2) / 4);
        }
    }

    private readonly HeatCapacity low, high;
    internal double R { get; }
    internal bool Perfect { get; }

    internal PortThermodynamics(GasState state, ReadOnlySpan<double> conservative, int species)
    {
        R = state.GasConstant;
        Perfect = species == 1;
        if (Perfect)
        {
            low = high = new(state.Gamma * R / (state.Gamma - 1), 0, 0, 0, 0);
            return;
        }
        Span<double> lower = stackalloc double[5], upper = stackalloc double[5];
        lower.Clear(); upper.Clear();
        for (int i = 0; i < species; i++)
        {
            if (conservative[i] == 0) continue;
            var source = SpeciesData.All[i];
            double weight = source.R * conservative[i] / state.Density;
            var a = source.HeatCapacityCoefficients(false);
            var b = source.HeatCapacityCoefficients(true);
            for (int k = 0; k < 5; k++)
            {
                lower[k] += weight * a[k];
                upper[k] += weight * b[k];
            }
        }
        low = new(lower[0], lower[1], lower[2], lower[3], lower[4]);
        high = new(upper[0], upper[1], upper[2], upper[3], upper[4]);
    }

    internal double Cp(double t) => (t <= 1000 ? low : high).At(t);
    internal double SoundSquared(double t) { double cp = Cp(t); return R * t * cp / (cp - R); }

    // Preserve increments which disappear in 1 + x before evaluating log/exp.
    internal static double LogOnePlus(double x)
    {
        double sum = 1 + x;
        return sum == 1 ? x : Math.Log(sum) * (x / (sum - 1));
    }

    internal static double ExpMinusOne(double x) => Math.Abs(x) < .001
        ? x * (1 + x * (.5 + x * (1.0 / 6 + x * (1.0 / 24 + x / 120))))
        : Math.Exp(x) - 1;

    internal double EnthalpyChange(double a, double b) => a <= 1000
        ? b <= 1000 ? low.Integral(a, b) : low.Integral(a, 1000) + high.Integral(1000, b)
        : b > 1000 ? high.Integral(a, b) : high.Integral(a, 1000) + low.Integral(1000, b);

    internal double EntropyChange(double a, double b) => a <= 1000
        ? b <= 1000 ? low.Entropy(a, b) : low.Entropy(a, 1000) + high.Entropy(1000, b)
        : b > 1000 ? high.Entropy(a, b) : high.Entropy(a, 1000) + low.Entropy(1000, b);

    internal double TemperatureChange(double from, double enthalpy)
    {
        double delta = enthalpy / Cp(from);
        if (Perfect) return delta;
        for (int i = 0; i < 10; i++)
        {
            double to = from + delta;
            double correction = (EnthalpyChange(from, to) - enthalpy) / Cp(to);
            delta -= correction;
            if (Math.Abs(correction) <= 2e-13 * from) return delta;
        }
        throw new NumericalStateException("No se resolvió la entalpía de la interfaz.");
    }

    internal double IsentropicTemperature(double t, double pressureLog)
    {
        double target = R * pressureLog;
        double result = t * Math.Exp(target / Cp(t));
        if (Perfect) return result;
        for (int i = 0; i < 12; i++)
        {
            double step = (EntropyChange(t, result) - target) * result / Cp(result);
            // The logarithmic coordinate cannot produce a negative trial temperature.
            result *= Math.Exp(-step / result);
            if (Math.Abs(step) <= 2e-13 * t) return result;
        }
        throw new NumericalStateException($"No se resolvió la isentrópica de la interfaz: T={t:R}, ln(p₂/p₁)={pressureLog:R}.");
    }

    internal double IsentropicPressure(double p, double from, double to) => p * Math.Exp(EntropyChange(from, to) / R);

    internal double IsentropicEnthalpyChange(double from, double to, double pressureLog)
    {
        double cp = Cp(from);
        if (Perfect) return cp * from * ExpMinusOne(R * pressureLog / cp);
        // dh/d(log p) = R T, dT/d(log p) = R T / cp. Keep the small
        // increment even when the two absolute temperatures round alike.
        return Math.Abs(pressureLog) < 1e-6
            ? R * from * pressureLog * (1 + .5 * R * pressureLog / cp)
            : EnthalpyChange(from, to);
    }

    internal double SonicTemperature(double t, double kinetic)
    {
        double guess = t + (kinetic - SoundSquared(t) * .5) / (Cp(t) + SoundSquared(t) / (2 * t));
        if (Perfect) return guess;
        for (int i = 0; i < 16; i++)
        {
            double change = (EnthalpyChange(t, guess) + SoundSquared(guess) * .5 - kinetic) /
                (Cp(guess) + SoundSquared(guess) / (2 * guess));
            guess -= change;
            if (Math.Abs(change) <= 2e-13 * t) return guess;
        }
        throw new NumericalStateException("No se resolvió el estado sónico en la interfaz.");
    }

    internal double AcousticIntegral(double from, double to)
    {
        if (Perfect) return 2 * Cp(from) * (to - from) /
            (Math.Sqrt(SoundSquared(to)) + Math.Sqrt(SoundSquared(from)));
        if ((from < 1000 && to > 1000) || (from > 1000 && to < 1000))
            return AcousticIntegral(from, 1000) + AcousticIntegral(1000, to);
        double m = (from + to) * .5, s = (to - from) * .3872983346207417;
        return (to - from) * (4 * InverseImpedance(m) + 2.5 * (InverseImpedance(m - s) + InverseImpedance(m + s))) / 9;
    }

    private double InverseImpedance(double t) => Cp(t) / Math.Sqrt(SoundSquared(t));
}
