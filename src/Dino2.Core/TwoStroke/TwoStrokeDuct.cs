using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.TwoStroke;

internal sealed class TwoStrokeDuct(DuctMesh mesh, GasEquation gas, double[] ghost, GasState reservoir,
    ExteriorBoundary boundaryMode, TwoStrokeProfile? profile)
{
    internal int Size => mesh.Size;
    internal int Cells => mesh.Cells;
    internal double Volume(int cell) => mesh.Volume(cell);
    internal void Validate(ReadOnlySpan<double> tube)
    {
        for (int i = 0; i < tube.Length; i += gas.Size)
            gas.ValidateMixture(tube.Slice(i, gas.Size));
    }

    internal void Initialize(Span<double> values)
    {
        for (int cell = 0; cell < mesh.Cells; cell++)
            ghost.CopyTo(values.Slice(cell * gas.Size, gas.Size));
    }

    internal static TwoStrokeDuct Intake(TwoStrokeConfiguration c, GasEquation gas, double[] fractions, TwoStrokeProfile? profile) =>
        Create(new(c.IntakeDuct, gas, c.Numerics.Scheme, boundedPassiveTag: true), gas, fractions,
            c.Intake.PressurePa, c.Intake.TemperatureK, 1, c.Numerics.Exterior, profile);

    internal static TwoStrokeDuct Exhaust(TwoStrokeConfiguration c, GasEquation gas, TwoStrokeProfile? profile)
        => Create(ExpansionMesh(c.Expansion, gas, c.Numerics.Scheme), gas, TailMixture(c), c.Tail.PressurePa,
            c.Tail.TemperatureK, 0, c.Numerics.Exterior, profile);

    internal static TwoStrokeDuct Primary(TwoStrokeConfiguration c, DuctGeometry primary, GasEquation gas) =>
        Create(new(primary, gas, c.Numerics.Scheme, boundedPassiveTag: true), gas, TailMixture(c),
            c.Tail.PressurePa, c.Tail.TemperatureK, 0, c.Numerics.Exterior, null);

    internal static DuctMesh ExpansionMesh(ExpansionChamber e, GasEquation gas, DuctScheme scheme) =>
        new(new DuctGeometry(e.LengthM, e.StingerDiameterM, e.InletDiameterM, e.Cells) { Wall = e.Wall }, gas,
            scheme, e.DiameterFromTail, e.MeshKnots(), boundedPassiveTag: true);

    internal static double[] TailMixture(TwoStrokeConfiguration c)
    {
        var air = SpeciesThermodynamics.FreshMixture(c.Combustion.AirFuelRatio);
        double fraction = 1 - air[0];
        return [0, air[1] / fraction, air[2] / fraction, 0, 0, 0, 0];
    }

    private static TwoStrokeDuct Create(DuctMesh mesh, GasEquation gas, double[] fractions, double p, double t, double tag,
        ExteriorBoundary boundaryMode, TwoStrokeProfile? profile)
    {
        var ghost = new double[gas.Size];
        var boundary = gas.Encode(ghost, fractions, p, t, 0, tag);
        return new(mesh, gas, ghost, boundary, boundaryMode, profile);
    }

    // Positive flux enters the engine. The single port flux is also the duct right boundary.
    internal double Rates(ReadOnlySpan<double> tube, Span<double> rate, GasState plenum,
        ReadOnlySpan<double> donor, double portArea, Reed? reed, Span<double> outer, Span<double> inner, out double facePressure,
        out double wallHeat, DuctBoundary? exterior = null, double dynamicReedLiftM = double.NaN)
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
        if (exterior is null)
            ReservoirBoundary.Flux(gas, boundaryMode, ghost, reservoir, face, left, outer);
        else
            exterior.ToRunner(gas, face, left, outer);
        for (int i = 0; i < outer.Length; i++) outer[i] *= mesh.ReservoirArea;

        var right = mesh.BoundaryState(tube, true, face);
        double wall = gas.WallPressure(right, true);
        if (boundaryMode != ExteriorBoundary.PressureReservoir) wall = Math.Max(1, wall);
        bool dynamicReedActive = reed is not null && double.IsFinite(dynamicReedLiftM);
        double limit = plenum.Pressure + (reed?.OpeningPressurePa ?? 0);
        double instantaneousAperture = reed is null ? portArea : dynamicReedActive
            ? reed.DynamicArea(dynamicReedLiftM) : reed.EffectiveArea(wall - plenum.Pressure);
        inner.Clear();
        if (boundaryMode == ExteriorBoundary.PressureReservoir)
        {
            bool dynamicReed = dynamicReedActive;
            double reedArea = reed is null ? portArea : dynamicReed
                ? reed.DynamicArea(dynamicReedLiftM) : reed.EffectiveArea(wall - plenum.Pressure);
            double aperture = Math.Min(mesh.ValveArea, reedArea);
            facePressure = reed is null
                ? PortBoundary.Flux(gas, mesh.ValveArea, aperture, donor, plenum, face, right, inner)
                : dynamicReed
                    ? PortBoundary.Flux(gas, mesh.ValveArea, aperture, donor, plenum, face, right, inner)
                    : PortBoundary.OneWayFlux(gas, mesh.ValveArea, aperture, donor, plenum, face, right, inner);
        }
        else if (reed is not null && !dynamicReedActive ? wall <= limit : instantaneousAperture <= 0)
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
                double area = Math.Min(mesh.ValveArea, reed is null ? portArea : dynamicReedActive
                    ? reed.DynamicArea(dynamicReedLiftM) : reed.EffectiveArea(p - plenum.Pressure));
                double flow = ReactiveCylinder.Nozzle(area, p, right.Temperature, right.GasConstant, right.Gamma,
                    plenum.Pressure, plenum.Temperature, plenum.GasConstant, plenum.Gamma);
                if (impedance * (wall - p) > flow) low = p;
                else high = p;
            }
            facePressure = (low + high) * .5;
            double effectiveArea = Math.Min(mesh.ValveArea, reed is null ? portArea : dynamicReedActive
                ? reed.DynamicArea(dynamicReedLiftM) : reed.EffectiveArea(facePressure - plenum.Pressure));
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
        double dt = mesh.Rates(tube, rate, outer, inner, out _, out wallHeat);
        if (profile is not null) profile.DuctBalance += TwoStrokeProfile.Timestamp() - start;
        return dt;
    }
}
