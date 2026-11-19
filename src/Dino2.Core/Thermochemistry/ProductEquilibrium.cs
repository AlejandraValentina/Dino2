using Dino2.Core.FourStroke;

namespace Dino2.Core.Thermochemistry;

/// <summary>Ideal-gas UV equilibrium restricted to CO2/CO and H2O/H2 with a shared oxygen pool.</summary>
public static class ProductEquilibrium
{
    private const double R = 8.31446261815324;
    private const double ReferencePressure = 101325;

    public static double Solve(ReadOnlySpan<double> inventory, double energy, double volume, Span<double> products)
    {
        if (inventory.Length != SpeciesThermodynamics.ExtendedCount || products.Length != inventory.Length ||
            !double.IsFinite(volume) || volume <= 0)
            throw new ArgumentException("El equilibrio UV requiere siete especies y volumen positivo.");

        var species = SpeciesData.All;
        double frozenT = SpeciesThermodynamics.Temperature(inventory, energy);
        inventory.CopyTo(products);
        double co = inventory[5] / species[5].MolecularWeight;
        double h2 = inventory[6] / species[6].MolecularWeight;
        double carbon = inventory[3] / species[3].MolecularWeight + co;
        double hydrogen = inventory[4] / species[4].MolecularWeight + h2;
        double oxygen = inventory[1] / species[1].MolecularWeight - (co + h2) / 2;
        if (carbon == 0 && hydrogen == 0) return frozenT;

        double temperature = frozenT, lower = 200, upper = 6000;
        double tolerance = 1e-6 * SpeciesThermodynamics.TotalMass(inventory); // J
        for (int iteration = 0; iteration < 32; iteration++)
        {
            double logT = Math.Log(temperature);
            double halfOxygen = .5 * species[1].GibbsOverRT(temperature, logT);
            double kc = Math.Exp(species[3].GibbsOverRT(temperature, logT) - species[5].GibbsOverRT(temperature, logT) - halfOxygen);
            double kh = Math.Exp(species[4].GibbsOverRT(temperature, logT) - species[6].GibbsOverRT(temperature, logT) - halfOxygen);
            double a = R * temperature / (volume * ReferencePressure);
            double qLower = Math.Sqrt(a * Math.Max(oxygen, 0));
            double qUpper = qLower + Math.Cbrt(.5 * a * (carbon * kc + hydrogen * kh));
            double q = qUpper;
            double nCo = 0, nH2 = 0, dqCo = 0, dqH2 = 0;
            bool converged = false;
            for (int i = 0; i < 80; i++)
            {
                nCo = carbon * (kc / (q + kc));
                nH2 = hydrogen * (kh / (q + kh));
                dqCo = nCo / (q + kc);
                dqH2 = nH2 / (q + kh);
                double f = q * q / a - oxygen - .5 * (nCo + nH2);
                double delta = f / (2 * q / a + .5 * (dqCo + dqH2));
                if (Math.Abs(delta) <= 1e-12 * q)
                {
                    converged = true;
                    break;
                }
                if (f < 0) qLower = q; else qUpper = q;
                double proposal = q - delta;
                q = proposal > qLower && proposal < qUpper ? proposal : (qLower + qUpper) / 2;
            }
            if (!converged) throw new NumericalStateException("No convergió el equilibrio de oxígeno.");

            products[1] = q * q / a * species[1].MolecularWeight;
            products[3] = carbon * (q / (q + kc)) * species[3].MolecularWeight;
            products[4] = hydrogen * (q / (q + kh)) * species[4].MolecularWeight;
            products[5] = nCo * species[5].MolecularWeight;
            products[6] = nH2 * species[6].MolecularWeight;

            double u = 0, cv = 0;
            for (int i = 0; i < products.Length; i++)
            {
                u += products[i] * species[i].U(temperature);
                cv += products[i] * (species[i].Cp(temperature) - species[i].R);
            }
            double error = u - energy;
            if (Math.Abs(error) <= tolerance) return temperature;
            if (error < 0) lower = temperature; else upper = temperature;

            // Implicit differentiation of the oxygen balance and van 't Hoff relation.
            double DeltaU(int reactant, int product) =>
                species[product].MolecularWeight * species[product].U(temperature) +
                .5 * species[1].MolecularWeight * species[1].U(temperature) -
                species[reactant].MolecularWeight * species[reactant].U(temperature);
            double duCo = DeltaU(3, 5), duH2 = DeltaU(4, 6);
            double dCo = dqCo * q * (duCo + .5 * R * temperature) / (R * temperature * temperature);
            double dH2 = dqH2 * q * (duH2 + .5 * R * temperature) / (R * temperature * temperature);
            double dq = (q * q / (a * temperature) + .5 * (dCo + dH2)) / (2 * q / a + .5 * (dqCo + dqH2));
            cv += duCo * (dCo - dqCo * dq) + duH2 * (dH2 - dqH2 * dq);
            double next = temperature - error / cv;
            temperature = next > lower && next < upper ? next : (lower + upper) / 2;
        }
        throw new NumericalStateException("No convergió el equilibrio UV de productos entre 200 y 6000 K.");
    }
}
