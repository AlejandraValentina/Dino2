using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Multicylinder;
using Dino2.Core.Thermochemistry;
using Dino2.Core.TwoStroke;

internal static class CouplingChecks
{
    private static readonly GasEquation Air = new(1);
    private static readonly double[] Pure = [1];
    private const double Pressure = 100000, Temperature = 300, Gamma = 1.4, R = 287;

    internal static void Run()
    {
        Aperture();
        AperturePulse();
        foreach (double volume in new[] { .0001, .0005, .0024, .01, .03 })
            Acoustic(volume, .036, .060, 1);
        Acoustic(.0005, .051, .040, .8);
        Serialization();
    }

    private static void Serialization()
    {
        var fresh = new GasPath(new(.35, .04, .04, 12), new(.5, .04, .04, 12));
        var fresh2T = new TwoStrokeNumerics(.5, .44, 5e-5, 140, DuctScheme.Muscl);
        if (fresh.Exterior != ExteriorBoundary.PressureReservoir || fresh2T.Exterior != ExteriorBoundary.PressureReservoir)
            throw new Exception("Los modelos nuevos deben emplear presión.");
        void Check<T>(T input, Func<T, ExteriorBoundary> get, Func<T, string?> notice)
        {
            var json = JsonSerializer.SerializeToNode(input)!.AsObject();
            json.Remove("Exterior");
            var historical = json.Deserialize<T>()!;
            if (get(historical) != ExteriorBoundary.UnspecifiedLegacy || notice(historical)?.Contains("ausente") != true)
                throw new Exception("El proyecto antiguo perdió la condición histórica implícita.");
            json["Exterior"] = 0;
            if (get(json.Deserialize<T>()!) != ExteriorBoundary.HistoricalRiemann)
                throw new Exception("El formato numérico histórico cambió de significado.");
            json["Exterior"] = 1;
            if (get(json.Deserialize<T>()!) != ExteriorBoundary.PressureReservoir)
                throw new Exception("El formato numérico físico cambió de significado.");
        }
        Check(fresh, p => p.Exterior, p => p.BoundaryNotice());
        Check(fresh2T, p => p.Exterior, p => p.BoundaryNotice());
    }

