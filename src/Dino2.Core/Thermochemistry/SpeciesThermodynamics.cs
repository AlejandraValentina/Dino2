using Dino2.Core.FourStroke;

namespace Dino2.Core.Thermochemistry;

internal sealed class NasaSpecies(string name, double molecularWeight, double[] low, double[] high)
{
    public string Name { get; } = name;
    public double MolecularWeight { get; } = molecularWeight; // kg/mol
    public double R { get; } = 8.31446261815324 / molecularWeight;
    private readonly double h298 = 8.31446261815324 / molecularWeight *
        (Integral(low, 298.15) + low[5]);
    private readonly double highOffset = Integral(low, 1000) - Integral(high, 1000);
    private static double Integral(double[] a, double t) => t * (a[0] + t * (a[1] / 2 + t * (a[2] / 3 + t * (a[3] / 4 + t * a[4] / 5))));
    public double FormationEnthalpy => h298;
    public double Cp(double t)
    {
        var a = t <= 1000 ? low : high;
        return R * (a[0] + t * (a[1] + t * (a[2] + t * (a[3] + t * a[4]))));
    }
    public double H(double t) => h298 + R * ((t <= 1000 ? Integral(low, t) : Integral(high, t) + highOffset) - Integral(low, 298.15));
    public double GibbsOverRT(double t, double logT)
    {
        var a = t <= 1000 ? low : high;
        double entropyOverR = a[0] * logT + t * (a[1] + t * (a[2] / 2 + t * (a[3] / 3 + t * a[4] / 4))) + a[6];
        return H(t) / (R * t) - entropyOverR;
    }
    public double U(double t) => H(t) - R * t;
}

public static class SpeciesThermodynamics
{
    public const int Count = 5;
    public const int ExtendedCount = 7;
    public static double MolecularWeight(int species) => SpeciesData.All[species].MolecularWeight;
    public static string Name(int species) => SpeciesData.All[species].Name;
    public static double Cp(int species, double temperature) => SpeciesData.All[species].Cp(temperature);
    public static double H(int species, double temperature) => SpeciesData.All[species].H(temperature);
    public static double U(int species, double temperature) => SpeciesData.All[species].U(temperature);
    public static double FormationEnthalpy(int species) => SpeciesData.All[species].FormationEnthalpy;
    public static double StoichiometricAfr => 12.5 * (MolecularWeight(1) + 79.0 / 21 * MolecularWeight(2)) / MolecularWeight(0);
    public static double LowerHeatingValue => -(8 * MolecularWeight(3) * H(3, 298.15) + 9 * MolecularWeight(4) * H(4, 298.15) -
        MolecularWeight(0) * H(0, 298.15) - 12.5 * MolecularWeight(1) * H(1, 298.15)) / MolecularWeight(0);
    public static double ReactionMassRatio(int species) => species switch
    {
        0 => -1, 1 => -12.5 * MolecularWeight(1) / MolecularWeight(0), 2 => 0,
        3 => 8 * MolecularWeight(3) / MolecularWeight(0), 4 => 9 * MolecularWeight(4) / MolecularWeight(0), 5 or 6 => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(species))
    };
    public static double[] FreshMixture(double afr)
    {
        double airOxygen = 21 * MolecularWeight(1) / (21 * MolecularWeight(1) + 79 * MolecularWeight(2));
        return [1 / (afr + 1), afr / (afr + 1) * airOxygen, afr / (afr + 1) * (1 - airOxygen), 0, 0];
    }
    public static double[] ExhaustMixture(double afr)
    {
        var fresh = FreshMixture(afr);
        double consumed = fresh[0];
        for (int i = 0; i < Count; i++) fresh[i] += consumed * ReactionMassRatio(i);
        if (fresh[1] < 0 && fresh[1] > -1e-14) fresh[1] = 0; // reservoir construction roundoff only
        return fresh;
    }
    public static double TotalMass(ReadOnlySpan<double> masses)
    {
        double mass = 0;
        for (int i = 0; i < masses.Length; i++) mass += masses[i];
        return mass;
    }
    public static double GasConstant(ReadOnlySpan<double> masses)
    {
        double sum = 0, mass = 0;
        for (int i = 0; i < masses.Length; i++) { sum += masses[i] * SpeciesData.All[i].R; mass += masses[i]; }
        return sum / mass;
    }
    public static double Energy(ReadOnlySpan<double> masses, double temperature)
    {
        double energy = 0;
        for (int i = 0; i < masses.Length; i++) energy += masses[i] * U(i, temperature);
        return energy;
    }
    public static double Enthalpy(ReadOnlySpan<double> masses, double temperature)
    {
        double enthalpy = 0;
        for (int i = 0; i < masses.Length; i++) enthalpy += masses[i] * H(i, temperature);
        return enthalpy / TotalMass(masses);
    }
    public static double Gamma(ReadOnlySpan<double> masses, double temperature)
    {
        double cp = 0, cv = 0;
        for (int i = 0; i < masses.Length; i++)
        {
            cp += masses[i] * Cp(i, temperature);
            cv += masses[i] * (Cp(i, temperature) - SpeciesData.All[i].R);
        }
        return cp / cv;
    }
    public static double Temperature(ReadOnlySpan<double> masses, double energy)
    {
        double mass = TotalMass(masses);
        if (!double.IsFinite(mass) || mass <= 1e-10) throw new NumericalStateException("Masa reactiva no positiva.");
        for (int i = 0; i < masses.Length; i++)
            if (!double.IsFinite(masses[i]) || masses[i] < -1e-13)
                throw new NumericalStateException("Masa de especie negativa fuera de tolerancia de redondeo.");
        double t = 1000;
        for (int iteration = 0; iteration < 15; iteration++)
        {
            double u = 0, cv = 0;
            for (int i = 0; i < masses.Length; i++)
            {
                u += masses[i] * U(i, t);
                cv += masses[i] * (Cp(i, t) - SpeciesData.All[i].R);
            }
            double correction = (u - energy) / cv;
            double next = t - correction;
            if (!double.IsFinite(next) || next < 199.999 || next > 6000.001)
                throw new NumericalStateException("El estado reactivo sale de 200–6000 K; no se extrapolan las propiedades.");
            t = next;
            if (Math.Abs(correction) < 1e-8) return t;
        }
        throw new NumericalStateException("No convergió la inversión de energía con composición.");
    }

    // Element mass per kg of species. C, H, O, N; consistent molecular masses.
    public static double ElementMassFraction(int species, int element)
    {
        int atoms = (species, element) switch
        {
            (0, 0) => 8, (0, 1) => 18, (1, 2) => 2, (2, 3) => 2,
            (3, 0) => 1, (3, 2) => 2, (4, 1) => 2, (4, 2) => 1, (5, 0) => 1, (5, 2) => 1, (6, 1) => 2, _ => 0
        };
        double atomicWeight = element switch { 0 => .012011, 1 => .001008, 2 => .015999, 3 => .014007, _ => throw new ArgumentOutOfRangeException(nameof(element)) };
        return atoms * atomicWeight / MolecularWeight(species);
    }
}
