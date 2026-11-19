using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.GasDynamics;

public sealed record DuctGeometry(double LengthM, double ReservoirDiameterM, double ValveDiameterM, int Cells)
{
    internal void Validate()
    {
        if (!double.IsFinite(LengthM) || LengthM is < .08 or > 3 ||
            !double.IsFinite(ReservoirDiameterM) || ReservoirDiameterM is < .015 or > .15 ||
            !double.IsFinite(ValveDiameterM) || ValveDiameterM is < .015 or > .15 ||
            Cells is < 8 or > 256)
            throw new ArgumentException("Conducto: longitud 0,08–3 m, diámetros 15–150 mm y 8–256 celdas.");
    }
}

public enum DuctScheme { FirstOrder, Muscl }

public sealed record GasPath(DuctGeometry Intake, DuctGeometry Exhaust, double Courant = .45, DuctScheme Scheme = DuctScheme.FirstOrder)
{
    public const string ModelId = "four-stroke-isooctane-ducts-v4";
    public const string MusclModelId = "four-stroke-isooctane-ducts-v4-muscl";
    public string Identifier => Scheme == DuctScheme.Muscl ? MusclModelId : ModelId;
    public static GasPath Example => new(new(.35, .042, .036, 24), new(.65, .036, .032, 32));
    public void Validate()
    {
        if (!Enum.IsDefined(Scheme) || Intake is null || Exhaust is null || !double.IsFinite(Courant) || Courant is < .05 or > .5)
            throw new ArgumentException("Se requieren ambos conductos y CFL entre 0,05 y 0,5.");
        Intake.Validate();
        Exhaust.Validate();
    }
}

// Densities in SI: partial masses, axial momentum, total energy and one passive mass density.
internal readonly record struct GasState(double Density, double Velocity, double Pressure, double Energy,
    double SoundSpeed, double Temperature, double Gamma, double GasConstant);

internal sealed class GasEquation(int speciesCount)
{
    internal int SpeciesCount => speciesCount;
    internal int Size => speciesCount + 3;
    internal int Momentum => speciesCount;
    internal int Energy => speciesCount + 1;
    internal int Tag => speciesCount + 2;
    private readonly double[] energyAtMinimum = Enumerable.Range(0, speciesCount).Select(i => SpeciesThermodynamics.U(i, 200)).ToArray();
    private readonly double[] energyAtMaximum = Enumerable.Range(0, speciesCount).Select(i => SpeciesThermodynamics.U(i, 6000)).ToArray();

    internal void ValidateMixture(ReadOnlySpan<double> cell)
    {
        double density = 0, minimum = 0, maximum = 0;
        for (int i = 0; i < speciesCount; i++)
        {
            double value = cell[i];
            if (!double.IsFinite(value) || value < -1e-12)
                throw new NumericalStateException("Densidad parcial no física en el conducto.");
            density += value;
            minimum += value * energyAtMinimum[i];
            maximum += value * energyAtMaximum[i];
        }
        double energy = cell[Energy] - .5 * cell[Momentum] * cell[Momentum] / density;
        if (density <= 1e-9 || !(energy >= minimum && energy <= maximum))
            throw new NumericalStateException("Estado del conducto fuera de 200–6000 K.");
    }

    internal GasState Decode(ReadOnlySpan<double> cell, double temperatureGuess = 600)
    {
        var masses = cell[..speciesCount];
        double density = 0;
        foreach (double value in masses)
        {
            if (!double.IsFinite(value) || value < -1e-12)
                throw new NumericalStateException("Densidad parcial no física en el conducto.");
            density += value;
        }
        if (density <= 1e-9) throw new NumericalStateException("Densidad no positiva en el conducto.");
        double u = cell[Momentum] / density;
        double internalEnergy = cell[Energy] - .5 * density * u * u;
        double t, gamma, r;
        if (speciesCount == 1)
        {
            gamma = 1.4;
            r = 287;
            t = internalEnergy / density * (gamma - 1) / r;
        }
        else
        {
            try { t = SpeciesThermodynamics.Temperature(masses, internalEnergy, temperatureGuess); }
            catch (NumericalStateException) when (temperatureGuess != 600)
            {
                t = SpeciesThermodynamics.Temperature(masses, internalEnergy, 600);
            }
            gamma = SpeciesThermodynamics.Gamma(masses, t);
            r = SpeciesThermodynamics.GasConstant(masses);
        }

        double pressure = density * r * t;
        if (!double.IsFinite(pressure) || pressure <= 0 || !double.IsFinite(u))
            throw new NumericalStateException("Presión no positiva en el conducto.");
        return new(density, u, pressure, cell[Energy], Math.Sqrt(gamma * pressure / density), t, gamma, r);
    }

