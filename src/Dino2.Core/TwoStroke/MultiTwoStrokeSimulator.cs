using System.Diagnostics;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Multicylinder;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.TwoStroke;

public sealed partial class TwoStrokeSimulator
{
    public static MultiTwoStrokeSweep Sweep(MultiTwoStrokeConfiguration configuration, bool trace = true,
        IProgress<MultiTwoStrokeRun>? progress = null, CancellationToken cancellation = default, IProgress<string>? activity = null)
    {
        configuration.Validate();
        var results = new List<MultiTwoStrokeRun>();
        foreach (int rpm in configuration.Cylinder.Speeds.Values())
        {
            cancellation.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            JointCycle? cycle = null;
            MultiTwoStrokeRun run;
            try
            {
                configuration.Cylinder.ValidateOperatingPoint(rpm);
                cycle = new JointCycle(configuration, rpm);
                var point = cycle.Run(trace, cancellation, activity);
                run = new(rpm, TwoStrokeRunStatus.Periodic, point, null, cycle.Diagnostic, timer.Elapsed.TotalMilliseconds);
            }
            catch (ArithmeticException ex)
            {
                var status = ex switch
                {
                    CycleConvergenceException => TwoStrokeRunStatus.NonConverged,
                    ModelDomainException => TwoStrokeRunStatus.OutsideDomain,
                    _ => TwoStrokeRunStatus.NumericalFailure
                };
                run = new(rpm, status, null, ex.Message, cycle?.Diagnostic, timer.Elapsed.TotalMilliseconds);
            }
            results.Add(run);
            progress?.Report(run);
        }
        return new(configuration, results.AsReadOnly()) { Provenance = ResultProvenance.Capture(MultiTwoStrokeConfiguration.ModelId,
            configuration.Cylinder.Revision + "-network-recovery-v1") };
    }

    private sealed class JointCycle
    {
        private sealed class Member(TwoStrokeSimulator cylinder, int offset, double phase)
        {
            internal TwoStrokeSimulator Cylinder { get; } = cylinder;
            internal double Phase { get; } = phase;
            internal Span<double> State(double[] vector) => vector.AsSpan(offset, Cylinder.dimension);
            internal Span<double> Boundary(double[] vector, bool intake) =>
                vector.AsSpan(offset + Cylinder.dimension + (intake ? 0 : Width), Width);
            internal double Local(double angle) => Cylinder.open + FiringPhases.Wrap(angle - Phase, 360);
            internal double SegmentStart, PeakP, PeakT, Trapped, TrappedMass, MarkerRemoved, Burned, Error;
            internal double PreviousWork = double.NaN, InitialFuel;
            internal bool InitiallyBurning;
            internal SpeciesMasses Composition, Residual;
            internal double[] Chemistry { get; } = new double[N];
            internal List<TwoStrokeSample> Trace { get; } = [];
        }

        private readonly MultiTwoStrokeConfiguration configuration;
        private readonly TwoStrokeConfiguration c;
        private readonly int rpm, intakeOffset, exhaustOffset;
        private readonly Member[] members;
        private readonly ManifoldVolume? intake, exhaust;
        private readonly GasEquation gas = new(N);
        private readonly SortedDictionary<double, List<(Member Member, double Local)>> events = new() { [360] = [] };
        private readonly double[] y, before, k1, k2, stage;
        private readonly TwoStrokeSample[] first, second;
        private readonly List<MultiTwoStrokeSample> trace = [];
        private int accepted, rejected, steps, cycle;
        private long cellEvaluations;
        internal TwoStrokeConvergence? Diagnostic { get; private set; }