    private static void Aperture()
    {
        Span<double> reservoir = stackalloc double[Air.Size], edge = stackalloc double[Air.Size];
        Span<double> flux = stackalloc double[Air.Size];
        var tank = Air.Encode(reservoir, Pure, Pressure, Temperature, 0, .7);
        const double open = .0003, total = .001;
        double critical = Pressure * Math.Pow(2 / (Gamma + 1), Gamma / (Gamma - 1));
        double expected = open * Pressure / Math.Sqrt(Temperature) * Math.Sqrt(Gamma / R) *
            Math.Pow((Gamma + 1) / 2, -(Gamma + 1) / (2 * (Gamma - 1)));
        foreach (double downstream in new[] { 5000.0, 10000.0 })
        {
            var pipe = Air.Encode(edge, Pure, downstream, Temperature, 0, .1);
            double p = PortBoundary.Flux(Air, total, open, reservoir, tank, edge, pipe, flux, out var trace);
            if (trace is not { } choked || !(p < critical)) throw new Exception("Garganta y sección mezclada no se distinguen.");
            Near(choked.ThroatPressurePa / critical, 1, 1e-5);
            Near(choked.ThroatMach, 1, 1e-12);
            Near(-flux[0] / expected, 1, 1e-5);
            Near(flux[Air.Energy] / flux[0], Gamma * R * Temperature / (Gamma - 1), .01);
            Near(flux[Air.Tag] / flux[0], .7, 1e-12);
            PortBoundary.Flux(Air, total, 0, reservoir, tank, edge, pipe, flux);
            Near(flux[Air.Momentum], total * downstream, 1e-12);
            Near(flux[0], 0, 1e-12);
            Near(flux[Air.Energy], 0, 1e-12);
        }
        // A prescribed isentropic trace at Mach 0.2 must pass a uniform subsonic jet.
        const double mach = .2;
        double factor = 1 + (Gamma - 1) / 2 * mach * mach;
        double ts = Temperature / factor, ps = Pressure / Math.Pow(factor, Gamma / (Gamma - 1));
        double speed = mach * Math.Sqrt(Gamma * R * ts);
        var subsonic = Air.Encode(edge, Pure, ps, ts, -speed, .7);
        Near(PortBoundary.Flux(Air, total, total, reservoir, tank, edge, subsonic, flux) / ps, 1, 1e-5);
        Near(flux[0] / (-total * speed * ps / (R * ts)), 1, 1e-5);

        // A uniform mixed section from independent subsonic momentum and energy balances.
        double j = open / total * speed * ps / (R * ts);
        double momentum = ps + j * speed;
        double k = (Gamma + 1) / (2 * Gamma);
        double mixedSpeed = (momentum - Math.Sqrt(momentum * momentum - 4 * k * j * j * R * Temperature)) / (2 * k * j);
        double mixedPressure = momentum - j * mixedSpeed;
        double mixedT = Temperature - mixedSpeed * mixedSpeed / (2 * Gamma * R / (Gamma - 1));
        var mixed = Air.Encode(edge, Pure, mixedPressure, mixedT, -mixedSpeed, .1);
        Near(PortBoundary.Flux(Air, total, open, reservoir, tank, edge, mixed, flux, out var partial) / mixedPressure, 1, 1e-6);
        Near(flux[0] / (-total * j), 1, 1e-6);
        Near(partial!.Value.ThroatMach, mach, 1e-6);
        double shoulderForce = (total - open) * ps;
        Near(flux[Air.Momentum] / (open * ps + total * j * speed + shoulderForce), 1, 1e-6);
        double mixedStagnation = mixedPressure * Math.Pow(Temperature / mixedT, Gamma / (Gamma - 1));
        if (!(mixedStagnation < Pressure)) throw new Exception("La expansión brusca debe perder presión total.");

        var still = Air.Encode(edge, Pure, Pressure, Temperature, 0, .1);
        foreach (double difference in new[] { -500.0, 0, 500.0 })
        {
            var supply = Air.Encode(reservoir, Pure, Pressure + difference, Temperature, 0, .7);
            double last = 0;
            foreach (double fraction in new[] { 0, .001, .01, .1, .3, .6, .99, 1 })
            {
                double pressure = PortBoundary.Flux(Air, total, total * fraction, reservoir, supply, edge, still, flux);
                if (!(pressure > 0) || Math.Sign(flux[0]) != (fraction == 0 ? 0 : -Math.Sign(difference)))
                    throw new Exception("Sentido de flujo incorrecto al abrir la válvula.");
                // The preserved full-open solve brackets velocity separately. Its
                // last bit can differ when the incremental area loss is negligible.
                if (Math.Abs(flux[0]) + 1e-9 < last)
                    throw new Exception($"Caudal decreciente durante la apertura: Δp={difference}, α={fraction}, último={last:R}, nuevo={Math.Abs(flux[0]):R}.");
                last = Math.Abs(flux[0]);
            }
        }

        // M=2 normal shock. For p_back below the stationary shock pressure the trace
        // remains supersonic. Above it, an upstream-moving shock reaches the pipe.
        double a = Math.Sqrt(Gamma * R * Temperature);
        var inflow = Air.Encode(edge, Pure, Pressure, Temperature, 2 * a, .2);
        double shockPressureRatio = 1 + 2 * Gamma / (Gamma + 1) * (4 - 1);
        var lowBack = Air.Encode(reservoir, Pure, Pressure * shockPressureRatio * .95, Temperature, 0, 0);
        Near(PortBoundary.Flux(Air, total, total, reservoir, lowBack, edge, inflow, flux), Pressure, 1e-6);
        Near(flux[0], total * Pressure / (R * Temperature) * 2 * a, 1e-12);
        Near(flux[Air.Tag] / flux[0], .2, 1e-12);
        double ratio = shockPressureRatio * 1.05;
        var highBack = Air.Encode(reservoir, Pure, Pressure * ratio, Temperature, 0, 0);
        Near(PortBoundary.Flux(Air, total, total, reservoir, highBack, edge, inflow, flux) / (Pressure * ratio), 1, 1e-12);
        double q = (Gamma - 1) / (Gamma + 1);
        double jump = (ratio + q) / (1 + q * ratio);
        double shockSpeed = 2 * a - a * Math.Sqrt(1 + (ratio - 1) * (Gamma + 1) / (2 * Gamma));
        double expectedVelocity = shockSpeed + (2 * a - shockSpeed) / jump;
        Near(flux[0] / (total * Pressure / (R * Temperature) * jump * expectedVelocity), 1, 1e-12);
        lowBack = Air.Encode(reservoir, Pure, Pressure * shockPressureRatio * .95, Temperature, 0, 0);
        PortBoundary.Flux(Air, total, open, reservoir, lowBack, edge, inflow, flux, out var restricted);
        if (restricted is not { } shocked || shocked.MixedPressurePa < Pressure * shockPressureRatio)
            throw new Exception("La restricción debe enviar un choque aguas arriba.");
        PortBoundary.Flux(Air, total, .99 * total, reservoir, lowBack, edge, inflow, flux, out var loose);
        Near(loose!.Value.MixedPressurePa / Pressure, 1, 1e-12);
        double AreaRatio(double m) => Math.Pow(2 / (Gamma + 1) * (1 + (Gamma - 1) * m * m / 2),
            (Gamma + 1) / (2 * (Gamma - 1))) / m;
        if (!(loose.Value.ThroatMach > 1)) throw new Exception("Garganta supersónica mal identificada.");
        Near(AreaRatio(loose.Value.ThroatMach) / AreaRatio(2), .99, 1e-6);
        var lowMass = Air.Encode(edge, Pure, Pressure, 199, 1, 0);
        Near(lowMass.Temperature, 199, 1e-10);
        try { Air.ValidateMixture(edge); }
        catch (ArithmeticException) { return; }
        throw new Exception("Térmica inadmisible aceptada.");
    }