    internal GasState Encode(Span<double> destination, ReadOnlySpan<double> fractions, double p, double t, double u, double tag)
    {
        double r = speciesCount == 1 ? 287 : SpeciesThermodynamics.GasConstant(fractions);
        double density = p / (r * t);
        for (int i = 0; i < speciesCount; i++) destination[i] = fractions[i] * density;
        destination[Momentum] = density * u;
        destination[Energy] = (speciesCount == 1 ? p / .4 : SpeciesThermodynamics.Energy(destination[..speciesCount], t))
            + .5 * density * u * u;
        destination[Tag] = density * tag;
        double gamma = speciesCount == 1 ? 1.4 : SpeciesThermodynamics.Gamma(fractions, t);
        return new(density, u, p, destination[Energy], Math.Sqrt(gamma * r * t), t, gamma, r);
    }

    internal GasState EncodeFace(Span<double> destination, ReadOnlySpan<double> fractions, double p, double t, double u, double tag)
    {
        if (speciesCount == 1) return Encode(destination, fractions, p, t, u, tag);
        double mixtureR = 0, cp = 0, cv = 0;
        for (int i = 0; i < speciesCount; i++)
        {
            var species = SpeciesData.All[i];
            double speciesCp = species.Cp(t);
            mixtureR += fractions[i] * species.R;
            cp += fractions[i] * speciesCp;
            cv += fractions[i] * (speciesCp - species.R);
        }
        double density = p / (mixtureR * t), energy = 0;
        for (int i = 0; i < speciesCount; i++)
        {
            double partial = fractions[i] * density;
            destination[i] = partial;
            energy += partial * SpeciesData.All[i].U(t);
        }
        destination[Momentum] = density * u;
        destination[Energy] = energy + .5 * density * u * u;
        destination[Tag] = density * tag;
        double gamma = cp / cv;
        return new(density, u, p, destination[Energy], Math.Sqrt(gamma * p / density), t, gamma, mixtureR);
    }

    internal void Flux(ReadOnlySpan<double> left, GasState l, ReadOnlySpan<double> right, GasState r, Span<double> result)
    {
        double sl = Math.Min(l.Velocity - l.SoundSpeed, r.Velocity - r.SoundSpeed);
        double sr = Math.Max(l.Velocity + l.SoundSpeed, r.Velocity + r.SoundSpeed);
        if (sl >= 0) { PhysicalFlux(left, l, result); return; }
        if (sr <= 0) { PhysicalFlux(right, r, result); return; }

        double ml = l.Density * (sl - l.Velocity);
        double mr = r.Density * (sr - r.Velocity);
        double star = (r.Pressure - l.Pressure + l.Velocity * ml - r.Velocity * mr) / (ml - mr);
        bool fromLeft = star >= 0;
        var donor = fromLeft ? l : r;
        var values = fromLeft ? left : right;
        double wave = fromLeft ? sl : sr, m = fromLeft ? ml : mr;
        double pStar = donor.Pressure + m * (star - donor.Velocity);
        if (pStar > 0 && star > sl && star < sr)
        {
            double densityStar = m / (wave - star);
            double mdot = densityStar * star;
            double eStar = (donor.Energy * (wave - donor.Velocity) -
                donor.Pressure * donor.Velocity + pStar * star) / (wave - star);
            for (int i = 0; i < speciesCount; i++) result[i] = mdot * values[i] / donor.Density;
            result[Momentum] = mdot * star + pStar;
            result[Energy] = (eStar + pStar) * star;
            result[Tag] = mdot * values[Tag] / donor.Density;
            return;
        }

        // HLL fallback in strong expansions without an admissible contact estimate.
        double scale = 1 / (sr - sl);
        for (int i = 0; i < Size; i++)
        {
            double fl = left[i] * l.Velocity, fr = right[i] * r.Velocity;
            if (i == Momentum) { fl += l.Pressure; fr += r.Pressure; }
            if (i == Energy) { fl += l.Pressure * l.Velocity; fr += r.Pressure * r.Velocity; }
            result[i] = (sr * fl - sl * fr + sl * sr * (right[i] - left[i])) * scale;
        }
    }

    private void PhysicalFlux(ReadOnlySpan<double> cell, GasState gas, Span<double> flux)
    {
        for (int i = 0; i < Size; i++) flux[i] = gas.Velocity * cell[i];
        flux[Momentum] += gas.Pressure;
        flux[Energy] += gas.Pressure * gas.Velocity;
    }

