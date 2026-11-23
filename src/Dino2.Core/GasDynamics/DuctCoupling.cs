using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.GasDynamics;

internal sealed class DuctBoundary(double[] values, ExteriorBoundary coupling = ExteriorBoundary.HistoricalRiemann)
{
    internal double[] Values { get; } = values;
    internal GasState State { get; set; }

    internal void ToRunner(GasEquation gas, ReadOnlySpan<double> edge, GasState inside, Span<double> flux) =>
        ReservoirBoundary.Flux(gas, coupling, Values, State, edge, inside, flux, allowUpstreamShock: true);
}

internal sealed class DuctCoupling
{
    private sealed record End(DuctMesh Mesh, int Offset, PoppetValve Valve, double[] Reservoir, GasState Ambient, ExteriorBoundary Mode);

    private readonly GasEquation gas;
    private readonly End inlet, outlet;
    private readonly int integralOffset;
    internal int Dimension => integralOffset + 4 * gas.Size + 4;
    internal double Courant { get; }
    internal double StableTime { get; private set; }
    internal long CellEvaluations { get; private set; }

    internal DuctCoupling(FourStrokeConfiguration config, double[] intake, double[] exhaust, int offset)
    {
        var path = config.Ducts!;
        gas = new GasEquation(intake.Length);
        inlet = Create(path.Intake, config.Intake, config.IntakeValve, intake, 1 - intake[0], offset, path.Scheme, path.IntakeBoundary);
        outlet = Create(path.Exhaust, config.Exhaust, config.ExhaustValve, exhaust, 0, inlet.Offset + inlet.Mesh.Size, path.Scheme, path.ExhaustBoundary);
        integralOffset = outlet.Offset + outlet.Mesh.Size;
        Courant = path.Courant;
    }

    private End Create(DuctGeometry geometry, Reservoir reservoir, PoppetValve valve, double[] fractions, double tag,
        int offset, DuctScheme scheme, ExteriorBoundary mode)
    {
        var mesh = new DuctMesh(geometry, gas, scheme);
        var state = new double[gas.Size];
        gas.Encode(state, fractions, reservoir.PressurePa, reservoir.TemperatureK, 0, tag);
        return new(mesh, offset, valve, state, gas.Decode(state), mode);
    }

    internal void Initialize(Span<double> state)
    {
        Initialize(inlet, state);
        Initialize(outlet, state);
    }

    private void Initialize(End end, Span<double> state)
    {
        for (int i = 0; i < end.Mesh.Cells; i++)
            end.Reservoir.CopyTo(state.Slice(end.Offset + i * gas.Size, gas.Size));
    }

    internal void StartCycle(Span<double> state) => state[integralOffset..].Clear();

    internal void Evaluate(double angle, double degreesPerSecond, ReadOnlySpan<double> state, Span<double> rates,
        ReadOnlySpan<double> cylinder, GasState chamber, Span<double> intake, Span<double> exhaust,
        DuctBoundary? intakeBoundary = null, DuctBoundary? exhaustBoundary = null)
    {
        StableTime = Math.Min(Evaluate(inlet, angle, state, rates, cylinder, chamber, intake, intakeBoundary),
            Evaluate(outlet, angle, state, rates, cylinder, chamber, exhaust, exhaustBoundary));
        CellEvaluations += inlet.Mesh.Cells + outlet.Mesh.Cells;
        for (int i = inlet.Offset; i < Dimension; i++) rates[i] /= degreesPerSecond;
    }

    private double Evaluate(End end, double angle, ReadOnlySpan<double> state, Span<double> rates,
        ReadOnlySpan<double> cylinder, GasState chamber, Span<double> valveFlow, DuctBoundary? exterior)
    {
        var tube = state.Slice(end.Offset, end.Mesh.Size);
        end.Mesh.Prepare(tube);
        Span<double> outer = stackalloc double[gas.Size];
        Span<double> boundary = stackalloc double[gas.Size];
        var ambientEnd = end.Mesh.BoundaryState(tube, false, boundary);
        if (exterior is null)
            ReservoirBoundary.Flux(gas, end.Mode, end.Reservoir, end.Ambient, boundary, ambientEnd, outer);
        else
            exterior.ToRunner(gas, boundary, ambientEnd, outer);
        for (int k = 0; k < gas.Size; k++) outer[k] *= end.Mesh.ReservoirArea;
        var valveEnd = end.Mesh.BoundaryState(tube, true, boundary);
        ValveFlux(end, angle, boundary, valveEnd, cylinder, chamber, valveFlow);
        int integral = IntegralOffset(end);
        outer.CopyTo(rates.Slice(integral, gas.Size));
        valveFlow.CopyTo(rates.Slice(integral + gas.Size, gas.Size));
        double time = end.Mesh.Rates(tube, rates.Slice(end.Offset, end.Mesh.Size), outer, valveFlow,
            out double wallForce, out double wallHeat);
        rates[ForceIndex(end)] = wallForce;
        rates[ForceIndex(end) + 2] = wallHeat;
        return time;
    }