    private static void AperturePulse()
    {
        const double length = .4, center = .125, probe = .25, sigma = .025;
        const int cells = 320;
        double c = Math.Sqrt(Gamma * R * Temperature), rho = Pressure / (R * Temperature);
        double arrival = (2 * length - probe - center) / c;
        var mesh = new DuctMesh(new(length, .04, .04, cells), Air, DuctScheme.Muscl);
        var tank = new double[Air.Size];
        var tankState = Air.Encode(tank, Pure, Pressure, Temperature, 0, 0);
        foreach (double amplitude in new[] { -25.0, 25.0 })
        foreach (double aperture in new[] { 0.0, .3, 1.0 })
        {
            var y = new double[mesh.Size]; var stage = new double[y.Length];
            var k1 = new double[y.Length]; var k2 = new double[y.Length];
            var left = new double[Air.Size]; var right = new double[Air.Size]; var edge = new double[Air.Size];
            for (int i = 0; i < cells; i++)
            {
                double x = (i + .5) * mesh.Dx;
                double p = amplitude * Math.Exp(-Math.Pow((x - center) / sigma, 2));
                Air.Encode(y.AsSpan(i * Air.Size, Air.Size), Pure, Pressure + p,
                    Temperature * Math.Pow(1 + p / Pressure, (Gamma - 1) / Gamma), p / (rho * c), 0);
            }
            double Rate(double[] input, double[] derivative)
            {
                mesh.Prepare(input);
                var far = mesh.BoundaryState(input, false, edge);
                ReservoirBoundary.Flux(Air, ExteriorBoundary.PressureReservoir, tank, tankState, edge, far, left);
                for (int k = 0; k < Air.Size; k++) left[k] *= mesh.ReservoirArea;
                var near = mesh.BoundaryState(input, true, edge);
                PortBoundary.Flux(Air, mesh.ValveArea, aperture * mesh.ValveArea, tank, tankState, edge, near, right);
                return mesh.Rates(input, derivative, left, right);
            }
            double time = 0, reflected = 0;
            while (time < arrival + 3 * sigma / c)
            {
                double dt = .35 * Rate(y, k1);
                for (int i = 0; i < y.Length; i++) stage[i] = y[i] + dt * k1[i];
                Rate(stage, k2);
                for (int i = 0; i < y.Length; i++) y[i] += .5 * dt * (k1[i] + k2[i]);
                time += dt;
                if (time < arrival - 3 * sigma / c) continue;
                double pressure = Air.Decode(y.AsSpan((int)(probe / mesh.Dx) * Air.Size, Air.Size)).Pressure - Pressure;
                if (Math.Abs(pressure) > Math.Abs(reflected)) reflected = pressure;
            }
            double reflection = reflected / amplitude;
            Near(reflection, aperture == 0 ? 1 : -1, .10);
            Console.WriteLine(FormattableString.Invariant($"APERTURE PULSE Δp={amplitude} Pa α={aperture} R={reflection:F5}"));
        }
    }

