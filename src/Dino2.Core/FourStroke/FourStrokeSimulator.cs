using System.Diagnostics;
using Dino2.Core.Numerics;

namespace Dino2.Core.FourStroke;

public static class FourStrokeSimulator
{
    public static FourStrokeResult Run(FourStrokeConfiguration configuration, CancellationToken cancellation = default,
        IProgress<string>? progress = null)
    {
        configuration.Validate();
        var clock = Stopwatch.StartNew();
        var results = new List<FourStrokePoint>();
        foreach (int rpm in configuration.Speeds.Values())
        {
            progress?.Report($"Simulando {rpm} RPM…");
            results.Add(Simulate(configuration, rpm, cancellation));
        }
        return new(configuration, results.AsReadOnly(), clock.Elapsed.TotalMilliseconds);
    }

    public static FourStrokePoint Simulate(FourStrokeConfiguration c, int rpm, CancellationToken cancellation = default)
    {
        c.Validate();
        if (!c.Speeds.Values().Contains(rpm)) throw new ArgumentException("RPM fuera de la malla configurada.");
        var clock = Stopwatch.StartNew();
        var equations = new CylinderEquations(c, rpm);
        const int n = CylinderEquations.Dimension;
        var y = new double[n];
        var full = new double[n]; var half = new double[n]; var refined = new double[n];
        var workspace = new double[n * 5];
        double volume = SliderCrank.At(c.Geometry, 0).VolumeM3;
        y[0] = c.Exhaust.PressurePa * volume / (AirProperties.R * c.Exhaust.TemperatureK);
        y[1] = y[0] * AirProperties.InternalEnergy(c.Exhaust.TemperatureK);
        double[] scales = [1e-5, 10, 1e-5, 10, 10, 10, 10, 10, 1e-5, 1e-5];
        double ivo = (c.IntakeValve.OpensDeg + 720) % 720;
        double[] boundaries = new[] { 0.0, 180, 360, 540, 720, ivo, c.IntakeValve.ClosesDeg,
            c.ExhaustValve.OpensDeg, c.ExhaustValve.ClosesDeg % 720,
            equations.BurnStartDeg, equations.BurnStartDeg + equations.BurnDurationDeg }
            .Where(a => a > 0 && a <= 720).Distinct().Order().ToArray();
        int accepted = 0, rejected = 0, consecutive = 0;
        double previousWork = double.NaN;
        double periodic = double.PositiveInfinity;
        var trace = new List<CycleSample>();
        for (int cycle = 1; cycle <= c.Solver.MaximumCycles; cycle++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (ivo == 0) y[2] = 0;
            var initial = y[..3];
            Array.Clear(y, 3, n - 3);
            trace.Clear();
            trace.Add(equations.Sample(0, y));
            double angle = 0, step = c.Solver.MaximumStepDeg;
            foreach (double boundary in boundaries)
            {
                while (angle < boundary - 1e-9)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (accepted + rejected > 2_000_000) throw new ArithmeticException($"{rpm} RPM: exceso de pasos numéricos.");
                    step = Math.Min(step, boundary - angle);
                    equations.PumpingInterval = angle < 180 - 1e-9 || angle >= 540 - 1e-9;
                    equations.BurningInterval = angle >= equations.BurnStartDeg - 1e-9 &&
                        angle < equations.BurnStartDeg + equations.BurnDurationDeg - 1e-9;
                    double error = double.PositiveInfinity;
                    try
                    {
                        RungeKutta.Step(equations.Evaluate, angle, y, step, full, workspace);
                        RungeKutta.Step(equations.Evaluate, angle, y, step / 2, half, workspace);
                        RungeKutta.Step(equations.Evaluate, angle + step / 2, half, step / 2, refined, workspace);
                        AirProperties.Temperature(refined[1] / refined[0]);
                        error = 0;
                        for (int i = 0; i < n; i++)
                        {
                            double scale = scales[i] + Math.Max(Math.Abs(y[i]), Math.Abs(refined[i]));
                            double e = Math.Abs(refined[i] - full[i]) / (15 * c.Solver.RelativeTolerance * scale);
                            error = Math.Max(error, double.IsFinite(e) ? e : double.PositiveInfinity);
                        }
                    }
                    catch (NumericalStateException) { /* Retry at a smaller physical-time step, never clip the state. */ }
                    if (error <= 1)
                    {
                        Array.Copy(refined, y, n);
                        angle += step;
                        accepted++;
                        trace.Add(equations.Sample(angle, y));
                    }
                    else rejected++;
                    double factor = error == 0 ? 2 : Math.Clamp(.9 * Math.Pow(1 / error, .2), .2, 2);
                    step = Math.Min(c.Solver.MaximumStepDeg, step * factor);
                    if (step < 1e-7 && boundary - angle > 1e-8)
                        throw new ArithmeticException($"{rpm} RPM, {angle:F6}°: no se pudo resolver el estado. No se publica una curva parcial.");
                }
                angle = boundary;
                if (Math.Abs(boundary - c.IntakeValve.ClosesDeg) < 1e-8)
                    equations.FuelMassKg = y[2] / (c.Combustion.AirFuelRatio + 1);
                if (Math.Abs(boundary - ivo) < 1e-8 || (boundary == 720 && ivo == 0)) y[2] = 0;
            }
            double stateError = 0;
            for (int i = 0; i < 3; i++)
                stateError = Math.Max(stateError, Math.Abs(y[i] - initial[i]) / (scales[i] + Math.Abs(y[i])));
            double workError = double.IsNaN(previousWork) ? 1 : Math.Abs(y[3] - previousWork) / (10 + Math.Abs(y[3]));
            periodic = Math.Max(stateError, workError);
            consecutive = cycle >= 3 && periodic < c.Solver.PeriodicTolerance ? consecutive + 1 : 0;
            previousWork = y[3];
            if (consecutive < 2) continue;
            double peakP = trace.Max(s => s.PressurePa), peakT = trace.Max(s => s.TemperatureK);
            double speed = 2 * c.Geometry.StrokeM * rpm / 60;
            double friction = LossModels.FrictionMep(c.Friction, peakP, speed) * c.Geometry.DisplacementM3;
            double brakeWork = y[3] - friction;
            double fuelEnergy = equations.FuelMassKg * c.Combustion.LowerHeatingValueJPerKg;
            double etaV = equations.FuelMassKg * c.Combustion.AirFuelRatio /
                (c.Intake.PressurePa / (AirProperties.R * c.Intake.TemperatureK) * c.Geometry.DisplacementM3);
            return new(rpm, y[3] * rpm / 120, brakeWork * rpm / 120, y[3] / (4 * Math.PI), brakeWork / (4 * Math.PI),
                y[3] - y[4], y[4], y[3], friction, brakeWork / c.Geometry.DisplacementM3, etaV,
                fuelEnergy > 0 ? y[3] / fuelEnergy : double.NaN, fuelEnergy > 0 ? brakeWork / fuelEnergy : double.NaN,
                equations.FuelMassKg, peakP, peakT, y[7], y[5], y[6],
                y[0] - initial[0] - y[8] + y[9], y[1] - initial[1] - y[7] + y[5] - y[6] + y[3],
                periodic, cycle, accepted, rejected, clock.Elapsed.TotalMilliseconds, Array.AsReadOnly(trace.ToArray()));
        }
        throw new ArithmeticException($"{rpm} RPM: no convergió en {c.Solver.MaximumCycles} ciclos (residual relativo {periodic:E3}).");
    }
}
