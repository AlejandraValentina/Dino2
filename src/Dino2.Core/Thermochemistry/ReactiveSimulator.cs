using Dino2.Core.GasDynamics;
using System.Diagnostics;
using Dino2.Core.FourStroke;
using Dino2.Core.Numerics;

namespace Dino2.Core.Thermochemistry;

public static class EngineSimulation
{
    public static FourStrokeResult Run(FourStrokeConfiguration c, CancellationToken cancellation = default, IProgress<string>? progress = null) =>
        c.Thermochemistry is null ? FourStrokeSimulator.Run(c, cancellation, progress) : ReactiveSimulator.Run(c, cancellation, progress);
}

public static class ReactiveSimulator
{
    public static FourStrokeResult Run(FourStrokeConfiguration c, CancellationToken cancellation = default, IProgress<string>? progress = null)
    {
        c.Validate();
        if (c.Thermochemistry is null) throw new ArgumentException("El modelo reactivo requiere opciones explícitas.");
        var clock = Stopwatch.StartNew(); var points = new List<FourStrokePoint>();
        foreach (int rpm in c.Speeds.Values())
        {
            progress?.Report($"Mezcla reactiva: {rpm} RPM…");
            points.Add(Simulate(c, rpm, cancellation));
        }
        return new(c, points.AsReadOnly(), clock.Elapsed.TotalMilliseconds) { Model = c.ModelIdentifier };
    }
    public static FourStrokePoint Simulate(FourStrokeConfiguration c, int rpm, CancellationToken cancellation = default)
    {
        c.Validate();
        if (c.Thermochemistry is null || !c.Speeds.Values().Contains(rpm)) throw new ArgumentException("Configuración/RPM reactiva inválida.");
        var clock = Stopwatch.StartNew(); var eq = new ReactiveCylinder(c, rpm);
        OdeFunction derivative = eq.Evaluate;
        int n = eq.Dimension;
        var y = new double[n]; eq.Initialize(y);
        var full = new double[n]; var half = new double[n]; var refined = new double[n]; var workspace = new double[5 * n];
        Span<double> physical = stackalloc double[eq.SpeciesCount];
        Span<double> extents = stackalloc double[2];
        Span<double> ductFlux = stackalloc double[7];
        double[] scales = Enumerable.Range(0, n).Select(i => i is >= 5 and <= 9 ? 10.0 : 1e-7).ToArray();
        int accepted = 0, rejected = 0, consecutive = 0;
        double previousWork = double.NaN, periodic = double.PositiveInfinity;
        double ivo = (c.IntakeValve.OpensDeg + 720) % 720;
        var trace = new List<CycleSample>();
        for (int cycle = 1; cycle <= c.Solver.MaximumCycles; cycle++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (ivo == 0) y[11] = 0;
            var initial = (double[])y.Clone(); double initialTag = y[11];
            Array.Clear(y, 6, 5); Array.Clear(y, 12, 5);
            if (eq.SpeciesCount > 5) Array.Clear(y, 19, 2);
            extents.Clear();
            eq.Ducts?.StartCycle(y);
            trace.Clear(); trace.Add(eq.Sample(0, y, 0));
            double angle = 0, step = c.Solver.MaximumStepDeg, consumed = 0, freshAirRetained = 0;
            double nextSample = .5;
            double resolvedPeakPressure = 0, resolvedPeakTemperature = 0;
            bool measureEveryStep = c.Ducts?.Scheme == DuctScheme.Muscl;
            double burnEnd = double.PositiveInfinity;
            var events = new SortedSet<double>(new[] { 180.0, 360, 540, 720, ivo, c.IntakeValve.ClosesDeg,
                c.ExhaustValve.OpensDeg, c.ExhaustValve.ClosesDeg % 720, eq.BurnStart }.Where(a => a > 0 && a <= 720));
            while (events.Count > 0)
            {
                double boundary = events.Min; events.Remove(boundary);
                while (angle < boundary - 1e-9)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (accepted + rejected > 2_000_000) throw new ArithmeticException($"{rpm} RPM: exceso de pasos reactivos.");
                    step = Math.Min(step, boundary - angle);
                    eq.PumpingInterval = angle < 180 - 1e-9 || angle >= 540 - 1e-9;
                    double error = double.PositiveInfinity;
                    try
                    {
                        if (eq.Ducts is not null)
                        {
                            if (CoupledStep(eq, angle, ref step, y, half, full, refined))
                            {
                                double volume = SliderCrank.At(c.Geometry, angle + step).VolumeM3;
                                double temperature = eq.Resolve(angle + step, volume, refined, physical);
                                eq.Ducts.ValidateState(refined);
                                if (measureEveryStep)
                                {
                                    double pressure = SpeciesThermodynamics.TotalMass(physical) *
                                        SpeciesThermodynamics.GasConstant(physical) * temperature / volume;
                                    resolvedPeakPressure = Math.Max(resolvedPeakPressure, pressure);
                                    resolvedPeakTemperature = Math.Max(resolvedPeakTemperature, temperature);
                                }
                                error = 0;
                            }
                        }
                        else
                        {
                            RungeKutta.Step(derivative, angle, y, step, full, workspace);
                            RungeKutta.Step(derivative, angle, y, step / 2, half, workspace);
                            RungeKutta.Step(derivative, angle + step / 2, half, step / 2, refined, workspace);
                            eq.Resolve(angle + step, SliderCrank.At(c.Geometry, angle + step).VolumeM3, refined, physical);
                            error = 0;
                            for (int i = 0; i < n; i++)
                            {
                                double e = Math.Abs(refined[i] - full[i]) / (15 * c.Solver.RelativeTolerance * (scales[i] + Math.Max(Math.Abs(y[i]), Math.Abs(refined[i]))));
                                error = Math.Max(error, double.IsFinite(e) ? e : double.PositiveInfinity);
                            }
                        }
                    }
                    catch (NumericalStateException) { }
                    if (error <= 1)
                    {
                        Array.Copy(refined, y, n); angle += step; accepted++;
                        if (eq.Ducts is null || angle >= nextSample - 1e-9 || angle >= boundary - 1e-9)
                        {
                            trace.Add(eq.Sample(angle, y, consumed));
                            if (angle >= nextSample - 1e-9) nextSample += .5;
                        }
                    }
                    else rejected++;
                    step = Math.Min(c.Solver.MaximumStepDeg, step * (error == 0 ? 2 : Math.Clamp(.9 * Math.Pow(1 / error, .2), .2, 2)));
                    if (step < 1e-7 && boundary - angle > 1e-8)
                        throw new ArithmeticException($"{rpm} RPM a {angle:F5}°: estado reactivo irresoluble; no se recortan especies ni temperatura.");
                }
                angle = boundary;
                if (Math.Abs(boundary - c.IntakeValve.ClosesDeg) < 1e-8) freshAirRetained = y[11];
                if (boundary == eq.BurnStart)
                {
                    eq.BeginReaction(y);
                    burnEnd = eq.BurnStart + eq.BurnDuration;
                    events.Add(burnEnd);
                    if (eq.Ducts is not null && eq.TargetFuel > 0) y[11] = 0; // End of fresh-air tagging for this firing.
                }
                if (boundary == burnEnd) consumed += eq.CommitReaction(y);
                if (boundary == c.ExhaustValve.OpensDeg) eq.FreezeProducts(boundary, y, extents);
                if (boundary == ivo || (boundary == 720 && ivo == 0)) y[11] = 0;
            }
            periodic = Math.Abs(y[11] - initialTag) / (1e-7 + Math.Abs(y[11]));
            for (int i = 0; i < 6; i++) periodic = Math.Max(periodic, Math.Abs(y[i] - initial[i]) / (scales[i] + Math.Abs(y[i])));
            for (int i = 5; i < eq.SpeciesCount; i++)
            {
                int index = ReactiveCylinder.MassIndex(i);
                periodic = Math.Max(periodic, Math.Abs(y[index] - initial[index]) / (scales[index] + Math.Abs(y[index])));
            }
            periodic = Math.Max(periodic, double.IsNaN(previousWork) ? 1 : Math.Abs(y[6] - previousWork) / (10 + Math.Abs(y[6])));
            double ductPeriodicity = eq.Ducts?.PeriodicError(initial, y) ?? 0;
            periodic = Math.Max(periodic, ductPeriodicity);
            consecutive = cycle >= 3 && periodic < c.Solver.PeriodicTolerance ? consecutive + 1 : 0;
            previousWork = y[6]; if (consecutive < 2) continue;
            double[] speciesResiduals = new double[eq.SpeciesCount];
            for (int i = 0; i < eq.SpeciesCount; i++)
            {
                int index = ReactiveCylinder.MassIndex(i);
                double changeMoles = i switch
                {
                    1 => .5 * (extents[0] + extents[1]),
                    3 => -extents[0], 4 => -extents[1], 5 => extents[0], 6 => extents[1], _ => 0
                };
                speciesResiduals[i] = y[index] - initial[index] - y[ReactiveCylinder.FlowIndex(i)] -
                    consumed * SpeciesThermodynamics.ReactionMassRatio(i) - changeMoles * SpeciesThermodynamics.MolecularWeight(i);
            }
            double maxElement = 0;
            for (int element = 0; element < 4; element++)
            {
                double residual = 0;
                for (int i = 0; i < eq.SpeciesCount; i++) residual += speciesResiduals[i] * SpeciesThermodynamics.ElementMassFraction(i, element);
                maxElement = Math.Max(maxElement, Math.Abs(residual));
            }
            DuctDiagnostics? ductDiagnostics = null;
            if (eq.Ducts is not null)
            {
                for (int i = 0; i < eq.SpeciesCount; i++) ductFlux[i] = y[ReactiveCylinder.FlowIndex(i)];
                var balance = eq.Ducts.Balance(initial, y, ductFlux, y[9]);
                ductDiagnostics = new(balance.Mass, balance.Species, balance.Energy, ductPeriodicity, eq.Ducts.CellEvaluations, balance.Momentum);
            }
            double peakP = Math.Max(resolvedPeakPressure, trace.Max(s => s.PressurePa));
            double peakT = Math.Max(resolvedPeakTemperature, trace.Max(s => s.TemperatureK));
            double friction = LossModels.FrictionMep(c.Friction, peakP, 2 * c.Geometry.StrokeM * rpm / 60) * c.Geometry.DisplacementM3;
            double brake = y[6] - friction, fuelEnergy = y[10] * SpeciesThermodynamics.LowerHeatingValue;
            double[] air = [0, 21 * SpeciesThermodynamics.MolecularWeight(1), 79 * SpeciesThermodynamics.MolecularWeight(2), 0, 0];
            double rhoAir = c.Intake.PressurePa / (SpeciesThermodynamics.GasConstant(air) * c.Intake.TemperatureK);
            double Phase(double fraction) => eq.BurnStart + eq.BurnDuration * ConditionedBurn.PhaseAtFraction(fraction, c.Combustion.WiebeExponent);
            return new(rpm, y[6] * rpm / 120, brake * rpm / 120, y[6] / (4 * Math.PI), brake / (4 * Math.PI), y[6] - y[7], y[7], y[6], friction,
                brake / c.Geometry.DisplacementM3, freshAirRetained / (rhoAir * c.Geometry.DisplacementM3),
                fuelEnergy > 0 ? y[6] / fuelEnergy : double.NaN, fuelEnergy > 0 ? brake / fuelEnergy : double.NaN,
                y[10], peakP, peakT, consumed * SpeciesThermodynamics.LowerHeatingValue, y[8], y[9], speciesResiduals.Sum(),
                y[5] - initial[5] + y[8] - y[9] + y[6], periodic, cycle, accepted, rejected, eq.Evaluations,
                clock.Elapsed.TotalMilliseconds, Array.AsReadOnly(trace.ToArray()))
            {
                GasDynamics = ductDiagnostics,
                Chemistry = new(speciesResiduals.Max(Math.Abs), maxElement, consumed, eq.BurnDuration, eq.SocT, eq.SocP, eq.SocResidual,
                    Phase(.1), Phase(.5), Phase(.9), eq.ExtrapolatedCorrelation,
                    c.Thermochemistry.UsesEquilibrium
                    ? "Equilibrio UV reducido CO/H2 de inicio de combustión a apertura de escape; composición transportada fuera de ese intervalo. Sin radicales, NO ni cinética. Pérdidas requieren evidencia propia."
                    : "Productos congelados, sin disociación; iso-octano vaporizado; Wiebe prescrita o escalada, sin propagación de llama. Parámetros de pérdidas requieren evidencia propia.")
            };
        }
        throw new ArithmeticException($"{rpm} RPM: no convergió la composición y energía en {c.Solver.MaximumCycles} ciclos (residual {periodic:E3}).");
    }
    // Two FV stages use exactly the same valve flux for both subsystems.
    private static bool CoupledStep(ReactiveCylinder cylinder, double angle, ref double step,
        double[] state, double[] stage, double[] rate, double[] destination)
    {
        cylinder.Evaluate(angle, state, rate);
        step = Math.Min(step, .97 * cylinder.CflDegrees);
        for (int i = 0; i < state.Length; i++) stage[i] = state[i] + step * rate[i];
        cylinder.Evaluate(angle + step, stage, rate);
        if (step > cylinder.CflDegrees) return false;
        for (int i = 0; i < state.Length; i++)
            destination[i] = .5 * (state[i] + stage[i] + step * rate[i]);
        return true;
    }
}