        internal JointCycle(MultiTwoStrokeConfiguration configuration, int rpm)
        {
            this.configuration = configuration;
            c = configuration.Cylinder;
            this.rpm = rpm;
            double[] phases = configuration.Phases.Values();
            members = new Member[phases.Length];
            int size = 0;
            for (int i = 0; i < phases.Length; i++)
            {
                var cylinder = new TwoStrokeSimulator(c, rpm, exhaustPrimary: configuration.SharedExhaust?.Primary);
                var member = new Member(cylinder, size, phases[i]);
                members[i] = member;
                size += cylinder.dimension + 2 * Width;
                foreach (double local in cylinder.events.Where(e => e > cylinder.open + 1e-9))
                {
                    double angle = Math.Round(FiringPhases.Wrap(local - cylinder.open + member.Phase, 360), 9);
                    if (angle == 0) angle = 360;
                    if (!events.TryGetValue(angle, out var group)) events.Add(angle, group = []);
                    group.Add((member, local));
                }
            }
            double Area(double diameter) => Math.PI * diameter * diameter / 4;
            intakeOffset = size;
            if (configuration.SharedIntake is { } a)
            {
                intake = new(new DuctMesh(a.Trunk, gas, c.Numerics.Scheme, boundedPassiveTag: true), a.VolumeM3,
                    gas, c.Intake, members[0].Cylinder.freshMixture, 1,
                    Area(c.IntakeDuct.ReservoirDiameterM), members.Length, 6.0 * rpm, c.Numerics.Exterior);
                size += intake.Dimension;
            }
            exhaustOffset = size;
            if (configuration.SharedExhaust is { } e)
            {
                exhaust = new(TwoStrokeDuct.ExpansionMesh(c.Expansion, gas, c.Numerics.Scheme), e.JunctionVolumeM3,
                    gas, c.Tail, TwoStrokeDuct.TailMixture(c), 0,
                    Area(e.Primary.ReservoirDiameterM), members.Length, 6.0 * rpm, c.Numerics.Exterior);
                size += exhaust.Dimension;
            }
            y = new double[size]; before = new double[size]; k1 = new double[size];
            k2 = new double[size]; stage = new double[size];
            first = new TwoStrokeSample[members.Length]; second = new TwoStrokeSample[members.Length];
            foreach (var member in members) member.Cylinder.Initialize(member.State(y), member.Local(0));
            intake?.Initialize(Intake(y));
            exhaust?.Initialize(Exhaust(y));
        }

        private Span<double> Intake(double[] vector) => vector.AsSpan(intakeOffset, intake!.Dimension);
        private Span<double> Exhaust(double[] vector) => vector.AsSpan(exhaustOffset, exhaust!.Dimension);

        internal MultiTwoStrokePoint Run(bool capture, CancellationToken cancellation, IProgress<string>? activity)
        {
            int streak = 0;
            double? previous = null;
            for (cycle = 1; cycle <= c.Numerics.MaximumCycles; cycle++)
            {
                cancellation.ThrowIfCancellationRequested();
                StartCycle();
                Integrate(capture && streak == 1, cancellation);
                var (error, dominant) = Periodicity();
                Diagnostic = new(cycle, error, previous, dominant, accepted, rejected);
                if (cycle % 10 == 0) activity?.Report($"{rpm} RPM · ciclo {cycle} · periodicidad {error:E2}");
                previous = error;
                streak = error <= c.Numerics.PeriodicTolerance ? streak + 1 : 0;
                if (streak >= 2) return Finish();
            }
            throw new CycleConvergenceException($"2T conjunto a {rpm} RPM: residual {Diagnostic?.Residual:E3}; {Diagnostic?.Dominant}.");
        }

        private void StartCycle()
        {
            steps = 0;
            trace.Clear();
            foreach (var member in members)
            {
                member.State(y).Slice(Wc, IntakeOffset - Wc).Clear();
                member.Boundary(y, true).Clear();
                member.Boundary(y, false).Clear();
                member.PeakP = member.PeakT = member.MarkerRemoved = member.Burned = 0;
                member.InitiallyBurning = member.Cylinder.burning;
                member.InitialFuel = member.Cylinder.burnFuel;
                Array.Clear(member.Chemistry);
                member.Trace.Clear();
            }
            intake?.StartCycle(Intake(y));
            exhaust?.StartCycle(Exhaust(y));
            y.CopyTo(before, 0);
        }

