using System.Diagnostics;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.Multicylinder;

public static class MultiSimulator
{
    public static MultiResult Run(MultiConfiguration configuration, CancellationToken cancellation = default,
        IProgress<string>? progress = null) => Run(configuration, true, cancellation, progress);

    internal static MultiResult Run(MultiConfiguration configuration, bool captureTrace, CancellationToken cancellation,
        IProgress<string>? progress = null)
    {
        configuration.Validate();
        var clock = Stopwatch.StartNew();
        var points = new List<MultiPoint>();
        foreach (int rpm in configuration.Cylinder.Speeds.Values())
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report($"{rpm} RPM · {configuration.Phases.Count} cilindros");
            if (configuration.Cylinder.OperatingPointError(rpm) is { } error)
                points.Add(new(rpm, MultiStatus.OutsideDomain, error, 1, 0, 0, null, [], [], null));
            else
                points.Add(new CoupledCycle(configuration, rpm, captureTrace, progress).Run(cancellation));
        }
        return new(configuration, points.AsReadOnly(), clock.Elapsed.TotalMilliseconds) {
            Provenance = ResultProvenance.Capture(MultiConfiguration.ModelId, configuration.Cylinder.ModelIdentifier + "-network-recovery-v1") };
    }

    internal static double Wrap(double angle) => FiringPhases.Wrap(angle, 720);

    private sealed class CoupledCycle
    {
        private enum EventKind { Boundary, ResetTag, IntakeClose, Fire, EndBurn, Freeze }
        private sealed record Event(Chamber Chamber, EventKind Kind);

        private sealed class Chamber(ReactiveCylinder model, int offset, double phase)
        {
            internal ReactiveCylinder Model { get; } = model;
            internal int Offset { get; } = offset;
            internal double Phase { get; } = phase;
            internal double IntervalStart, PeakPressure, PeakTemperature, PreviousWork = double.NaN;
            internal double InitialFuelTarget, InitialDuration, FreshAtClose, Consumed, Periodic;
            internal bool InitiallyBurning, InitiallyEquilibrating;
            internal List<CycleSample> Trace { get; } = [];
            internal double Local(double angle) => Wrap(angle - Phase);
            internal Span<double> At(double[] values) => values.AsSpan(Offset, Model.Dimension);
            internal Span<double> IntakeTotals(double[] values) => values.AsSpan(Offset + Model.Dimension, 2);
        }

        private readonly MultiConfiguration config;
        private readonly FourStrokeConfiguration cylinder;
        private readonly int rpm, intakeOffset, exhaustOffset;
        private readonly bool captureTrace;
        private readonly IProgress<string>? progress;
        private readonly double degreesPerSecond, ivo;
        private readonly Chamber[] chambers;
        private readonly GasEquation gas = new(SpeciesThermodynamics.ExtendedCount);
        private readonly ManifoldVolume? intake, exhaust;
        private readonly double[] initial, stage, rate;
        private double[] state, next;
        private readonly List<ManifoldSample> samples = [];
        private readonly SortedDictionary<double, List<Event>> events = [];
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long accepted, rejected;
        private int cycles;
        private double residual = 1;

        internal CoupledCycle(MultiConfiguration config, int rpm, bool captureTrace, IProgress<string>? progress)
        {
            this.config = config;
            this.rpm = rpm;
            this.captureTrace = captureTrace;
            this.progress = progress;
            cylinder = config.Cylinder;
            degreesPerSecond = rpm * 6.0;
            ivo = Wrap(cylinder.IntakeValve.OpensDeg);
            var phases = config.Phases.Values();
            chambers = new Chamber[phases.Length];
            int size = 0;
            for (int i = 0; i < chambers.Length; i++)
            {
                chambers[i] = new(new ReactiveCylinder(cylinder, rpm), size, phases[i]);
                size += chambers[i].Model.Dimension + 2;
            }
            intakeOffset = size;
            if (config.Manifolds is { } shared)
            {
                double[] fresh = SpeciesThermodynamics.FreshMixture(cylinder.Combustion.AirFuelRatio);
                double[] burned = SpeciesThermodynamics.ExhaustMixture(cylinder.Combustion.AirFuelRatio);
                Array.Resize(ref fresh, gas.SpeciesCount);
                Array.Resize(ref burned, gas.SpeciesCount);
                var ducts = cylinder.Ducts!;
                double Area(double diameter) => Math.PI * diameter * diameter / 4;
                intake = new(shared.Intake, gas, ducts.Scheme, cylinder.Intake, fresh, 1 - fresh[0],
                    Area(ducts.Intake.ReservoirDiameterM), phases.Length, degreesPerSecond, ducts.IntakeBoundary);
                exhaust = new(shared.Exhaust, gas, ducts.Scheme, cylinder.Exhaust, burned, 0,
                    Area(ducts.Exhaust.ReservoirDiameterM), phases.Length, degreesPerSecond, ducts.ExhaustBoundary);
                size += intake.Dimension;
            }
            exhaustOffset = size;
            size += exhaust?.Dimension ?? 0;
            state = new double[size]; next = new double[size]; initial = new double[size];
            stage = new double[size]; rate = new double[size];
            foreach (var chamber in chambers) chamber.Model.Initialize(chamber.At(state), chamber.Local(0));
            if (intake is not null)
            {
                intake.Initialize(IntakeAt(state));
                exhaust!.Initialize(ExhaustAt(state));
            }
        }

        private Span<double> IntakeAt(double[] y) => y.AsSpan(intakeOffset, intake!.Dimension);
        private Span<double> ExhaustAt(double[] y) => y.AsSpan(exhaustOffset, exhaust!.Dimension);

        internal MultiPoint Run(CancellationToken cancellation)
        {
            try
            {
                int consecutive = 0;
                for (cycles = 1; cycles <= cylinder.Solver.MaximumCycles; cycles++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    StartCycle();
                    Integrate(cancellation);
                    residual = PeriodicError();
                    if (cycles % 5 == 0)
                        progress?.Report($"{rpm} RPM · ciclo {cycles} · periodicidad {residual:E2}");
                    consecutive = cycles >= 3 && residual < cylinder.Solver.PeriodicTolerance ? consecutive + 1 : 0;
                    if (consecutive == 2) return Finish();
                }
                cycles = cylinder.Solver.MaximumCycles;
                return Incomplete(MultiStatus.NotPeriodic, "El estado conjunto de cilindros y colectores no alcanzó periodicidad.");
            }
            catch (ModelDomainException ex) { return Incomplete(MultiStatus.OutsideDomain, ex.Message); }
            catch (ArithmeticException ex) { return Incomplete(MultiStatus.NumericalFailure, ex.Message); }
        }

        private void StartCycle()
        {
            Array.Copy(state, initial, state.Length);
            samples.Clear();
            events.Clear();
            events.Add(720, []);
            foreach (var chamber in chambers)
            {
                var y = chamber.At(state);
                y.Slice(6, 5).Clear();
                y.Slice(12, 5).Clear();
                y.Slice(19, 2).Clear();
                chamber.Model.Ducts!.StartCycle(y);
                chamber.IntakeTotals(state).Clear();
                chamber.PeakTemperature = chamber.PeakPressure = chamber.Consumed = 0;
                chamber.InitiallyBurning = chamber.Model.ReactionActive;
                chamber.InitiallyEquilibrating = chamber.Model.IsEquilibrating;
                chamber.InitialDuration = chamber.Model.BurnDuration;
                chamber.InitialFuelTarget = chamber.Model.TargetFuel;
                chamber.Trace.Clear();
                Add(chamber, 0, EventKind.Boundary);
                Add(chamber, 180, EventKind.Boundary);
                Add(chamber, 540, EventKind.Boundary);
                Add(chamber, ivo, EventKind.ResetTag);
                Add(chamber, cylinder.IntakeValve.ClosesDeg, EventKind.IntakeClose);
                Add(chamber, cylinder.ExhaustValve.OpensDeg, EventKind.Freeze);
                Add(chamber, cylinder.ExhaustValve.ClosesDeg, EventKind.Boundary);
                Add(chamber, chamber.Model.BurnStart, EventKind.Fire);
                if (chamber.InitiallyBurning)
                    Add(chamber, chamber.Model.BurnStart + chamber.InitialDuration, EventKind.EndBurn);
            }
            if (intake is not null)
            {
                intake.StartCycle(IntakeAt(state));
                exhaust!.StartCycle(ExhaustAt(state));
            }
            Sample(0);
        }

        private void Add(Chamber chamber, double local, EventKind kind, double after = 0)
        {
            double angle = Math.Round(Wrap(local + chamber.Phase), 9);
            if (angle == 0) angle = 720;
            if (angle <= after) return; // The end of a burn can fall in the next global cycle.
            if (!events.TryGetValue(angle, out var list)) events.Add(angle, list = []);
            list.Add(new(chamber, kind));
        }

        private void Integrate(CancellationToken cancellation)
        {
            double angle = 0, step = cylinder.Solver.MaximumStepDeg, nextSample = 1;
            Span<double> extents = stackalloc double[2];
            while (events.Count > 0)
            {
                var (boundary, ending) = events.First();
                events.Remove(boundary);
                foreach (var chamber in chambers)
                {
                    chamber.IntervalStart = chamber.Local(angle);
                    chamber.Model.PumpingInterval = chamber.IntervalStart < 180 - 1e-8 || chamber.IntervalStart >= 540 - 1e-8;
                }
                double start = angle;
                while (angle < boundary - 1e-9)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (accepted + rejected > 2_000_000) throw new ArithmeticException("Se excedió el límite de pasos acoplados.");
                    step = Math.Min(step, boundary - angle);
                    if (Advance(angle - start, ref step, out var invalid))
                    {
                        angle += step;
                        accepted++;
                        step = Math.Min(2 * step, cylinder.Solver.MaximumStepDeg);
                        if (angle >= nextSample && angle < boundary - 1e-9)
                        {
                            Sample(angle);
                            nextSample = Math.Floor(angle) + 1;
                        }
                    }
                    else
                    {
                        rejected++;
                        step *= .5;
                        if (step < 1e-7)
                        {
                            if (invalid is ModelDomainException)
                                throw new ModelDomainException($"{rpm} RPM a {angle:F5}°: {invalid.Message}");
                            throw new ArithmeticException($"Estado irresoluble a {angle:F5}° de cigüeñal. {invalid?.Message}");
                        }
                    }
                }
                angle = boundary;
                foreach (var (chamber, kind) in ending)
                {
                    var y = chamber.At(state);
                    switch (kind)
                    {
                        case EventKind.ResetTag: y[11] = 0; break;
                        case EventKind.IntakeClose: chamber.FreshAtClose = y[11]; break;
                        case EventKind.Fire:
                            chamber.Model.BeginReaction(y);
                            if (chamber.Model.TargetFuel > 0) y[11] = 0;
                            Add(chamber, chamber.Model.BurnStart + chamber.Model.BurnDuration, EventKind.EndBurn, boundary);
                            break;
                        case EventKind.EndBurn: chamber.Consumed += chamber.Model.CommitReaction(y); break;
                        case EventKind.Freeze:
                            extents.Clear();
                            chamber.Model.FreezeProducts(cylinder.ExhaustValve.OpensDeg, y, extents);
                            break;
                    }
                }
                Sample(angle);
                if (angle >= nextSample) nextSample = Math.Floor(angle) + 1;
            }
        }

        private bool Advance(double elapsedAngle, ref double step, out ArithmeticException? invalid)
        {
            invalid = null;
            try
            {
                step = Math.Min(step, .97 * Evaluate(elapsedAngle, state));
                for (int i = 0; i < state.Length; i++) stage[i] = state[i] + step * rate[i];
                if (step > Evaluate(elapsedAngle + step, stage)) return false;
                for (int i = 0; i < state.Length; i++) next[i] = .5 * (state[i] + stage[i] + step * rate[i]);
                if (intake is not null)
                {
                    intake.Validate(IntakeAt(next));
                    exhaust!.Validate(ExhaustAt(next));
                }
                Span<double> species = stackalloc double[gas.SpeciesCount];
                Span<double> peaks = stackalloc double[2 * chambers.Length];
                for (int i = 0; i < chambers.Length; i++)
                {
                    var chamber = chambers[i];
                    double angle = chamber.IntervalStart + elapsedAngle + step;
                    double volume = SliderCrank.At(cylinder.Geometry, angle).VolumeM3;
                    var y = chamber.At(next);
                    chamber.Model.Ducts!.ValidateState(y);
                    double t = chamber.Model.Resolve(angle, volume, y, species);
                    peaks[2 * i] = t;
                    peaks[2 * i + 1] = SpeciesThermodynamics.TotalMass(species) *
                        SpeciesThermodynamics.GasConstant(species) * t / volume;
                }
                for (int i = 0; i < chambers.Length; i++)
                {
                    chambers[i].PeakTemperature = Math.Max(chambers[i].PeakTemperature, peaks[2 * i]);
                    chambers[i].PeakPressure = Math.Max(chambers[i].PeakPressure, peaks[2 * i + 1]);
                }
                (state, next) = (next, state);
                return true;
            }
            catch (ArithmeticException ex) when (ex is NumericalStateException or ModelDomainException)
            {
                invalid = ex;
                return false;
            }
        }

        private double Evaluate(double elapsedAngle, double[] y)
        {
            double step = double.PositiveInfinity;
            if (intake is not null)
            {
                double seconds = Math.Min(intake.Evaluate(IntakeAt(y), IntakeAt(rate)),
                    exhaust!.Evaluate(ExhaustAt(y), ExhaustAt(rate)));
                step = seconds * cylinder.Ducts!.Courant * degreesPerSecond;
            }
            foreach (var chamber in chambers)
            {
                var change = chamber.At(rate);
                var model = chamber.Model;
                model.Evaluate(chamber.IntervalStart + elapsedAngle, chamber.At(y), change, intake?.Junction, exhaust?.Junction);
                step = Math.Min(step, model.CflDegrees);
                double massRate = model.Ducts!.IntakeMassRatePerDegree(change);
                var totals = chamber.IntakeTotals(rate);
                totals[0] = Math.Max(0, massRate);
                totals[1] = massRate;
                if (intake is null) continue;
                intake.ExchangeWithRunner(model.Ducts.OuterFlux(change, true), IntakeAt(rate));
                exhaust!.ExchangeWithRunner(model.Ducts.OuterFlux(change, false), ExhaustAt(rate));
            }
            return step;
        }

        private void Sample(double angle)
        {
            if (!captureTrace) return;
            double gasTorque = 0;
            foreach (var chamber in chambers)
            {
                var sample = chamber.Model.Sample(chamber.Local(angle), chamber.At(state), chamber.Consumed);
                gasTorque += sample.PressurePa * SliderCrank.At(cylinder.Geometry, sample.AngleDeg).VolumeDerivativeM3PerDeg * 180 / Math.PI;
                chamber.Trace.Add(sample with { AngleDeg = angle });
            }
            var a = intake?.Pressures(IntakeAt(state)) ?? (cylinder.Intake.PressurePa, cylinder.Intake.PressurePa);
            var b = exhaust?.Pressures(ExhaustAt(state)) ?? (cylinder.Exhaust.PressurePa, cylinder.Exhaust.PressurePa);
            samples.Add(new(angle, a.Item1, b.Item1, a.Item2, b.Item2, gasTorque));
        }

        private double PeriodicError()
        {
            double maximum = 0;
            foreach (var chamber in chambers)
            {
                var a = chamber.At(initial);
                var b = chamber.At(state);
                double error = Normalized(a[5], b[5], 10);
                for (int i = 0; i < gas.SpeciesCount; i++)
                {
                    int index = ReactiveCylinder.MassIndex(i);
                    error = Math.Max(error, Normalized(a[index], b[index], 1e-7));
                }
                error = Math.Max(error, Normalized(a[11], b[11], 1e-7));
                if (chamber.InitiallyBurning != chamber.Model.ReactionActive) error = Math.Max(error, 1);
                if (chamber.InitiallyEquilibrating != chamber.Model.IsEquilibrating) error = Math.Max(error, 1);
                else if (chamber.InitiallyBurning)
                {
                    error = Math.Max(error, Normalized(chamber.InitialDuration, chamber.Model.BurnDuration, 1));
                    error = Math.Max(error, Normalized(chamber.InitialFuelTarget, chamber.Model.TargetFuel, 1e-7));
                }
                error = Math.Max(error, chamber.Model.Ducts!.PeriodicError(a, b));
                error = Math.Max(error, double.IsNaN(chamber.PreviousWork) ? 1 : Normalized(chamber.PreviousWork, b[6], 10));
                chamber.PreviousWork = b[6];
                chamber.Periodic = error;
                maximum = Math.Max(maximum, error);
            }
            return intake is null ? maximum : Math.Max(maximum, Math.Max(intake.PeriodicError(IntakeAt(initial), IntakeAt(state)),
                exhaust!.PeriodicError(ExhaustAt(initial), ExhaustAt(state))));
        }

        private static double Normalized(double a, double b, double scale) => Math.Abs(a - b) / (scale + Math.Abs(b));

        private long CellEvaluations => chambers.Sum(c => c.Model.Ducts!.CellEvaluations) +
            ((intake?.Cells ?? 0) + (exhaust?.Cells ?? 0)) * (long)chambers[0].Model.Evaluations;

        private MultiPoint Incomplete(MultiStatus status, string detail) => new(rpm, status, detail, residual,
            cycles, clock.Elapsed.TotalMilliseconds, null, [], [], null, accepted, rejected, CellEvaluations);

        private MultiPoint Finish()
        {
            var contributions = new CylinderContribution[chambers.Length];
            double work = 0, friction = 0, fresh = 0, fuel = 0, runnerMomentum = 0;
            double interfaceSpecies = 0, interfaceEnergy = 0;
            Span<double> inventory = stackalloc double[gas.Size];
            Span<double> transported = stackalloc double[gas.SpeciesCount];
            inventory.Clear();
            for (int i = 0; i < chambers.Length; i++)
            {
                var chamber = chambers[i];
                var a = chamber.At(initial);
                var b = chamber.At(state);
                double loss = LossModels.FrictionMep(cylinder.Friction, chamber.PeakPressure,
                    2 * cylinder.Geometry.StrokeM * rpm / 60) * cylinder.Geometry.DisplacementM3;
                var totals = chamber.IntakeTotals(state);
                contributions[i] = new(i + 1, chamber.Phase, b[6], b[7], loss, b[6] * rpm / 120,
                    (b[6] - loss) * rpm / 120, chamber.FreshAtClose, totals[0], totals[1], chamber.Model.SocResidual,
                    chamber.PeakPressure, chamber.PeakTemperature, chamber.Periodic, chamber.Model.ExtrapolatedCorrelation,
                    Array.AsReadOnly(chamber.Trace.ToArray()));
                work += b[6]; friction += loss; fresh += chamber.FreshAtClose; fuel += b[10];
                var ducts = chamber.Model.Ducts!;
                ducts.AccumulateStorageChange(a, b, inventory);
                if (intake is null)
                {
                    var outerA = ducts.OuterFlux(b, true);
                    var outerB = ducts.OuterFlux(b, false);
                    for (int k = 0; k < gas.Size; k++) inventory[k] -= outerA[k] + outerB[k];
                }
                for (int k = 0; k < gas.SpeciesCount; k++)
                {
                    int index = ReactiveCylinder.MassIndex(k);
                    inventory[k] += b[index] - a[index];
                    transported[k] = b[ReactiveCylinder.FlowIndex(k)];
                }
                inventory[gas.Energy] += b[5] - a[5] + b[8] + b[6] - ducts.Heat(b);
                var balance = ducts.Balance(a, b, transported, b[9]);
                runnerMomentum = Math.Max(runnerMomentum, balance.Momentum);
                interfaceSpecies = Math.Max(interfaceSpecies, balance.Species);
                interfaceEnergy = Math.Max(interfaceEnergy, balance.Energy);
            }
            if (intake is not null)
            {
                Span<double> sideBalance = stackalloc double[gas.Size];
                for (int side = 0; side < 2; side++)
                {
                    sideBalance.Clear();
                    bool isIntake = side == 0;
                    if (isIntake) intake.AccumulateResidual(IntakeAt(initial), IntakeAt(state), sideBalance);
                    else exhaust!.AccumulateResidual(ExhaustAt(initial), ExhaustAt(state), sideBalance);
                    for (int k = 0; k < gas.Size; k++) inventory[k] += sideBalance[k];
                    foreach (var chamber in chambers)
                    {
                        var outer = chamber.Model.Ducts!.OuterFlux(chamber.At(state), isIntake);
                        for (int k = 0; k < gas.Size; k++) sideBalance[k] += outer[k];
                    }
                    for (int k = 0; k < gas.SpeciesCount; k++)
                        interfaceSpecies = Math.Max(interfaceSpecies, Math.Abs(sideBalance[k]));
                    interfaceEnergy = Math.Max(interfaceEnergy, Math.Abs(sideBalance[gas.Energy]));
                }
            }
            double mass = 0, maxElement = 0;
            for (int k = 0; k < gas.SpeciesCount; k++) mass += inventory[k];
            for (int element = 0; element < 4; element++)
            {
                double amount = 0;
                for (int k = 0; k < gas.SpeciesCount; k++)
                    amount += inventory[k] * SpeciesThermodynamics.ElementMassFraction(k, element);
                maxElement = Math.Max(maxElement, Math.Abs(amount));
            }
            double brake = work - friction, displacement = config.DisplacementM3;
            double[] air = [0, 21 * SpeciesThermodynamics.MolecularWeight(1), 79 * SpeciesThermodynamics.MolecularWeight(2), 0, 0];
            double rho = cylinder.Intake.PressurePa / (cylinder.Intake.TemperatureK * SpeciesThermodynamics.GasConstant(air));
            return new(rpm, MultiStatus.Converged, null, residual, cycles, clock.Elapsed.TotalMilliseconds,
                new(work * rpm / 120, brake * rpm / 120, work / (4 * Math.PI), brake / (4 * Math.PI),
                    work / displacement, brake / displacement, fresh / (rho * displacement), work, friction, fuel),
                Array.AsReadOnly(contributions), Array.AsReadOnly(samples.ToArray()),
                new(mass, maxElement, inventory[gas.Energy], Math.Abs(inventory[2]), interfaceSpecies, interfaceEnergy, runnerMomentum),
                accepted, rejected, CellEvaluations);
        }
    }
}
