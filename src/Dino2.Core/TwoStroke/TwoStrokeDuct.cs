using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.TwoStroke;

internal sealed class TwoStrokeDuct(DuctMesh mesh, GasEquation gas, double[] ghost, GasState reservoir, TwoStrokeProfile? profile)
{
    internal int Size => mesh.Size;
    internal int Cells => mesh.Cells;
    internal double Volume(int cell) => mesh.Volume(cell);

    internal void Initialize(Span<double> values)
    {
        for (int cell = 0; cell < mesh.Cells; cell++)
            ghost.CopyTo(values.Slice(cell * gas.Size, gas.Size));
    }

    internal static TwoStrokeDuct Intake(TwoStrokeConfiguration c, GasEquation gas, double[] fractions, TwoStrokeProfile? profile) =>
        Create(new(c.IntakeDuct, gas, c.Numerics.Scheme, boundedPassiveTag: true), gas, fractions, c.Intake.PressurePa, c.Intake.TemperatureK, 1, profile);

    internal static TwoStrokeDuct Exhaust(TwoStrokeConfiguration c, GasEquation gas, TwoStrokeProfile? profile)
    {
        var e = c.Expansion;
        var mesh = new DuctMesh(new(e.LengthM, e.StingerDiameterM, e.InletDiameterM, e.Cells), gas,
            c.Numerics.Scheme, e.DiameterFromTail, e.MeshKnots(), boundedPassiveTag: true);
        var air = SpeciesThermodynamics.FreshMixture(c.Combustion.AirFuelRatio);
        double fraction = 1 - air[0];
        return Create(mesh, gas, [0, air[1] / fraction, air[2] / fraction, 0, 0, 0, 0],
            c.Tail.PressurePa, c.Tail.TemperatureK, 0, profile);
    }

    private static TwoStrokeDuct Create(DuctMesh mesh, GasEquation gas, double[] fractions, double p, double t, double tag, TwoStrokeProfile? profile)
    {
        var ghost = new double[gas.Size];
        var boundary = gas.Encode(ghost, fractions, p, t, 0, tag);
        return new(mesh, gas, ghost, boundary, profile);
    }

    // Positive flux enters the engine. The single port flux is also the duct right boundary.
    internal double Rates(ReadOnlySpan<double> tube, Span<double> rate, GasState plenum,
        ReadOnlySpan<double> donor, double portArea, Reed? reed, Span<double> outer, Span<double> inner, out double facePressure)
    {
        Span<double> face = stackalloc double[gas.Size];
        long start = profile is null ? 0 : TwoStrokeProfile.Timestamp();
        mesh.Prepare(tube);
        if (profile is not null)
        {
            long now = TwoStrokeProfile.Timestamp();
            profile.DuctPreparation += now - start;
            start = now;
        }
        var left = mesh.BoundaryState(tube, false, face);
        gas.Flux(ghost, reservoir, face, left, outer);
        for (int i = 0; i < outer.Length; i++) outer[i] *= mesh.ReservoirArea;

        var right = mesh.BoundaryState(tube, true, face);
        double wall = Math.Max(1, gas.WallPressure(right, true));
        double limit = plenum.Pressure + (reed?.OpeningPressurePa ?? 0);
        inner.Clear();
        if (reed is not null ? wall <= limit : portArea <= 0)
        {
            facePressure = wall;
            inner[gas.Momentum] = wall * mesh.ValveArea;
        }
        else
        {
            double low = Math.Min(wall, limit), high = Math.Max(wall, limit);
            double impedance = mesh.ValveArea / right.SoundSpeed;
            for (int iteration = 0; iteration < 20; iteration++)
            {
                double p = (low + high) * .5;
                double area = Math.Min(mesh.ValveArea, reed?.EffectiveArea(p - plenum.Pressure) ?? portArea);
                double flow = ReactiveCylinder.Nozzle(area, p, right.Temperature, right.GasConstant, right.Gamma,
                    plenum.Pressure, plenum.Temperature, plenum.GasConstant, plenum.Gamma);
                if (impedance * (wall - p) > flow) low = p;
                else high = p;
            }
            facePressure = (low + high) * .5;
            double effectiveArea = Math.Min(mesh.ValveArea, reed?.EffectiveArea(facePressure - plenum.Pressure) ?? portArea);
            double mass = ReactiveCylinder.Nozzle(effectiveArea, facePressure, right.Temperature, right.GasConstant, right.Gamma,
                plenum.Pressure, plenum.Temperature, plenum.GasConstant, plenum.Gamma);
            var source = mass >= 0 ? right : plenum;
            var values = mass >= 0 ? face : donor;
            for (int i = 0; i < gas.SpeciesCount; i++) inner[i] = mass * values[i] / source.Density;
            inner[gas.Energy] = mass * (source.Energy + source.Pressure) / source.Density;
            inner[gas.Tag] = mass * values[gas.Tag] / source.Density;
            inner[gas.Momentum] = facePressure * mesh.ValveArea + mass * mass / (right.Density * mesh.ValveArea);
        }

        if (profile is not null)
        {
            long now = TwoStrokeProfile.Timestamp();
            profile.InterfaceFlux += now - start;
            start = now;
        }
        double dt = mesh.Rates(tube, rate, outer, inner);
        if (profile is not null) profile.DuctBalance += TwoStrokeProfile.Timestamp() - start;
        return dt;
    }
}