        private void Integrate(bool capture, CancellationToken token)
        {
            double start = 0, nextSample = 0;
            foreach (var (end, ending) in events)
            {
                foreach (var member in members) member.SegmentStart = member.Local(start);
                double angle = start;
                while (angle < end - 1e-10)
                {
                    if ((steps & 31) == 0) token.ThrowIfCancellationRequested();
                    if (accepted + rejected > 2_000_000) throw new NumericalStateException("Se excedió el límite de pasos acoplados.");
                    double time = Rates(angle - start, y, k1, first);
                    if (capture && angle >= nextSample)
                    {
                        Record(angle, first);
                        nextSample = angle + 1;
                    }

                    double step = Math.Min(end - angle, Math.Min(c.Numerics.MaximumStepDeg, .98 * time));
                    string? lastError = null;
                    while (true)
                    {
                        if (!double.IsFinite(step) || step <= 0 || (step < 1e-7 && step < end - angle))
                            throw new NumericalStateException($"Paso conjunto insuficiente en ciclo {cycle}, {angle:F5}°. {lastError}");
                        for (int i = 0; i < y.Length; i++) stage[i] = y[i] + step * k1[i];
                        try
                        {
                            double limit = Rates(angle + step - start, stage, k2, second);
                            if (step <= limit) break;
                            step = Math.Min(step / 2, .98 * limit);
                        }
                        catch (NumericalStateException ex) { lastError = ex.Message; step *= .5; }
                        rejected++;
                    }
                    for (int i = 0; i < y.Length; i++) y[i] += step * .5 * (k1[i] + k2[i]);
                    for (int i = 0; i < members.Length; i++)
                    {
                        members[i].PeakP = Math.Max(members[i].PeakP, Math.Max(first[i].CylinderPressurePa, second[i].CylinderPressurePa));
                        members[i].PeakT = Math.Max(members[i].PeakT, Math.Max(first[i].CylinderTemperatureK, second[i].CylinderTemperatureK));
                    }
                    angle += step;
                    steps++;
                    accepted++;
                }
                foreach (var (member, local) in ending) ApplyEvent(member, local);
                if (capture && end == 360)
                {
                    foreach (var member in members) member.SegmentStart = member.Local(360);
                    Rates(0, y, k1, first);
                    Record(360, first);
                }
                start = end;
            }
        }

        private double Rates(double elapsed, double[] values, double[] rate, TwoStrokeSample[] samples)
        {
            double limit = double.PositiveInfinity;
            double angularCfl = c.Numerics.Courant * 6 * rpm;
            if (intake is not null) limit = Math.Min(limit, angularCfl * intake.Evaluate(Intake(values), Intake(rate)));
            if (exhaust is not null) limit = Math.Min(limit, angularCfl * exhaust.Evaluate(Exhaust(values), Exhaust(rate)));
            cellEvaluations += (intake?.Cells ?? 0) + (exhaust?.Cells ?? 0);
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                var ir = m.Boundary(rate, true);
                var er = m.Boundary(rate, false);
                limit = Math.Min(limit, m.Cylinder.Evaluate(m.SegmentStart + elapsed, m.State(values), m.State(rate),
                    out samples[i], intake?.Junction, exhaust?.Junction, ir, er));
                intake?.ExchangeWithRunner(ir, Intake(rate));
                exhaust?.ExchangeWithRunner(er, Exhaust(rate));
                cellEvaluations += m.Cylinder.inlet.Cells + m.Cylinder.outlet.Cells;
            }
            return limit;
        }

        private void Record(double angle, TwoStrokeSample[] states)
        {
            double torque = 0;
            for (int i = 0; i < members.Length; i++)
            {
                var s = states[i];
                torque += (s.CylinderPressurePa - s.CrankcasePressurePa) *
                    SliderCrank.At(c.Geometry, s.AngleDeg).VolumeDerivativeM3PerDeg * 180 / Math.PI;
                members[i].Trace.Add(s with { AngleDeg = angle });
            }
            var a = intake?.Pressures(Intake(y));
            var b = exhaust?.Pressures(Exhaust(y));
            trace.Add(new(angle, a?.JunctionPa, b?.JunctionPa, a?.TrunkPa, b?.TrunkPa, torque));
        }