    private double ValveFlux(End end, double angle, ReadOnlySpan<double> last, GasState inside,
        ReadOnlySpan<double> cylinder, GasState chamber, Span<double> result)
    {
        double area = end.Mesh.ValveArea;
        double opening = Math.Min(area, ValveFlow.EffectiveArea(end.Valve, angle));
        if (end.Mode == ExteriorBoundary.PressureReservoir)
            return PortBoundary.Flux(gas, area, opening, cylinder, chamber, last, inside, result);
        double wallPressure = gas.WallPressure(inside, true);
        result.Clear();
        if (opening == 0) { result[gas.Momentum] = wallPressure * area; return inside.Pressure; }

        // Intersect the valve capacity with an outgoing pressure/mass-flow characteristic at the pipe end.
        // The wall state anchors the characteristic at zero flow; its slope uses the local impedance.
        double conductance = area / inside.SoundSpeed;
        double low = Math.Min(wallPressure, chamber.Pressure);
        double high = Math.Max(wallPressure, chamber.Pressure);
        double pressure = (low + high) / 2;
        for (int iteration = 0; iteration < 26 && high - low > .001; iteration++)
        {
            pressure = (low + high) / 2;
            double predicted = ReactiveCylinder.Nozzle(opening, pressure, inside.Temperature, inside.GasConstant, inside.Gamma,
                chamber.Pressure, chamber.Temperature, chamber.GasConstant, chamber.Gamma);
            if (predicted > conductance * (wallPressure - pressure)) high = pressure;
            else low = pressure;
        }
        pressure = (low + high) / 2;
        double mdot = conductance * (wallPressure - pressure);
        bool toChamber = mdot >= 0;
        var donor = toChamber ? inside : chamber;
        var values = toChamber ? last : cylinder;
        for (int i = 0; i < gas.SpeciesCount; i++) result[i] = mdot * values[i] / donor.Density;
        result[gas.Momentum] = pressure * area + mdot * mdot / (inside.Density * area);
        result[gas.Energy] = mdot * (donor.Energy + donor.Pressure) / donor.Density;
        result[gas.Tag] = mdot * values[gas.Tag] / donor.Density;
        return inside.Pressure;
    }

    internal (double Intake, double Exhaust, double IntakePressure, double ExhaustPressure) Sample(double angle,
        ReadOnlySpan<double> state, ReadOnlySpan<double> cylinder, GasState chamber)
    {
        Span<double> flux = stackalloc double[gas.Size];
        Span<double> edge = stackalloc double[gas.Size];
        (double Flow, double Pressure) AtEnd(End end, ReadOnlySpan<double> state, ReadOnlySpan<double> cylinder,
            Span<double> edge, Span<double> flux)
        {
            var tube = state.Slice(end.Offset, end.Mesh.Size);
            end.Mesh.PrepareValve(tube);
            var edgeState = end.Mesh.BoundaryState(tube, true, edge);
            double p = ValveFlux(end, angle, edge, edgeState, cylinder, chamber, flux);
            return (MassFlux(flux), p);
        }

        var i = AtEnd(inlet, state, cylinder, edge, flux);
        var e = AtEnd(outlet, state, cylinder, edge, flux);
        return (i.Flow, e.Flow, i.Pressure, e.Pressure);
    }

    internal double MassFlux(ReadOnlySpan<double> flux)
    {
        double total = 0;
        for (int k = 0; k < gas.SpeciesCount; k++) total += flux[k];
        return total;
    }

    internal void ValidateState(ReadOnlySpan<double> state)
    {
        for (int i = inlet.Offset; i < integralOffset; i += gas.Size)
            gas.ValidateMixture(state.Slice(i, gas.Size));
    }

    internal ReadOnlySpan<double> OuterFlux(ReadOnlySpan<double> rates, bool intake) =>
        rates.Slice(IntegralOffset(intake ? inlet : outlet), gas.Size);