    private static void Acoustic(double volume, double runnerD, double trunkD, double length)
    {
        const double amplitude = 12, sigma = .04;
        double c = Math.Sqrt(Gamma * R * Temperature), rho = Pressure / (R * Temperature);
        var runner = new DuctMesh(new(length, runnerD, runnerD, 400), Air, DuctScheme.Muscl);
        var trunk = new DuctMesh(new(.3, trunkD, trunkD, 120), Air, DuctScheme.Muscl);
        var manifold = new ManifoldVolume(trunk, volume, Air, new(Pressure, Temperature),
            Pure, 0, runner.ReservoirArea, 1, 1, ExteriorBoundary.PressureReservoir);
        int offset = manifold.Dimension;
        var y = new double[offset + runner.Size];
        var stage = new double[y.Length];
        var dy = new double[y.Length];
        var k1 = new double[y.Length];
        manifold.Initialize(y);
        double initialCenter = length / 2, arrival = initialCenter / c;
        double Pulse(double distance) => amplitude * Math.Exp(-Math.Pow(distance / sigma, 2));
        for (int i = 0; i < runner.Cells; i++)
        {
            double dp = Pulse((i + .5) * runner.Dx - initialCenter);
            Air.Encode(y.AsSpan(offset + i * Air.Size, Air.Size), Pure, Pressure + dp,
                Temperature * Math.Pow(1 + dp / Pressure, (Gamma - 1) / Gamma), -dp / (rho * c), 0);
        }
        double stop = arrival + .28 / c;
        double lambda = c * (runner.ReservoirArea + trunk.ValveArea) / volume;
        double source = c * runner.ReservoirArea / volume;
        double Incoming(double t) => Pulse(c * (t - arrival));
        // Solve the linearized plenum compliance by independent convolution. The emitted
        // trunk wave has not yet returned to the plenum during this observation window.
        double LateralPressure(double t)
        {
            if (t <= 0) return 0;
            double dq = t / 750;
            double sum = 0;
            for (int i = 0; i < 750; i++)
            {
                double s = (i + .5) * dq;
                sum += Math.Exp(-lambda * (t - s)) * Incoming(s);
            }
            return 2 * source * sum * dq;
        }
        double Mass(double[] state) => state[0] * volume +
            Enumerable.Range(0, trunk.Cells).Sum(i => state[Air.Size * (i + 1)] * trunk.Volume(i)) +
            Enumerable.Range(0, runner.Cells).Sum(i => state[offset + Air.Size * i] * runner.Volume(i));
        double Energy(double[] state) => state[Air.Energy] * volume +
            Enumerable.Range(0, trunk.Cells).Sum(i => state[Air.Size * (i + 1) + Air.Energy] * trunk.Volume(i)) +
            Enumerable.Range(0, runner.Cells).Sum(i => state[offset + Air.Size * i + Air.Energy] * runner.Volume(i));
        double initialMass = Mass(y), initialEnergy = Energy(y);
        for (double time = 0; time < stop;)
        {
            double dt = Math.Min(stop - time, .35 * Rates(y, k1));
            for (int i = 0; i < y.Length; i++) stage[i] = y[i] + dt * k1[i];
            Rates(stage, dy);
            for (int i = 0; i < y.Length; i++) y[i] += .5 * dt * (k1[i] + dy[i]);
            time += dt;
        }
        double peak = 0, reflected = 0, transmitted = 0;
        for (int i = 0; i < runner.Cells; i++)
        {
            double x = (i + .5) * runner.Dx;
            double emitted = stop - x / c;
            double observed = Air.Decode(y.AsSpan(offset + i * Air.Size, Air.Size)).Pressure - Pressure;
            double expected = Incoming(stop + x / c) + LateralPressure(emitted) - Incoming(emitted);
            peak = Math.Min(peak, observed);
            reflected = Math.Max(reflected, Math.Abs(observed - expected));
        }
        for (int i = 0; i < trunk.Cells; i++)
        {
            double x = (i + .5) * trunk.Dx;
            double observed = Air.Decode(y.AsSpan(Air.Size * (i + 1), Air.Size)).Pressure - Pressure;
            double expected = LateralPressure(stop - (.3 - x) / c) - LateralPressure(stop - (.3 + x) / c);
            transmitted = Math.Max(transmitted, Math.Abs(observed - expected));
        }
        int outerIntegral = Air.Size + trunk.Size;
        Near(Mass(y) - initialMass, y[outerIntegral], 2e-14);
        Near(Energy(y) - initialEnergy, y[outerIntegral + Air.Energy], 2e-8);
        Console.WriteLine(FormattableString.Invariant($"PLENUM V={volume * 1000:G3} L · Rmin={peak / amplitude:F4} · L∞/A ramal={reflected / amplitude:F4}, tronco={transmitted / amplitude:F4}"));
        Near(reflected / amplitude, 0, .085);
        Near(transmitted / amplitude, 0, .065);

        double Rates(double[] state, double[] rate)
        {
            double manifoldStep = manifold.Evaluate(state.AsSpan(0, offset), rate.AsSpan(0, offset));
            var cells = state.AsSpan(offset);
            runner.Prepare(cells);
            Span<double> edge = stackalloc double[Air.Size], left = stackalloc double[Air.Size], right = stackalloc double[Air.Size];
            var inlet = runner.BoundaryState(cells, false, edge);
            manifold.Junction.ToRunner(Air, edge, inlet, left);
            for (int i = 0; i < left.Length; i++) left[i] *= runner.ReservoirArea;
            manifold.ExchangeWithRunner(left, rate.AsSpan(0, offset));
            var wall = runner.BoundaryState(cells, true, edge);
            right.Clear();
            right[Air.Momentum] = runner.ValveArea * Air.WallPressure(wall, true);
            return Math.Min(manifoldStep, runner.Rates(cells, rate.AsSpan(offset), left, right));
        }
    }

