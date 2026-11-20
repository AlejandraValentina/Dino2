using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Multicylinder;
using Dino2.Core.Thermochemistry;

internal static class ManifoldChecks
{
    internal static void Run()
    {
        var rest = Pulse(32, 0, .35);
        if (rest.Amplitude > 1e-7 || rest.Reflection > 1e-7) throw new Exception("La unión en reposo debe conservar presión constante.");
        var coarse = Pulse(32, 300, .35);
        var fine = Pulse(64, 300, .35);
        var time = Pulse(64, 300, .175);
        Console.WriteLine($"UNION dx={coarse.Amplitude:F3}/{fine.Amplitude:F3} Pa; dt={fine.Amplitude:F3}/{time.Amplitude:F3} Pa; " +
            $"retorno={fine.Reflection:F3} Pa; c={fine.Sound:F2} m/s; arribo={fine.Arrival:E4} s; " +
            $"reversión={time.Inverse}; balances={time.Species:E2} kg/{time.Energy:E2} J");
        if (fine.Amplitude < 15 || fine.Reflection < 2 || fine.Inverse <= 0 ||
            Math.Abs(fine.Arrival - (.4 - .025) / fine.Sound) > .00035 ||
            Math.Abs(time.Amplitude - fine.Amplitude) > 1 ||
            Math.Abs(fine.Amplitude - coarse.Amplitude) > 3 ||
            new[] { coarse, fine, time }.Any(p => p.Species > 5e-13 || p.Energy > 5e-8))
            throw new Exception("Verificación independiente de transmisión, retorno y conservación de la unión.");
    }

    private sealed record Wave(double Amplitude, double Reflection, double Arrival, double Inverse,
        double Species, double Energy, double Sound);

    private static Wave Pulse(int cells, double amplitude, double cfl)
    {
        const double pressure = 1e5, temperature = 320, length = .60;
        var gas = new GasEquation(7);
        double[] fresh = SpeciesThermodynamics.FreshMixture(SpeciesThermodynamics.StoichiometricAfr);
        Array.Resize(ref fresh, 7);
        var pipe = new DuctGeometry(length, .04, .04, cells);
        var branch = new DuctMesh(pipe, gas, DuctScheme.Muscl);
        var ambient = new double[gas.Size];
        var initialGas = gas.Encode(ambient, fresh, pressure, temperature, 0, 1 - fresh[0]);
        var junction = new ManifoldVolume(new(pipe, .00055), gas, DuctScheme.Muscl, new(pressure, temperature),
            fresh, 1 - fresh[0], branch.ReservoirArea, 2, 1);
        var network = new double[junction.Dimension + 2 * branch.Size];
        junction.Initialize(network.AsSpan(0, junction.Dimension));
        for (int i = junction.Dimension; i < network.Length; i += gas.Size)
            ambient.CopyTo(network.AsSpan(i, gas.Size));
        for (int i = 0; i < cells; i++)
        {
            double location = (i + .5) * branch.Dx;
            double bump = amplitude * Math.Exp(-Math.Pow((location - .25) / .055, 2));
            var pulse = network.AsSpan((i + 1) * gas.Size, gas.Size);
            gas.Encode(pulse, fresh, pressure + bump, temperature * Math.Pow(1 + bump / pressure,
                (initialGas.Gamma - 1) / initialGas.Gamma), bump / (initialGas.Density * initialGas.SoundSpeed), 1 - fresh[0]);
        }
        // Distinct composition in one sealed runner exercises species transfer when the flow reverses.
        if (amplitude > 0)
        {
            double[] other = SpeciesThermodynamics.ExhaustMixture(SpeciesThermodynamics.StoichiometricAfr);
            Array.Resize(ref other, 7);
            for (int i = 0; i < branch.Cells; i++)
                gas.Encode(network.AsSpan(junction.Dimension + branch.Size + gas.Size * i, gas.Size),
                    other, pressure, temperature, 0, 0);
        }
        var initial = (double[])network.Clone();
        var rates = new double[network.Length];
        var stage = new double[network.Length];
        double max = 0, reflection = 0, arrival = 0, inverse = 0;
        double dt = cfl * Math.Min(branch.Dx, .00055 / (3 * branch.ValveArea)) / initialGas.SoundSpeed;
        for (double t = 0; t < .0054; t += dt)
        {
            Derivative(network);
            for (int i = 0; i < network.Length; i++) stage[i] = network[i] + dt * rates[i];
            Derivative(stage);
            for (int i = 0; i < network.Length; i++) network[i] = .5 * (network[i] + stage[i] + dt * rates[i]);
            junction.Validate(network.AsSpan(0, junction.Dimension));
            int monitor = Math.Clamp((int)(.025 / branch.Dx), 0, branch.Cells - 1);
            double p = gas.Decode(network.AsSpan(junction.Dimension + monitor * gas.Size, gas.Size)).Pressure;
            if (t < .0025 && p - pressure > max) { max = p - pressure; arrival = t + dt; }
            double trunk = gas.Decode(network.AsSpan((int)(.44 / branch.Dx + 1) * gas.Size, gas.Size)).Pressure;
            if (t > .00135 && t < .003) reflection = Math.Max(reflection, Math.Abs(trunk - pressure));
        }
        Span<double> residual = stackalloc double[gas.Size];
        residual.Clear();
        junction.AccumulateResidual(initial.AsSpan(0, junction.Dimension), network.AsSpan(0, junction.Dimension), residual);
        for (int b = 0; b < 2; b++)
            for (int i = 0; i < branch.Cells; i++)
                for (int k = 0; k < gas.Size; k++)
                {
                    int j = junction.Dimension + b * branch.Size + i * gas.Size + k;
                    residual[k] += branch.Volume(i) * (network[j] - initial[j]);
                }
        double maxSpecies = 0;
        for (int k = 0; k < gas.SpeciesCount; k++) maxSpecies = Math.Max(maxSpecies, Math.Abs(residual[k]));
        return new(max, reflection, arrival, inverse, maxSpecies, Math.Abs(residual[gas.Energy]), initialGas.SoundSpeed);

        void Derivative(double[] values)
        {
            var junctionRate = rates.AsSpan(0, junction.Dimension);
            junction.Evaluate(values.AsSpan(0, junction.Dimension), junctionRate);
            Span<double> edge = stackalloc double[10];
            Span<double> flow = stackalloc double[10];
            Span<double> wall = stackalloc double[10];
            for (int b = 0; b < 2; b++)
            {
                int offset = junction.Dimension + b * branch.Size;
                var tube = values.AsSpan(offset, branch.Size);
                var change = rates.AsSpan(offset, branch.Size);
                branch.Prepare(tube);
                var inner = branch.BoundaryState(tube, false, edge);
                gas.Flux(junction.Junction.Values, junction.Junction.State, edge, inner, flow);
                double net = 0;
                for (int i = 0; i < gas.Size; i++)
                {
                    flow[i] *= branch.ReservoirArea;
                    if (i < gas.SpeciesCount) net += flow[i];
                }
                inverse = Math.Max(inverse, -net);
                junction.ExchangeWithRunner(flow, junctionRate);
                var end = branch.BoundaryState(tube, true, edge);
                wall.Clear();
                wall[gas.Momentum] = gas.WallPressure(end, true) * branch.ValveArea;
                branch.Rates(tube, change, flow, wall);
            }
        }
    }
}