        private void ApplyEvent(Member m, double local)
        {
            var model = m.Cylinder;
            var s = m.State(y);
            Span<double> composition = stackalloc double[N];
            if (local == model.close)
            {
                m.Trapped = SpeciesThermodynamics.TotalMass(s[..N]);
                m.TrappedMass = m.Trapped + SpeciesThermodynamics.TotalMass(s.Slice(Old, N));
                m.Residual = SpeciesMasses.From(s.Slice(Old, N));
                for (int i = 0; i < N; i++) composition[i] = s[i] + s[Old + i];
                m.Composition = SpeciesMasses.From(composition);
            }
            if (local == model.soc)
            {
                m.MarkerRemoved = SpeciesThermodynamics.TotalMass(s[..N]);
                for (int i = 0; i < N; i++)
                {
                    s[Old + i] += s[i];
                    s[i] = 0;
                }
                model.burnFuel = c.Combustion.BurnFraction * Math.Max(0,
                    Math.Min(s[Old], s[Old + 1] / -SpeciesThermodynamics.ReactionMassRatio(1)));
                model.burning = model.burnFuel > 0;
            }
            if (local != model.open + 360 || !model.burning) return;

            double volume = SliderCrank.At(c.Geometry, local).VolumeM3;
            model.CylinderComposition(local, s, volume, composition, out _);
            double co = (composition[5] - s[5] - s[Old + 5]) / SpeciesThermodynamics.MolecularWeight(5);
            double h2 = (composition[6] - s[6] - s[Old + 6]) / SpeciesThermodynamics.MolecularWeight(6);
            for (int i = 0; i < N; i++)
            {
                double nu = i switch { 1 => .5 * (co + h2), 3 => -co, 4 => -h2, 5 => co, 6 => h2, _ => 0 };
                m.Chemistry[i] += model.burnFuel * SpeciesThermodynamics.ReactionMassRatio(i) + nu * SpeciesThermodynamics.MolecularWeight(i);
                s[Old + i] = composition[i] - s[i];
            }
            m.Burned += model.burnFuel;
            model.burning = false;
            model.burnFuel = 0;
        }

        private (double Error, string Dominant) Periodicity()
        {
            double error = 0;
            string dominant = "estado conjunto";
            foreach (var (m, i) in members.Select((m, i) => (m, i)))
            {
                var (residual, field) = m.Cylinder.PeriodicError(m.State(before), m.State(y));
                double work = m.State(y)[Wc] + m.State(y)[Wk];
                double workError = double.IsNaN(m.PreviousWork) ? 1 :
                    Math.Abs(work - m.PreviousWork) / Math.Max(10, Math.Abs(work));
                m.PreviousWork = work;
                if (workError > residual) (residual, field) = (workError, "trabajo");
                double burnError = m.InitiallyBurning != m.Cylinder.burning ? 1 : m.InitiallyBurning
                    ? Math.Abs(m.InitialFuel - m.Cylinder.burnFuel) / (1e-7 + Math.Abs(m.Cylinder.burnFuel)) : 0;
                if (burnError > residual) (residual, field) = (burnError, "fase química");
                m.Error = residual;
                Worst(residual, $"cilindro {i + 1} · {field}");
            }
            if (intake is not null) Worst(intake.PeriodicError(Intake(before), Intake(y)), "colector y tronco de admisión");
            if (exhaust is not null) Worst(exhaust.PeriodicError(Exhaust(before), Exhaust(y)), "colector y expansión común");
            return (error, dominant);
            void Worst(double residual, string label)
            {
                if (residual > error) (error, dominant) = (residual, label);
            }
        }

        private MultiTwoStrokePoint Finish()
        {
            var results = new MultiTwoStrokeCylinder[members.Length];
            Span<double> total = stackalloc double[Width], chemistry = stackalloc double[N], delta = stackalloc double[Width];
            total.Clear(); chemistry.Clear();
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                var balance = Balance(m, delta);
                for (int j = 0; j < Width; j++) total[j] += delta[j];
                for (int j = 0; j < N; j++) chemistry[j] += m.Chemistry[j];
                results[i] = new(i + 1, m.Phase, Summarize(m, balance));
            }