    private static void Near(double measured, double expected, double tolerance)
    {
        if (!(Math.Abs(measured - expected) <= tolerance))
            throw new Exception(FormattableString.Invariant($"Medido {measured:R}, referencia {expected:R}, tolerancia {tolerance:R}."));
    }

    internal static void RunCampaign(string selection)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (selection == "checks") { Run(); return; }
        if (selection is "mesh" or "fine" or "temporal")
        {
            var c = TwoStrokeConfiguration.Example;
            c = c with { Numerics = c.Numerics with { Exterior = ExteriorBoundary.PressureReservoir } };
            var rpms = selection == "temporal" ? new[] { 8000, 9500 } :
                new[] { 4000, 5000, 6000, 7000, 8000, 9000, 9500, 10000 };
            var scales = selection switch { "fine" => new[] { 8 }, "temporal" => new[] { 2 }, _ => new[] { 1, 2, 4 } };
            var jobs = from rpm in rpms
                       from scale in scales
                       select (rpm, scale);
            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 3 }, job =>
            {
                var settings = c with { Speeds = new(job.rpm, job.rpm, 1),
                    IntakeDuct = c.IntakeDuct with { Cells = c.IntakeDuct.Cells * job.scale },
                    Expansion = c.Expansion with { Cells = c.Expansion.Cells * job.scale },
                    Numerics = c.Numerics with { MaximumStepDeg = .65 / job.scale,
                        PeriodicTolerance = 5e-5 / job.scale, MaximumCycles = 200 } };
                if (selection == "temporal")
                    settings = settings with { Numerics = settings.Numerics with
                    { Courant = .20, MaximumStepDeg = .1, PeriodicTolerance = 1e-6 } };
                var clock = Stopwatch.StartNew();
                var result = TwoStrokeSimulator.Sweep(settings, false).Runs.Single();
                var p = result.Point;
                Console.WriteLine($"2T {job.rpm} x{job.scale} {result.Status} torque={p?.ShaftTorqueNm:F6} W={p?.ShaftPowerW:F4} TE={p?.TrappingEfficiency:F6} trap={p?.FreshTrappedKg:E5} P={p?.PeakPressurePa:F3} T={p?.PeakTemperatureK:F4} Wc={p?.CylinderWorkJ:F5} Wk={p?.CrankcaseWorkJ:F5} cycles={p?.Cycles} periodic={p?.PeriodicError:E4} U={p?.Balance.EnergyJ:E4} mass={p?.Balance.MassKg:E4} ms={clock.ElapsedMilliseconds} {result.Failure}");
            });
        }
        else if (selection is "four" or "four-strict")
        {
            var baseConfiguration = ThermochemicalOptions.EquilibriumExample;
            var jobs = from length in new[] { .25, .4, .65, .85 }
                       from scale in new[] { 1, 2 }
                       select (length, scale);
            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 2 }, job =>
            {
                var gas = GasPath.RecoveryExample;
                var path = gas with { Intake = gas.Intake with { Cells = 24 * job.scale },
                    Exhaust = gas.Exhaust with { LengthM = job.length,
                        Cells = (int)Math.Round(job.length * gas.Exhaust.Cells / gas.Exhaust.LengthM) * job.scale } };
                var c = baseConfiguration with { Ducts = path, Speeds = new(3000, 6000, 1500),
                    Thermochemistry = baseConfiguration.Thermochemistry! with { BurnLaw = BurnLaw.PrescribedWiebe },
                    Combustion = baseConfiguration.Combustion with { DurationDeg = 40 },
                    Solver = baseConfiguration.Solver with
                    {
                        MaximumCycles = selection == "four" ? 125 : 200,
                        PeriodicTolerance = (selection == "four" ? .0001 : 1e-6) / job.scale,
                        MaximumStepDeg = baseConfiguration.Solver.MaximumStepDeg / job.scale
                    } };
                var result = EngineSimulation.Run(c);
                foreach (var p in result.Points)
                    Console.WriteLine($"4T L={job.length} x{job.scale} {p.Rpm} torque={p.BrakeTorqueNm:F6} W={p.BrakePowerW:F4} VE={p.VolumetricEfficiency:F6} P={p.PeakPressurePa:F3} T={p.PeakTemperatureK:F4} cycles={p.Cycles} periodic={p.PeriodicError:E4} U={p.GasDynamics?.EnergyResidualJ:E4} ms={p.ElapsedMilliseconds:F0}");
                foreach (var failure in result.Failures) Console.WriteLine($"4T L={job.length} x{job.scale} {failure}");
            });
        }
        else if (selection == "networks") Networks();
        else throw new ArgumentException("Campaña de etapa 12: checks, mesh, fine, temporal, four, four-strict o networks.");
    }

    private static void Networks()
    {
        static double Range(IEnumerable<double> signals)
        {
            var s = signals.ToArray();
            return s.Length == 0 ? double.NaN : s.Max() - s.Min();
        }
        var jobs = new List<Action>();
        foreach (int n in new[] { 2, 3, 4 })
        foreach (bool shared in new[] { false, true })
        {
            jobs.Add(() => FourNetwork(n, shared));
            jobs.Add(() => TwoNetwork(n, shared));
        }
        jobs.Add(() => FourNetwork(2, true, true));
        jobs.Add(() => TwoNetwork(2, true, true));
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 3 }, job => job());

        static void FourNetwork(int n, bool shared, bool asymmetric = false)
        {
            var source = MultiConfiguration.Example(n, ExteriorBoundary.PressureReservoir);
            var basis = source.Cylinder;
            if (asymmetric) basis = basis with { Ducts = basis.Ducts! with
                { Intake = basis.Ducts.Intake with { LengthM = .48, Cells = 10 } } };
            var c = source with { Manifolds = shared ? source.Manifolds : null,
                Phases = asymmetric ? new(270) : source.Phases,
                Cylinder = basis with { Speeds = new(4000, 4000, 1), Solver = basis.Solver with { MaximumCycles = 130 } } };
            var p = MultiSimulator.Run(c).Points.Single();
            var signal = Range(p.Cycle.Select(q => q.IntakeJunctionPa));
            Console.WriteLine($"4T {n} shared={shared} caseB={asymmetric} {p.Status} T={p.Metrics?.BrakeTorqueNm:F6} " +
                $"dP={signal:F2} port=[{string.Join(", ", p.Cylinders.Select(v => Range(v.Trace.Select(q => q.IntakePortPressurePa)).ToString("F2")))}] " +
                $"Wp=[{string.Join(", ", p.Cylinders.Select(v => v.NetWorkJ.ToString("F5")))}] " +
                $"species={p.Balance?.MaximumInterfaceSpeciesResidualKg:E3} U={p.Balance?.TotalEnergyJ:E3} ms={p.ElapsedMilliseconds:F0} {p.Detail}");
        }

        static void TwoNetwork(int n, bool shared, bool asymmetric = false)
        {
            var source = MultiTwoStrokeConfiguration.Example(n, ExteriorBoundary.PressureReservoir);
            var basis = source.Cylinder;
            if (asymmetric) basis = basis with { IntakeDuct = basis.IntakeDuct with { LengthM = .18, Cells = 15 } };
            var c = source with { SharedIntake = shared ? source.SharedIntake : null,
                SharedExhaust = shared ? source.SharedExhaust : null, Phases = asymmetric ? new(135) : source.Phases,
                Cylinder = basis with { Speeds = new(5000, 5000, 1) } };
            var run = TwoStrokeSimulator.Sweep(c).Runs.Single();
            var p = run.Point;
            var signal = Range(p?.Cycle.Select(q => q.IntakeJunctionPa ?? 0) ?? []);
            Console.WriteLine($"2T {n} shared={shared} caseB={asymmetric} {run.Status} T={p?.ShaftTorqueNm:F6} " +
                $"dP={signal:F2} port=[{string.Join(", ", p?.Cylinders.Select(v => Range(v.Point.Trace.Select(q => q.IntakeValvePressurePa)).ToString("F2")) ?? [])}] " +
                $"Wp=[{string.Join(", ", p?.Cylinders.Select(v => v.Point.CylinderWorkJ.ToString("F5")) ?? [])}] " +
                $"U={p?.Balance.EnergyJ:E3} ms={run.Milliseconds:F0} {run.Failure}");
        }
    }
}
