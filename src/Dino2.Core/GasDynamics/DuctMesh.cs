using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;
using System.Text.Json.Serialization;

namespace Dino2.Core.GasDynamics;

public sealed record DuctGeometry(double LengthM, double ReservoirDiameterM, double ValveDiameterM, int Cells)
{
    public DuctWall? Wall { get; init; }
    internal void Validate()
    {
        if (!double.IsFinite(LengthM) || LengthM is < .08 or > 3 ||
            !double.IsFinite(ReservoirDiameterM) || ReservoirDiameterM is < .015 or > .15 ||
            !double.IsFinite(ValveDiameterM) || ValveDiameterM is < .015 or > .15 ||
            Cells is < 8 or > 256)
            throw new ArgumentException("Conducto: longitud 0,08–3 m, diámetros 15–150 mm y 8–256 celdas.");
        Wall?.Validate(Math.Min(ReservoirDiameterM, ValveDiameterM));
    }
}

public enum DuctScheme { FirstOrder, Muscl }
public enum ExteriorBoundary { HistoricalRiemann = 0, PressureReservoir = 1, UnspecifiedLegacy = 2 }

public sealed record GasPath(DuctGeometry Intake, DuctGeometry Exhaust, double Courant = .45, DuctScheme Scheme = DuctScheme.FirstOrder)
    : IJsonOnDeserializing
{
    private ExteriorBoundary exterior = ExteriorBoundary.PressureReservoir;
    public ExteriorBoundary Exterior { get => exterior; init => exterior = value; }
    void IJsonOnDeserializing.OnDeserializing() => exterior = ExteriorBoundary.UnspecifiedLegacy;
    public ExteriorBoundary? IntakeExterior { get; init; }
    public ExteriorBoundary? ExhaustExterior { get; init; }
    public ExteriorBoundary IntakeBoundary => IntakeExterior ?? Exterior;
    public ExteriorBoundary ExhaustBoundary => ExhaustExterior ?? Exterior;
    public const string ModelId = "four-stroke-isooctane-ducts-v4";
    public const string MusclModelId = "four-stroke-isooctane-ducts-v4-muscl";
    public string Identifier => Stage11Identifier + (PhysicalCoupling ? "-ports-v2" : "");
    [JsonIgnore]
    public string Stage12Identifier => Stage11Identifier + (PhysicalCoupling ? "-ports-v1" : "");
    internal bool PhysicalCoupling => IntakeBoundary == ExteriorBoundary.PressureReservoir || ExhaustBoundary == ExteriorBoundary.PressureReservoir;
    public bool UsesLegacyExterior() => IntakeBoundary != ExteriorBoundary.PressureReservoir || ExhaustBoundary != ExteriorBoundary.PressureReservoir;
    public string? BoundaryNotice() => IntakeBoundary == ExteriorBoundary.UnspecifiedLegacy || ExhaustBoundary == ExteriorBoundary.UnspecifiedLegacy
        ? "LEGACY / NO PREDICTIVO · contorno ausente en el archivo histórico."
        : UsesLegacyExterior() ? "LEGACY / NO PREDICTIVO · extremo adaptado." : null;
    [JsonIgnore]
    public string Stage11Identifier => (Scheme == DuctScheme.Muscl ? MusclModelId : ModelId) +
        ((IntakeBoundary, ExhaustBoundary) switch
        {
            (ExteriorBoundary.PressureReservoir, ExteriorBoundary.PressureReservoir) => "-pressure-boundary",
            (ExteriorBoundary.PressureReservoir, _) => "-pressure-intake",
            (_, ExteriorBoundary.PressureReservoir) => "-pressure-exhaust",
            _ => ""
        }) +
        (Intake.Wall is not null || Exhaust.Wall is not null ? "-wall" : "");
    public static GasPath Example => new(new(.35, .042, .036, 24), new(.65, .036, .032, 32))
        { Exterior = ExteriorBoundary.HistoricalRiemann };
    public static GasPath RecoveryExample => new(new DuctGeometry(.35, .042, .036, 24) { Wall = new(.00002, 320) },
        new DuctGeometry(.85, .036, .032, 42) { Wall = new(.00004, 500) }, Scheme: DuctScheme.Muscl)
        { Exterior = ExteriorBoundary.PressureReservoir };
    public void Validate()
    {
        if (!Enum.IsDefined(Scheme) || !Enum.IsDefined(Exterior) || !Enum.IsDefined(IntakeBoundary) || !Enum.IsDefined(ExhaustBoundary) ||
            Intake is null || Exhaust is null || !double.IsFinite(Courant) || Courant is < .05 or > .5)
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
    private readonly double[] energyAtMinimum = ThermalBounds(speciesCount, 200);
    private readonly double[] energyAtMaximum = ThermalBounds(speciesCount, 6000);

    private static double[] ThermalBounds(int speciesCount, double temperature) => speciesCount == 1
        ? [287 * temperature / .4]
        : Enumerable.Range(0, speciesCount).Select(i => SpeciesThermodynamics.U(i, temperature)).ToArray();

    internal void ValidateMixture(ReadOnlySpan<double> cell)
    {
        double density = 0, minimum = 0, maximum = 0;
        for (int i = 0; i < speciesCount; i++)
        {
            double value = cell[i];
            if (!double.IsFinite(value) || value < -1e-12)
                throw new NumericalStateException($"Densidad parcial no física en el conducto, especie {i}: {value:E4} kg/m³.");
            density += value;
            minimum += value * energyAtMinimum[i];
            maximum += value * energyAtMaximum[i];
        }
        double energy = cell[Energy] - .5 * cell[Momentum] * cell[Momentum] / density;
        if (density <= 1e-9 || !(energy >= minimum && energy <= maximum))
            throw new NumericalStateException($"Estado del conducto fuera de 200–6000 K: ρ={density:G6}, u_int={energy:G6}, intervalo [{minimum:G6}, {maximum:G6}] J/m³.");
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
            if (fractions[i] == 0) continue;
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
            if (partial != 0) energy += partial * SpeciesData.All[i].U(t);
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

    internal void PhysicalFlux(ReadOnlySpan<double> cell, GasState gas, Span<double> flux)
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
    private readonly bool boundedPassiveTag;
    private readonly DuctWall? wall;
    internal int Cells { get; }
    internal int Size => Cells * equation.Size;
    internal double Dx { get; }
    internal double ValveArea => area[^1];
    internal double ReservoirArea => area[0];
    internal double Volume(int cell) => volume[cell];

    internal DuctMesh(DuctGeometry geometry, GasEquation equation, DuctScheme scheme = DuctScheme.FirstOrder, bool boundedPassiveTag = false)
    {
        this.equation = equation;
        this.boundedPassiveTag = boundedPassiveTag;
        wall = geometry.Wall;
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

    internal DuctMesh(DuctGeometry geometry, GasEquation equation, DuctScheme scheme,
        Func<double, double> diameterAtM, IReadOnlyList<double> knots, bool boundedPassiveTag = false)
        : this(geometry, equation, scheme, boundedPassiveTag)
    {
        double Area(double x)
        {
            double d = diameterAtM(x);
            return Math.PI / 4 * d * d;
        }
        for (int i = 0; i <= Cells; i++) area[i] = Area(i * Dx);
        for (int i = 0; i < Cells; i++)
        {
            double left = i * Dx, right = (i + 1) * Dx;
            double Integrate(double a, double b) => (b - a) / 6 * (Area(a) + 4 * Area((a + b) / 2) + Area(b));
            volume[i] = 0;
            foreach (double knot in knots)
            {
                if (knot <= left || knot >= right) continue;
                volume[i] += Integrate(left, knot);
                left = knot;
            }
            volume[i] += Integrate(left, right);
        }
    }
    // Boundary fluxes are integrated over face area in SI, internal fluxes share one face value.
    internal double Rates(ReadOnlySpan<double> state, Span<double> rate,
        ReadOnlySpan<double> leftBoundary, ReadOnlySpan<double> rightBoundary) =>
        Rates(state, rate, leftBoundary, rightBoundary, out _);

    internal double Rates(ReadOnlySpan<double> state, Span<double> rate,
        ReadOnlySpan<double> leftBoundary, ReadOnlySpan<double> rightBoundary, out double wallForce)
        => Rates(state, rate, leftBoundary, rightBoundary, out wallForce, out _);

    internal double Rates(ReadOnlySpan<double> state, Span<double> rate,
        ReadOnlySpan<double> leftBoundary, ReadOnlySpan<double> rightBoundary, out double wallForce, out double wallHeat)
    {
        int width = equation.Size;
        Span<double> leftFlux = stackalloc double[width];
        Span<double> rightFlux = stackalloc double[width];
        leftBoundary.CopyTo(leftFlux);
        wallForce = 0;
        wallHeat = 0;
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
            if (wall is not null)
            {
                double diameter = Math.Sqrt(4 * volume[i] / (Math.PI * Dx));
                var (momentum, heat, relax) = wall.Sources(left, diameter);
                force += momentum * volume[i];
                rate[offset + equation.Energy] += heat;
                wallHeat += heat * volume[i];
                cflTime = Math.Min(cflTime, relax);
            }
            rate[offset + equation.Momentum] += force * inverseVolume;
            wallForce += force;
            rightFlux.CopyTo(leftFlux);
            left = right;
        }
        return faces is not null ? Math.Min(cflTime, faceCflTime) : cflTime;
    }
}
