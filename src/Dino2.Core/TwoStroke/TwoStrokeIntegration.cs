using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.TwoStroke;

public sealed partial class TwoStrokeSimulator
{
    private TwoStrokePoint Run(bool trace, CancellationToken token, double[]? seed,
        out double[]? periodicState, out TwoStrokeConvergence? diagnostic)
    {
        var state = new double[dimension];
        var previous = new double[dimension];
        var k1 = new double[dimension];
        var k2 = new double[dimension];
        var predictor = new double[dimension];
        var candidate = new double[dimension];
        Span<double> inventory = stackalloc double[N], finalInventory = stackalloc double[N];
        Span<double> composition = stackalloc double[N], chemistry = stackalloc double[N], errors = stackalloc double[N];
        var physical = new double[N];
        periodicState = null;
        diagnostic = null;
        if (seed is null) Initialize(state);
        else seed.CopyTo(state, 0);
        double periodic = double.PositiveInfinity;
        int streak = 0, totalSteps = 0, rejected = 0;
        for (int cycle = 1; cycle <= c.Numerics.MaximumCycles; cycle++)
        {
            token.ThrowIfCancellationRequested();
            state.AsSpan(Wc, IntakeOffset - Wc).Clear();
            state.CopyTo(previous, 0);
            var initial = Inventory(state, inventory, out double initialMarker);
            double angle = open, peakP = 0, peakT = 0, trap = 0, trapMass = 0, removedMarker = 0;
            var trappedSpecies = default(SpeciesMasses);
            var residualSpecies = default(SpeciesMasses);
            bool record = trace && streak == 1;
            var samples = new List<TwoStrokeSample>();
            double nextSample = open;
            int steps = 0;
            burning = false;
            burnFuel = 0;
            foreach (double end in events)
            {
                while (angle < end - 1e-10)
                {
                    if ((steps & 63) == 0) token.ThrowIfCancellationRequested();
                    double limit = Evaluate(angle, state, k1, out var first);
                    if (record && angle >= nextSample - 1e-9)
                    {
                        samples.Add(first);
                        nextSample = angle + 1;
                    }

                    double h = Math.Min(end - angle, Math.Min(c.Numerics.MaximumStepDeg, .98 * limit));
                    TwoStrokeSample last;
                    string? cause = null;
                    ModelDomainException? interfaceDomain = null;
                    while (true)
                    {
                        if (!double.IsFinite(h) || h <= 0 || (h < 1e-7 && h < end - angle))
                        {
                            if (interfaceDomain is not null) throw new ModelDomainException($"{rpm} RPM, ciclo {cycle}, {angle:G7}°: {interfaceDomain.Message}");
                            throw new NumericalStateException($"Paso 2T insuficiente ciclo {cycle} a {angle:G7}°: {cause} [pc={first.CylinderPressurePa:G5},tc={first.CylinderTemperatureK:G5},pk={first.CrankcasePressurePa:G5},tk={first.CrankcaseTemperatureK:G5},e={first.ExhaustOutKgPerS:G6},t={first.TransferKgPerS:G6},work={state[Wc]:G6},q={state[Q]:G6}]");
                        }
                        for (int i = 0; i < dimension; i++) predictor[i] = state[i] + h * k1[i];
                        try
                        {
                            double stageLimit = Evaluate(angle + h, predictor, k2, out last);
                            if (h <= stageLimit)
                            {
                                for (int i = 0; i < dimension; i++) candidate[i] = state[i] + h * .5 * (k1[i] + k2[i]);
                                ValidateAccepted(angle + h, candidate);
                                break;
                            }
                            h = Math.Min(h / 2, .98 * stageLimit);
                            rejected++;
                        }
                        catch (NumericalStateException ex) { cause = ex.Message; h /= 2; rejected++; }
                        catch (ModelDomainException ex) { interfaceDomain = ex; h /= 2; rejected++; }
                    }

                    (state, candidate) = (candidate, state);
                    peakP = Math.Max(peakP, Math.Max(first.CylinderPressurePa, last.CylinderPressurePa));
                    peakT = Math.Max(peakT, Math.Max(first.CylinderTemperatureK, last.CylinderTemperatureK));
                    angle += h;
                    steps++;
                    totalSteps++;
                }

                angle = end;
                if (end == close)
                {
                    trap = SpeciesThermodynamics.TotalMass(state.AsSpan(0, N));
                    trapMass = trap + SpeciesThermodynamics.TotalMass(state.AsSpan(Old, N));
                    for (int i = 0; i < N; i++) composition[i] = state[i] + state[Old + i];
                    trappedSpecies = SpeciesMasses.From(composition);
                    residualSpecies = SpeciesMasses.From(state.AsSpan(Old, N));
                }

                if (end == soc)
                {
                    removedMarker = SpeciesThermodynamics.TotalMass(state.AsSpan(0, N));
                    for (int i = 0; i < N; i++)
                    {
                        state[Old + i] += state[i];
                        state[i] = 0;
                    }
                    burnFuel = c.Combustion.BurnFraction * Math.Max(0,
                        Math.Min(state[Old], state[Old + 1] / -SpeciesThermodynamics.ReactionMassRatio(1)));
                    burning = burnFuel > 0;
                }

                Evaluate(angle, state, k1, out var sample);
                if (record)
                {
                    if (samples.Count == 0 || samples[^1].AngleDeg != angle) samples.Add(sample);
                    nextSample = angle + 1;
                }
            }

            double finalVolume = SliderCrank.At(c.Geometry, open + 360).VolumeM3;
            CylinderComposition(open + 360, state, finalVolume, composition, out _);
            for (int i = 0; i < N; i++)
            {
                chemistry[i] = composition[i] - state[Old + i];
                state[Old + i] = composition[i];
            }
            double final = Inventory(state, finalInventory, out double finalMarker);
            var (residual, dominant) = PeriodicError(previous, state);
            diagnostic = new(cycle, residual, double.IsFinite(periodic) ? periodic : null, dominant, totalSteps, rejected);
            periodic = residual;
            streak = periodic <= c.Numerics.PeriodicTolerance ? streak + 1 : 0;
            if (streak >= 2)
            {
                // Compare every species independently against stoichiometric reaction extents.
                double co = (chemistry[5] - burnFuel * SpeciesThermodynamics.ReactionMassRatio(5)) /
                    SpeciesThermodynamics.MolecularWeight(5);
                double h2 = (chemistry[6] - burnFuel * SpeciesThermodynamics.ReactionMassRatio(6)) /
                    SpeciesThermodynamics.MolecularWeight(6);
                for (int i = 0; i < N; i++)
                {
                    double nu = i switch { 1 => .5 * (co + h2), 3 => -co, 4 => -h2, 5 => co, 6 => h2, _ => 0 };
                    double expected = burnFuel * SpeciesThermodynamics.ReactionMassRatio(i) +
                        nu * SpeciesThermodynamics.MolecularWeight(i);
                    errors[i] = finalInventory[i] - inventory[i] - state[Outer + i] - expected;
                    physical[i] = finalInventory[i] - inventory[i] - state[Outer + i];
                }

                double Element(int element)
                {
                    double sum = 0;
                    for (int i = 0; i < N; i++) sum += physical[i] * SpeciesThermodynamics.ElementMassFraction(i, element);
                    return sum;
                }
                var balance = new TwoStrokeBalance(physical.Sum(), SpeciesMasses.From(errors), Element(0), Element(1),
                    Element(2), Element(3), final - initial + state[Wc] + state[Wk] + state[Q] - state[OuterH] - state[DuctHeat],
                    finalMarker - initialMarker + removedMarker - state[OuterTag]);
                double work = state[Wc] + state[Wk], volume = c.Geometry.DisplacementM3;
                double shaft = work - LossModels.FrictionMep(c.Friction, peakP, meanSpeed) * volume;
                double power = shaft * rpm / 60;
                double reference = volume * c.Intake.PressurePa /
                    (SpeciesThermodynamics.GasConstant(freshMixture) * c.Intake.TemperatureK);
                double supplied = state[Delivered] + state[ExhaustReturn];
                periodicState = state;
                return new(rpm, work * rpm / 60, power, shaft / (2 * Math.PI), work / volume, shaft / volume,
                    state[Wc], state[Wk], state[Q], state[FuelIn], burnFuel,
                    power > 0 ? state[FuelIn] * rpm / 60 * 3.6e9 / power : null, state[Delivered], state[TransferReturn],
                    trap, state[Short], state[ExhaustReturn], state[Delivered] / reference,
                    supplied > 0 ? trap / supplied : null, trap / trapMass, trap / reference,
                    trapMass - trap, trappedSpecies, residualSpecies, peakP, peakT, cycle, steps, totalSteps, rejected, periodic, balance, samples.AsReadOnly());
            }
        }
        throw new CycleConvergenceException($"2T a {rpm} RPM: cambio {periodic:E3} tras {c.Numerics.MaximumCycles} ciclos. Componente dominante: {diagnostic?.Dominant}.");
    }