            var intakeError = JunctionResidual(intake, true, total);
            var exhaustError = JunctionResidual(exhaust, false, total);
            var global = MakeBalance(total, chemistry);
            double wsk = results.Sum(r => r.Point.CylinderWorkJ + r.Point.CrankcaseWorkJ);
            double shaftPower = results.Sum(r => r.Point.ShaftPowerW);
            double shaftWork = shaftPower * 60 / rpm;
            double fuel = results.Sum(r => r.Point.FuelMassKg);
            return new(rpm, wsk * rpm / 60, shaftPower, wsk / (2 * Math.PI), shaftWork / (2 * Math.PI),
                wsk / configuration.DisplacementM3, shaftWork / configuration.DisplacementM3,
                shaftPower > 0 && fuel > 0 ? fuel * rpm / 60 * 3.6e9 / shaftPower : null,
                Array.AsReadOnly(results), Array.AsReadOnly(trace.ToArray()), global,
                Math.Max(intakeError.Species, exhaustError.Species), Math.Max(intakeError.Energy, exhaustError.Energy), cellEvaluations);
        }

        private TwoStrokeBalance Balance(Member member, Span<double> delta)
        {
            Span<double> initial = stackalloc double[N], final = stackalloc double[N];
            var a = member.State(before);
            var b = member.State(y);
            double ua = member.Cylinder.Inventory(a, initial, out double ta);
            double ub = member.Cylinder.Inventory(b, final, out double tb);
            delta.Clear();
            for (int i = 0; i < N; i++) delta[i] = final[i] - initial[i] - b[Outer + i];
            delta[gas.Energy] = ub - ua + b[Wc] + b[Wk] + b[Q] - b[OuterH] - b[DuctHeat];
            delta[gas.Tag] = tb - ta + member.MarkerRemoved - b[OuterTag];
            return MakeBalance(delta, member.Chemistry);
        }

        private (double Species, double Energy) JunctionResidual(ManifoldVolume? network, bool inlet, Span<double> total)
        {
            if (network is null) return default;
            Span<double> residual = stackalloc double[Width];
            residual.Clear();
            network.AccumulateResidual(inlet ? Intake(before) : Exhaust(before), inlet ? Intake(y) : Exhaust(y), residual);
            foreach (var member in members)
            {
                var outer = member.Boundary(y, inlet);
                for (int i = 0; i < Width; i++) residual[i] += outer[i];
            }
            double max = 0;
            for (int i = 0; i < Width; i++)
            {
                if (i == gas.Momentum) continue;
                total[i] += residual[i];
                if (i < N) max = Math.Max(max, Math.Abs(residual[i]));
            }
            return (max, Math.Abs(residual[gas.Energy]));
        }

        private TwoStrokeBalance MakeBalance(ReadOnlySpan<double> delta, ReadOnlySpan<double> expected)
        {
            Span<double> elements = stackalloc double[4], species = stackalloc double[N];
            elements.Clear();
            double mass = 0;
            for (int i = 0; i < N; i++)
            {
                mass += delta[i];
                species[i] = delta[i] - expected[i];
                for (int e = 0; e < 4; e++) elements[e] += delta[i] * SpeciesThermodynamics.ElementMassFraction(i, e);
            }
            return new(mass, SpeciesMasses.From(species), elements[0], elements[1], elements[2], elements[3],
                delta[gas.Energy], delta[gas.Tag]);
        }

        private TwoStrokePoint Summarize(Member m, TwoStrokeBalance balance)
        {
            var s = m.State(y);
            double work = s[Wc] + s[Wk], displacement = c.Geometry.DisplacementM3;
            double shaft = work - LossModels.FrictionMep(c.Friction, m.PeakP, m.Cylinder.meanSpeed) * displacement;
            double power = shaft * rpm / 60;
            double reference = displacement * c.Intake.PressurePa /
                (SpeciesThermodynamics.GasConstant(m.Cylinder.freshMixture) * c.Intake.TemperatureK);
            double supplied = s[Delivered] + s[ExhaustReturn];
            return new(rpm, work * rpm / 60, power, shaft / (2 * Math.PI), work / displacement, shaft / displacement,
                s[Wc], s[Wk], s[Q], s[FuelIn], m.Burned, power > 0 && s[FuelIn] > 0 ? s[FuelIn] * rpm / 60 * 3.6e9 / power : null,
                s[Delivered], s[TransferReturn], m.Trapped, s[Short], s[ExhaustReturn], s[Delivered] / reference,
                supplied > 0 ? m.Trapped / supplied : null, m.Trapped / m.TrappedMass, m.Trapped / reference,
                m.TrappedMass - m.Trapped, m.Composition, m.Residual, m.PeakP, m.PeakT, cycle, steps, accepted, rejected,
                m.Error, balance, Array.AsReadOnly(m.Trace.ToArray()));
        }
    }
}
