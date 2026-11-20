using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;

namespace Dino2.Core.Multicylinder;

// Each duct is oriented from the exterior reservoir toward a cylinder, including on the exhaust side.
internal sealed class ManifoldVolume
{
    private readonly GasEquation gas;
    private readonly DuctMesh trunk;
    private readonly double volume, flowArea, degreesPerSecond;
    private readonly DuctBoundary exterior;
    internal DuctBoundary Junction { get; }
    private int Integral => gas.Size + trunk.Size;
    internal int Dimension => Integral + gas.Size;
    internal int Cells => trunk.Cells;

    internal ManifoldVolume(ManifoldSide side, GasEquation gas, DuctScheme scheme, Reservoir reservoir,
        double[] mixture, double tag, double runnerArea, int cylinders, double degreesPerSecond)
    {
        this.gas = gas;
        this.degreesPerSecond = degreesPerSecond;
        volume = side.VolumeM3;
        trunk = new(side.Trunk, gas, scheme);
        flowArea = trunk.ValveArea + cylinders * runnerArea;
        exterior = new(new double[gas.Size]);
        exterior.State = gas.Encode(exterior.Values, mixture, reservoir.PressurePa, reservoir.TemperatureK, 0, tag);
        Junction = new(new double[gas.Size]);
    }

    internal void Initialize(Span<double> state)
    {
        for (int i = 0; i < Integral; i += gas.Size)
            exterior.Values.CopyTo(state.Slice(i, gas.Size));
    }

    internal double Evaluate(ReadOnlySpan<double> state, Span<double> rate)
    {
        state[..gas.Size].CopyTo(Junction.Values);
        Junction.State = gas.Decode(Junction.Values);
        var tube = state[gas.Size..Integral];
        trunk.Prepare(tube);
        Span<double> edge = stackalloc double[gas.Size];
        Span<double> outer = stackalloc double[gas.Size];
        Span<double> inner = stackalloc double[gas.Size];
        var outside = trunk.BoundaryState(tube, false, edge);
        gas.Flux(exterior.Values, exterior.State, edge, outside, outer);
        var inside = trunk.BoundaryState(tube, true, edge);
        gas.Flux(edge, inside, Junction.Values, Junction.State, inner);
        for (int i = 0; i < gas.Size; i++)
        {
            outer[i] *= trunk.ReservoirArea;
            inner[i] *= trunk.ValveArea;
        }
        double time = trunk.Rates(tube, rate[gas.Size..Integral], outer, inner);
        for (int i = gas.Size; i < Integral; i++) rate[i] /= degreesPerSecond;
        for (int i = 0; i < gas.Size; i++)
        {
            rate[Integral + i] = outer[i] / degreesPerSecond;
            rate[i] = i == gas.Momentum ? 0 : inner[i] / (volume * degreesPerSecond);
        }
        // A mixed volume must also satisfy its own characteristic time, independent of duct cell length.
        return Math.Min(time, volume / (flowArea * Junction.State.SoundSpeed));
    }

    internal void ExchangeWithRunner(ReadOnlySpan<double> fluxPerDegree, Span<double> rate)
    {
        for (int i = 0; i < gas.Size; i++)
            if (i != gas.Momentum) rate[i] -= fluxPerDegree[i] / volume;
    }

    internal void Validate(ReadOnlySpan<double> state)
    {
        for (int i = 0; i < Integral; i += gas.Size) gas.ValidateMixture(state.Slice(i, gas.Size));
    }

    internal void StartCycle(Span<double> state) => state[Integral..].Clear();

    internal double PeriodicError(ReadOnlySpan<double> initial, ReadOnlySpan<double> final)
    {
        double error = 0;
        for (int i = 0; i < Integral; i += gas.Size)
            for (int k = 0; k < gas.Size; k++)
            {
                double scale = k == gas.Energy ? exterior.State.Pressure : k == gas.Momentum
                    ? exterior.State.Density * exterior.State.SoundSpeed : exterior.State.Density;
                error = Math.Max(error, Math.Abs(final[i + k] - initial[i + k]) / (scale + Math.Abs(final[i + k])));
            }
        return error;
    }

    internal void AccumulateResidual(ReadOnlySpan<double> initial, ReadOnlySpan<double> final, Span<double> residual)
    {
        for (int k = 0; k < gas.Size; k++)
        {
            residual[k] += volume * (final[k] - initial[k]) - final[Integral + k];
            for (int cell = 0; cell < trunk.Cells; cell++)
            {
                int i = (cell + 1) * gas.Size + k;
                residual[k] += trunk.Volume(cell) * (final[i] - initial[i]);
            }
        }
    }

    internal (double JunctionPa, double TrunkPa) Pressures(ReadOnlySpan<double> state) =>
        (gas.Decode(state[..gas.Size]).Pressure, gas.Decode(state.Slice(gas.Size * trunk.Cells, gas.Size)).Pressure);
}