    private void ValidateAccepted(double angle, ReadOnlySpan<double> state)
    {
        Span<double> masses = stackalloc double[N];
        CylinderComposition(angle, state, SliderCrank.At(c.Geometry, angle).VolumeM3, masses, out _);
        var crankcase = state.Slice(Case, N);
        SpeciesThermodynamics.Temperature(crankcase, state[Uk], c.Intake.TemperatureK);
        inlet.Validate(state.Slice(IntakeOffset, inlet.Size));
        outlet.Validate(state.Slice(outletOffset, outlet.Size));
    }

    private double Inventory(ReadOnlySpan<double> state, Span<double> species, out double marker)
    {
        for (int i = 0; i < N; i++) species[i] = state[i] + state[Old + i] + state[Case + i];
        double energy = state[Uc] + state[Uk], fresh = state[CaseTag] + SpeciesThermodynamics.TotalMass(state[..N]);
        AddDuct(inlet, state[IntakeOffset..], species, ref energy, ref fresh);
        AddDuct(outlet, state[outletOffset..], species, ref energy, ref fresh);
        marker = fresh;
        return energy;
    }

    private void AddDuct(TwoStrokeDuct duct, ReadOnlySpan<double> tube, Span<double> species, ref double energy, ref double fresh)
    {
        for (int cell = 0; cell < duct.Cells; cell++)
        {
            double v = duct.Volume(cell);
            var state = tube.Slice(cell * Width, Width);
            for (int i = 0; i < N; i++) species[i] += state[i] * v;
            energy += state[gas.Energy] * v;
            fresh += state[gas.Tag] * v;
        }
    }