    internal double WallPressure(GasState cell, bool rightWall)
    {
        double normalMach = (rightWall ? cell.Velocity : -cell.Velocity) / cell.SoundSpeed;
        if (normalMach <= 0)
        {
            double factor = Math.Max(0, 1 + (cell.Gamma - 1) / 2 * normalMach);
            return cell.Pressure * Math.Pow(factor, 2 * cell.Gamma / (cell.Gamma - 1));
        }
        // Symmetric shock, using the local frozen gamma to close the wall Riemann problem.
        double x = cell.Gamma * (cell.Gamma + 1) / 2 * normalMach * normalMach;
        return cell.Pressure * (1 + .5 * x + Math.Sqrt(x * (2 * cell.Gamma / (cell.Gamma + 1) + .25 * x)));
    }
}

// x increases from the exterior reservoir to the valve for both inlet and exhaust.
internal sealed partial class DuctMesh
{
    private readonly double[] area, volume;
    private readonly GasEquation equation;
    internal int Cells { get; }
    internal int Size => Cells * equation.Size;
    internal double Dx { get; }
    internal double ValveArea => area[^1];
    internal double ReservoirArea => area[0];
    internal double Volume(int cell) => volume[cell];

    internal DuctMesh(DuctGeometry geometry, GasEquation equation, DuctScheme scheme = DuctScheme.FirstOrder)
    {
        this.equation = equation;
        Cells = geometry.Cells;
        if (scheme == DuctScheme.Muscl)
        {
            centers = new GasState[Cells];
            faces = new GasState[2 * Cells];
            faceValues = new double[2 * Size];
            primitives = new double[Cells * (equation.Size + 1)];
        }
        Dx = geometry.LengthM / Cells;
        double Area(double diameter) => Math.PI / 4 * diameter * diameter;
        double Diameter(double x) => geometry.ReservoirDiameterM + x * (geometry.ValveDiameterM - geometry.ReservoirDiameterM);
        area = new double[Cells + 1];
        volume = new double[Cells];
        for (int i = 0; i <= Cells; i++) area[i] = Area(Diameter((double)i / Cells));
        for (int i = 0; i < Cells; i++)
            volume[i] = Dx / 6 * (area[i] + 4 * Area(Diameter((i + .5) / Cells)) + area[i + 1]);
    }

    // Boundary fluxes are integrated over face area in SI, internal fluxes share one face value.
    internal double Rates(ReadOnlySpan<double> state, Span<double> rate,
        ReadOnlySpan<double> leftBoundary, ReadOnlySpan<double> rightBoundary) =>
        Rates(state, rate, leftBoundary, rightBoundary, out _);

    internal double Rates(ReadOnlySpan<double> state, Span<double> rate,
        ReadOnlySpan<double> leftBoundary, ReadOnlySpan<double> rightBoundary, out double wallForce)
    {
        int width = equation.Size;
        Span<double> leftFlux = stackalloc double[width];
        Span<double> rightFlux = stackalloc double[width];
        leftBoundary.CopyTo(leftFlux);
        wallForce = 0;
        var left = centers is null ? equation.Decode(state[..width]) : centers[0];
        double cflTime = double.PositiveInfinity;
        for (int i = 0; i < Cells; i++)
        {
            int offset = i * width;
            var q = state.Slice(offset, width);
            cflTime = Math.Min(cflTime, volume[i] / (Math.Max(area[i], area[i + 1]) *
                (Math.Abs(left.Velocity) + left.SoundSpeed)));
            GasState right = default;
            if (i + 1 < Cells)
            {
                var qr = state.Slice(offset + width, width);
                if (faces is null)
                {
                    right = equation.Decode(qr);
                    equation.Flux(q, left, qr, right, rightFlux);
                }
                else
                {
                    right = centers![i + 1];
                    int face = 2 * i + 1;
                    equation.Flux(FaceValues(face), faces[face], FaceValues(face + 1), faces[face + 1], rightFlux);
                }
                for (int k = 0; k < width; k++) rightFlux[k] *= area[i + 1];
            }
            else rightBoundary.CopyTo(rightFlux);

            double inverseVolume = 1 / volume[i];
            for (int k = 0; k < width; k++) rate[offset + k] = (leftFlux[k] - rightFlux[k]) * inverseVolume;
            double force = left.Pressure * (area[i + 1] - area[i]);
            rate[offset + equation.Momentum] += force * inverseVolume;
            wallForce += force;
            rightFlux.CopyTo(leftFlux);
            left = right;
        }
        return faces is not null ? Math.Min(cflTime, faceCflTime) : cflTime;
    }
}
