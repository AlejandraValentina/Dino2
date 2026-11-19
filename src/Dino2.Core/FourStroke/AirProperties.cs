namespace Dino2.Core.FourStroke;

// Sensible pseudo-air, fixed 79% N2 / 21% O2 (mole); no reaction equilibrium.
// NASA7 cp data: Cantera v3.2.0 data/nasa_gas.yaml, N2 TPIS78, O2 TPIS89.
// Integrate cp continuously at 1000 K, with an arbitrary common sensible-energy datum.
public static class AirProperties
{
    public const double R = 8.31446261815324 / (.79 * .0280134 + .21 * .0319988);
    private static readonly double[] Low = Blend(
        [3.53100528, -1.23660987e-4, -5.02999437e-7, 2.43530612e-9, -1.40881235e-12],
        [3.78245636, -2.99673415e-3, 9.847302e-6, -9.68129508e-9, 3.24372836e-12]);
    private static readonly double[] High = Blend(
        [2.95257626, 1.39690057e-3, -4.92631691e-7, 7.86010367e-11, -4.60755321e-15],
        [3.66096083, 6.56365523e-4, -1.41149485e-7, 2.05797658e-11, -1.29913248e-15]);
    private static readonly double Offset = Integral(Low, 1000) - Integral(High, 1000);

    private static double[] Blend(double[] n, double[] o) => n.Zip(o, (a, b) => .79 * a + .21 * b).ToArray();
    private static double Integral(double[] a, double t) => t * (a[0] + t * (a[1] / 2 + t * (a[2] / 3 + t * (a[3] / 4 + t * a[4] / 5))));
    public static double Cp(double t)
    {
        var a = t <= 1000 ? Low : High;
        return R * (a[0] + t * (a[1] + t * (a[2] + t * (a[3] + t * a[4]))));
    }
    public static double InternalEnergy(double t) => R * ((t <= 1000 ? Integral(Low, t) : Integral(High, t) + Offset) - t);
    public static double Enthalpy(double t) => InternalEnergy(t) + R * t;
    public static double Gamma(double t) => Cp(t) / (Cp(t) - R);

    public static double Temperature(double specificEnergy)
    {
        double t = Math.Clamp(specificEnergy / 850, 200, 6000);
        for (int i = 0; i < 12; i++)
        {
            double correction = (InternalEnergy(t) - specificEnergy) / (Cp(t) - R);
            double next = t - correction;
            if (!double.IsFinite(next) || next < 199.999 || next > 6000.001)
                throw new NumericalStateException("El gas sale del intervalo termodinámico 200–6000 K; revise entradas o resolución.");
            t = next;
            if (Math.Abs(correction) < 1e-8) return t;
        }
        throw new NumericalStateException("No convergió la inversión de energía interna a temperatura.");
    }
}

public sealed class NumericalStateException(string message) : ArithmeticException(message);
public sealed class CycleConvergenceException(string message) : ArithmeticException(message);
public sealed class ModelDomainException(string message) : ArithmeticException(message);