    private (double Error, string Dominant) PeriodicError(ReadOnlySpan<double> before, ReadOnlySpan<double> after)
    {
        double error = 0;
        int worst = 0;
        double referenceMass = c.Geometry.DisplacementM3 * c.Intake.PressurePa / (280 * c.Intake.TemperatureK);
        for (int i = 0; i < dimension; i++)
        {
            if (i >= Wc && i < IntakeOffset) { i = IntakeOffset - 1; continue; }
            // Keep the stage 6 normalization to compare convergence under the same acceptance criterion.
            double floor = i < Wc ? (i is Uc or Uk ? referenceMass * 1e6 : referenceMass)
                : ((i - IntakeOffset) % Width) switch { N => 350, N + 1 => 1e6, _ => 1 };
            double residual = Math.Abs(after[i] - before[i]) /
                Math.Max(floor, Math.Max(Math.Abs(after[i]), Math.Abs(before[i])));
            if (residual > error) { error = residual; worst = i; }
        }

        return (error, DominantComponent(worst));
    }

    private string DominantComponent(int index)
    {
        if (index < N) return $"cilindro fresco · {SpeciesThermodynamics.Name(index)}";
        if (index < 2 * N) return $"cilindro residual · {SpeciesThermodynamics.Name(index - Old)}";
        if (index == Uc) return "energía del cilindro";
        if (index < Uk) return $"cárter · {SpeciesThermodynamics.Name(index - Case)}";
        if (index == Uk) return "energía del cárter";
        if (index == CaseTag) return "marcador fresco del cárter";
        bool isIntake = index < outletOffset;
        int local = index - (isIntake ? IntakeOffset : outletOffset), field = local % Width;
        string variable = field < N ? SpeciesThermodynamics.Name(field) :
            field == gas.Momentum ? "momento" : field == gas.Energy ? "energía" : "marcador fresco";
        return $"{(isIntake ? "admisión" : "escape")} · celda {local / Width + 1} · {variable}";
    }
}
