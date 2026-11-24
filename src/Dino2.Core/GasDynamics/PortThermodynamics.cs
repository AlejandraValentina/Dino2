using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.GasDynamics;

// Frozen composition. Integrate the same piecewise NASA cp as the volume EOS,
// with a continuous entropy integral across the coefficient switch at 1000 K.
internal readonly struct PortThermodynamics
{
    internal const double MinimumTemperature = 200;
    internal const double MaximumTemperature = 6000;

    internal static void RequireTemperature(double temperature, string plane)
    {
        if (!double.IsFinite(temperature) || temperature <= 0)
            throw new NumericalStateException($"Temperatura no física en {plane}: {temperature:R} K.");
        if (temperature is < MinimumTemperature or > MaximumTemperature)
            throw new ModelDomainException($"Interfaz fuera de 200–6000 K en {plane}: {temperature:R} K.");
    }

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

    internal double EnthalpyChange(double a, double b) => a == b ? 0 : a <= 1000
        ? b <= 1000 ? low.Integral(a, b) : low.Integral(a, 1000) + high.Integral(1000, b)
        : b > 1000 ? high.Integral(a, b) : high.Integral(a, 1000) + low.Integral(1000, b);

    internal double EntropyChange(double a, double b) => a == b ? 0 : a <= 1000
        ? b <= 1000 ? low.Entropy(a, b) : low.Entropy(a, 1000) + high.Entropy(1000, b)
        : b > 1000 ? high.Entropy(a, b) : high.Entropy(a, 1000) + low.Entropy(1000, b);

    internal double TemperatureChange(double from, double enthalpy)
    {
        double delta = enthalpy / Cp(from);
        if (Perfect)
        {
            RequireTemperature(from + delta, "entalpía");
            return delta;
        }
        double lo = MinimumTemperature - from, hi = MaximumTemperature - from;
        if (delta < lo || delta > hi)
        {
            if (enthalpy < EnthalpyChange(from, MinimumTemperature) || enthalpy > EnthalpyChange(from, MaximumTemperature))
                throw new ModelDomainException("Interfaz fuera de 200–6000 K en la inversión de entalpía.");
            delta = (lo + hi) * .5;
        }
        for (int i = 0; i < 48; i++)
        {
            double to = from + delta, residual = EnthalpyChange(from, to) - enthalpy;
            double correction = residual / Cp(to);
            if (Math.Abs(correction) <= 2e-13 * from)
            {
                double exact = delta - correction;
                return from + exact is >= MinimumTemperature and <= MaximumTemperature ? exact : delta;
            }
            if (residual < 0) lo = delta; else hi = delta;
            double next = delta - correction;
            if ((next < MinimumTemperature - from && enthalpy < EnthalpyChange(from, MinimumTemperature)) ||
                (next > MaximumTemperature - from && enthalpy > EnthalpyChange(from, MaximumTemperature)))
                throw new ModelDomainException("Interfaz fuera de 200–6000 K en la inversión de entalpía.");
            delta = next > lo && next < hi ? next : lo + (hi - lo) * .5;
        }
        throw new NumericalStateException("No se resolvió la entalpía de la interfaz.");
    }

    internal double IsentropicTemperature(double t, double pressureLog)
    {
        if (TryIsentropicTemperature(t, pressureLog, out double result)) return result;
        throw new ModelDomainException($"Interfaz fuera de 200–6000 K en la isentrópica: T={t:R}, ln(p₂/p₁)={pressureLog:R}.");
    }

    internal bool TryIsentropicTemperature(double t, double pressureLog, out double result)
    {
        double target = R * pressureLog;
        result = t * Math.Exp(target / Cp(t));
        if (Perfect) return result is >= MinimumTemperature and <= MaximumTemperature;
        double lo = MinimumTemperature, hi = MaximumTemperature;
        if (!(result >= lo && result <= hi))
        {
            if (target < EntropyChange(t, lo) || target > EntropyChange(t, hi)) return false;
            result = (lo + hi) * .5;
        }
        for (int i = 0; i < 48; i++)
        {
            double residual = EntropyChange(t, result) - target;
            double step = residual * result / Cp(result);
            double next = result * Math.Exp(-step / result);
            if ((next < MinimumTemperature && target < EntropyChange(t, MinimumTemperature)) ||
                (next > MaximumTemperature && target > EntropyChange(t, MaximumTemperature))) return false;
            if (Math.Abs(step) <= 2e-13 * t)
            {
                if (next is >= MinimumTemperature and <= MaximumTemperature) result = next;
                return true;
            }
            if (residual < 0) lo = result; else hi = result;
            result = next > lo && next < hi ? next : lo + (hi - lo) * .5;
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
        if (TrySonicTemperature(t, kinetic, out double temperature)) return temperature;
        throw new ModelDomainException("Interfaz fuera de 200–6000 K en el estado sónico de garganta.");
    }

    internal bool TrySonicTemperature(double t, double kinetic, out double guess)
    {
        double a2 = SoundSquared(t);
        guess = t + (kinetic - a2 * .5) / (Cp(t) + a2 / (2 * t));
        if (Perfect) return guess is >= MinimumTemperature and <= MaximumTemperature;
        double lo = MinimumTemperature, hi = MaximumTemperature;
        if (!(guess >= lo && guess <= hi))
        {
            if (kinetic < EnthalpyChange(t, lo) + .5 * SoundSquared(lo) ||
                kinetic > EnthalpyChange(t, hi) + .5 * SoundSquared(hi)) return false;
            guess = (lo + hi) * .5;
        }
        for (int i = 0; i < 48; i++)
        {
            a2 = SoundSquared(guess);
            double residual = EnthalpyChange(t, guess) + a2 * .5 - kinetic;
            double correction = residual / (Cp(guess) + a2 / (2 * guess));
            double next = guess - correction;
            if ((next < MinimumTemperature && kinetic < EnthalpyChange(t, MinimumTemperature) + .5 * SoundSquared(MinimumTemperature)) ||
                (next > MaximumTemperature && kinetic > EnthalpyChange(t, MaximumTemperature) + .5 * SoundSquared(MaximumTemperature)))
                return false;
            if (Math.Abs(correction) <= 2e-13 * t)
            {
                if (next is >= MinimumTemperature and <= MaximumTemperature) guess = next;
                return true;
            }
            if (residual < 0) lo = guess; else hi = guess;
            guess = next > lo && next < hi ? next : lo + (hi - lo) * .5;
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