    internal double IntakeMassRatePerDegree(ReadOnlySpan<double> rates) =>
        MassFlux(rates.Slice(IntegralOffset(inlet) + gas.Size, gas.Size));

    internal void AccumulateStorageChange(ReadOnlySpan<double> initial, ReadOnlySpan<double> final, Span<double> delta)
    {
        Accumulate(inlet, initial, final, delta);
        Accumulate(outlet, initial, final, delta);
    }

    internal double PeriodicError(ReadOnlySpan<double> initial, ReadOnlySpan<double> final) =>
        Math.Max(PeriodicError(inlet, initial, final), PeriodicError(outlet, initial, final));

    private double PeriodicError(End end, ReadOnlySpan<double> initial, ReadOnlySpan<double> final)
    {
        double error = 0;
        for (int i = end.Offset; i < end.Offset + end.Mesh.Size; i += gas.Size)
        for (int k = 0; k < gas.Size; k++)
        {
            double scale = k == gas.Energy ? end.Ambient.Pressure : k == gas.Momentum
                ? end.Ambient.Density * end.Ambient.SoundSpeed : end.Ambient.Density;
            error = Math.Max(error, Math.Abs(final[i + k] - initial[i + k]) / (scale + Math.Abs(final[i + k])));
        }
        return error;
    }

    internal (double Mass, double Species, double Energy, double Momentum) Balance(ReadOnlySpan<double> initial, ReadOnlySpan<double> final,
        ReadOnlySpan<double> cylinderFluxes, double cylinderEnthalpy)
    {
        var a = EndBalance(inlet, initial, final);
        var b = EndBalance(outlet, initial, final);
        int intake = IntegralOffset(inlet) + gas.Size;
        int exhaust = IntegralOffset(outlet) + gas.Size;
        double valveMass = 0, valveSpecies = 0;
        for (int k = 0; k < gas.SpeciesCount; k++)
        {
            double residual = cylinderFluxes[k] - (final[intake + k] + final[exhaust + k]);
            valveMass += residual;
            valveSpecies = Math.Max(valveSpecies, Math.Abs(residual));
        }
        double valveEnergy = cylinderEnthalpy - (final[intake + gas.Energy] + final[exhaust + gas.Energy]);
        return (MaxMagnitude(a.Mass, b.Mass, valveMass),
            MaxMagnitude(a.Species, b.Species, valveSpecies), MaxMagnitude(a.Energy, b.Energy, valveEnergy), Math.Max(Math.Abs(a.Momentum), Math.Abs(b.Momentum)));
    }

    private static double MaxMagnitude(double a, double b, double c) =>
        Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Abs(c));

    private int IntegralOffset(End end) => integralOffset + (ReferenceEquals(end, inlet) ? 0 : 2 * gas.Size);

    private int ForceIndex(End end) => integralOffset + 4 * gas.Size + (ReferenceEquals(end, inlet) ? 0 : 1);
    internal double Heat(ReadOnlySpan<double> state) => state[ForceIndex(inlet) + 2] + state[ForceIndex(outlet) + 2];

    private (double Mass, double Species, double Energy, double Momentum) EndBalance(End end, ReadOnlySpan<double> initial, ReadOnlySpan<double> final)
    {
        Span<double> residual = stackalloc double[gas.Size];
        residual.Clear();
        Accumulate(end, initial, final, residual);
        int offset = IntegralOffset(end);
        double mass = 0, species = 0;
        for (int k = 0; k < gas.SpeciesCount; k++)
        {
            double error = residual[k] - final[offset + k] + final[offset + gas.Size + k];
            mass += error;
            species = Math.Max(species, Math.Abs(error));
        }
        return (mass, species, residual[gas.Energy] - final[offset + gas.Energy] + final[offset + gas.Size + gas.Energy] - final[ForceIndex(end) + 2],
            residual[gas.Momentum] - final[offset + gas.Momentum] + final[offset + gas.Size + gas.Momentum] - final[ForceIndex(end)]);
    }

    private void Accumulate(End end, ReadOnlySpan<double> initial, ReadOnlySpan<double> final, Span<double> residual)
    {
        for (int i = 0; i < end.Mesh.Cells; i++)
        {
            int offset = end.Offset + i * gas.Size;
            double volume = end.Mesh.Volume(i);
            for (int k = 0; k < gas.Size; k++) residual[k] += volume * (final[offset + k] - initial[offset + k]);
        }
    }
}
