using System.Text.Json;
using Dino2.Core.GasDynamics;

internal static class DuctChecks
{
    private static readonly GasEquation Gas = new(1);
    private static readonly double[] Pure = [1];

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    internal static void Run()
    {
        var errors = new List<object>();
        double previous = double.PositiveInfinity;
        foreach (int cells in new[] { 50, 100, 200 })
        {
            var geometry = new DuctGeometry(1, .04, .04, cells);
            var (state, conservation) = Evolve(geometry, .2 / Math.Sqrt(1e5), .4, (x, q) =>
            {
                double rho = x < .5 ? 1 : .125, p = x < .5 ? 1e5 : 1e4;
                Gas.Encode(q, Pure, p, p / (287 * rho), 0, x < .5 ? 1 : 0);
            });
            double error = 0;
            for (int i = 0; i < cells; i++)
            {
                double x = ((i + .5) / cells - .5) / .2;
                double exact = SodDensity(x);
                error += Math.Abs(state[i * Gas.Size] - exact) / cells;
            }
            Check(error < previous && error < .055, $"Sod density L1 {error:G6} on {cells} cells");
            Check(conservation < 1e-11, "Closed tube loses mass, energy or tag.");
            errors.Add(new { cells, densityL1 = error, conservation });
            previous = error;
        }

        var resting = new DuctGeometry(1, .03, .06, 64);
        var (rest, restBalance) = Evolve(resting, .005, .45, (_, q) => Gas.Encode(q, Pure, 1e5, 300, 0, .4));
        Check(Enumerable.Range(0, 64).All(i => Math.Abs(rest[i * Gas.Size + Gas.Momentum]) < 1e-10)
            && restBalance < 1e-12, "Area source created motion at rest.");

        double sound = Math.Sqrt(1.4 * 287 * 300);
        const double amplitude = 30;
        var reflection = new List<object>();
        foreach (int cells in new[] { 50, 100, 200 })
        {
            var geometry = new DuctGeometry(1, .04, .04, cells);
            var (state, balance) = Evolve(geometry, 1 / sound, .45,
                (x, q) => Gas.Encode(q, Pure, 1e5 + amplitude * Math.Cos(Math.PI * x),
                    300 * Math.Pow(1 + amplitude / 1e5 * Math.Cos(Math.PI * x), 2.0 / 7), 0, .5));
            double leftPressure = Gas.Decode(state.AsSpan(0, Gas.Size)).Pressure;
            double error = Math.Abs(leftPressure - (1e5 - amplitude * Math.Cos(Math.PI / (2 * cells))));
            Check(leftPressure < 1e5 - 25 && error < 4 && balance < 1e-11, "Rigid-wall reflection or balance failed.");
            reflection.Add(new { cells, leftPressure, error });
        }

        var temporalGeometry = new DuctGeometry(1, .04, .04, 100);
        double Probe(double cfl)
        {
            var (state, _) = Evolve(temporalGeometry, .43 / sound, cfl,
                (x, q) => Gas.Encode(q, Pure, 1e5 + amplitude * Math.Cos(Math.PI * x),
                    300 * Math.Pow(1 + amplitude / 1e5 * Math.Cos(Math.PI * x), 2.0 / 7), 0, .5));
            return Gas.Decode(state.AsSpan(0, Gas.Size)).Pressure;
        }
        double reference = Probe(.025), coarse = Math.Abs(Probe(.45) - reference), fine = Math.Abs(Probe(.225) - reference);
        Check(fine < .4 * coarse, $"Temporal refinement {coarse} → {fine}");
        var nozzle = new List<object>();
        double previousNozzleError = double.PositiveInfinity;
        foreach (int cells in new[] { 24, 48, 96 })
        {
            var geometry = new DuctGeometry(1, .055, .04, cells);
            const double stagnationT = 320, stagnationP = 120000, targetFlow = .06;
            void Exact(double x, Span<double> q)
            {
                double diameter = .055 - .015 * x;
                double area = Math.PI / 4 * diameter * diameter;
                double flowCoefficient = area * stagnationP * Math.Sqrt(1.4 / (287 * stagnationT));
                double low = 0, high = 1;
                for (int i = 0; i < 45; i++)
                {
                    double m = (low + high) / 2;
                    if (flowCoefficient * m / Math.Pow(1 + .2 * m * m, 3) < targetFlow) low = m; else high = m;
                }
                double mach = (low + high) / 2, ratio = 1 + .2 * mach * mach;
                double t = stagnationT / ratio;
                Gas.Encode(q, Pure, stagnationP / Math.Pow(ratio, 3.5), t, mach * Math.Sqrt(1.4 * 287 * t), .4);
            }

            var (state, balance) = Evolve(geometry, .018, .45, Exact, true);
            double rms = 0;
            for (int i = 0; i < cells; i++)
            {
                double x = (i + .5) / cells, d = .055 - .015 * x;
                double flow = state[i * Gas.Size + Gas.Momentum] * Math.PI / 4 * d * d;
                rms += Math.Pow((flow - targetFlow) / targetFlow, 2) / cells;
            }
            double error = Math.Sqrt(rms);
            Check(error < previousNozzleError && error < .06 && balance < 1e-11, $"Variable area flow RMS {error:G6}");
            nozzle.Add(new { cells, rmsRelativeMassFlow = error, balance });
            previousNozzleError = error;
        }
        Directory.CreateDirectory("artifacts/stage4");
        File.WriteAllText("artifacts/stage4/standalone.json", JsonSerializer.Serialize(new { sod = errors, reflection, nozzle, temporal = new { coarse, fine } },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void VerifyMuscl()
    {
        const double pressure = 100000, temperature = 300, peak = 12;
        double density = pressure / (287 * temperature), sound = Math.Sqrt(1.4 * 287 * temperature);
        double Pulse(double x) => peak * Math.Exp(-Math.Pow((x - .25) / .055, 2));
        void Initialize(double x, Span<double> q)
        {
            double pulse = Pulse(x);
            Gas.Encode(q, Pure, pressure + pulse, temperature * Math.Pow(1 + pulse / pressure, 2.0 / 7),
                pulse / (density * sound), .3);
        }

        var comparisons = new List<object>();
        foreach (var scheme in new[] { DuctScheme.FirstOrder, DuctScheme.Muscl })
        {
            double previous = double.PositiveInfinity;
            foreach (int cells in new[] { 50, 100, 200, 256 })
            {
                var geometry = new DuctGeometry(1, .04, .04, cells);
                var (state, balance) = Evolve(geometry, .45 / sound, .35, Initialize, true, scheme);
                var (reflected, closedBalance) = Evolve(geometry, .95 / sound, .35, Initialize, false, scheme);
                double error = 0, reflection = 0, maximum = 0;
                for (int i = 0; i < cells; i++)
                {
                    double x = (i + .5) / cells;
                    double p = Gas.Decode(state.AsSpan(i * Gas.Size, Gas.Size)).Pressure - pressure;
                    double pr = Gas.Decode(reflected.AsSpan(i * Gas.Size, Gas.Size)).Pressure - pressure;
                    error += Math.Abs(p - Pulse(x - .45)) / cells;
                    reflection += Math.Abs(pr - Pulse(2 - x - .95)) / cells;
                    maximum = Math.Max(maximum, p);
                }
                Check(error < previous && balance < 1e-11 && closedBalance < 1e-11, $"Wave refinement {scheme}/{cells}: {error}");
                if (scheme == DuctScheme.Muscl && cells == 200) Check(error < .045 && reflection < .1, "Pulse dissipated excessively.");
                previous = error;
                comparisons.Add(new { scheme = scheme.ToString(), cells, travellingL1Pa = error,
                    reflectedL1Pa = reflection, pulsePeakPa = maximum, balance, closedBalance });
            }
        }

        var (shock, shockBalance) = Evolve(new(1, .04, .04, 160), .00025, .25,
            (x, q) => Gas.Encode(q, Pure, x < .5 ? 1e6 : 1e4, x < .5 ? 1000 : 250, 0, x < .5 ? 1 : 0),
            false, DuctScheme.Muscl);
        var minPressure = Enumerable.Range(0, 160).Min(i => Gas.Decode(shock.AsSpan(i * Gas.Size, Gas.Size)).Pressure);
        Check(minPressure > 0 && shockBalance < 1e-11, "Strong shock loses positivity or conservation.");

        var (rest, restBalance) = Evolve(new(1, .02, .08, 64), .005, .35,
            (_, q) => Gas.Encode(q, Pure, pressure, temperature, 0, .1), false, DuctScheme.Muscl);
        Check(Enumerable.Range(0, 64).All(i => Math.Abs(rest[i * Gas.Size + Gas.Momentum]) < 1e-10) &&
            restBalance < 1e-11, "MUSCL area source loses pressure equilibrium.");

        Directory.CreateDirectory("artifacts/stage4-closure");
        File.WriteAllText("artifacts/stage4-closure/waves.json", JsonSerializer.Serialize(new { comparisons, minPressure, shockBalance },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private delegate void InitialState(double x, Span<double> q);

    private static (double[] State, double Conservation) Evolve(DuctGeometry geometry, double end, double courant, InitialState initial, bool open = false, DuctScheme scheme = DuctScheme.FirstOrder)
    {
        var mesh = new DuctMesh(geometry, Gas, scheme);
        var y = new double[mesh.Size];
        var stage = new double[mesh.Size];
        var derivative = new double[mesh.Size];
        double Integral(int k) => Enumerable.Range(0, mesh.Cells).Sum(i => y[i * Gas.Size + k] * mesh.Volume(i));
        for (int i = 0; i < mesh.Cells; i++)
            initial((i + .5) * mesh.Dx, y.AsSpan(i * Gas.Size, Gas.Size));
        double[] totals = [Integral(0), Integral(Gas.Energy), Integral(Gas.Tag)];

        var leftGhost = new double[Gas.Size];
        var rightGhost = new double[Gas.Size];
        var net = new double[Gas.Size];
        var integrated = new double[Gas.Size];
        initial(0, leftGhost);
        initial(geometry.LengthM, rightGhost);

        double Rates(double[] q)
        {
            Span<double> left = stackalloc double[Gas.Size];
            Span<double> right = stackalloc double[Gas.Size];
            left.Clear(); right.Clear();
            Span<double> a = stackalloc double[Gas.Size];
            Span<double> b = stackalloc double[Gas.Size];
            mesh.Prepare(q);
            var inlet = mesh.BoundaryState(q, false, a);
            var outlet = mesh.BoundaryState(q, true, b);
            if (open)
            {
                Gas.Flux(leftGhost, Gas.Decode(leftGhost), a, inlet, left);
                Gas.Flux(b, outlet, rightGhost, Gas.Decode(rightGhost), right);
                for (int k = 0; k < Gas.Size; k++)
                {
                    left[k] *= mesh.ReservoirArea;
                    right[k] *= mesh.ValveArea;
                }
            }
            else
            {
                left[Gas.Momentum] = mesh.ReservoirArea * Gas.WallPressure(inlet, false);
                right[Gas.Momentum] = mesh.ValveArea * Gas.WallPressure(outlet, true);
            }
            for (int k = 0; k < Gas.Size; k++) net[k] = left[k] - right[k];
            return mesh.Rates(q, derivative, left, right);
        }

        double time = 0;
        while (time < end)
        {
            double dt = Math.Min(end - time, courant * Rates(y));
            for (int i = 0; i < y.Length; i++) stage[i] = y[i] + dt * derivative[i];
            for (int k = 0; k < Gas.Size; k++) integrated[k] += .5 * dt * net[k];
            Rates(stage);
            for (int k = 0; k < Gas.Size; k++) integrated[k] += .5 * dt * net[k];
            for (int i = 0; i < y.Length; i++) y[i] = .5 * (y[i] + stage[i] + dt * derivative[i]);
            time += dt;
        }

        double balance = new[] { Integral(0) - totals[0] - integrated[0], (Integral(Gas.Energy) - totals[1] - integrated[Gas.Energy]) / totals[1], Integral(Gas.Tag) - totals[2] - integrated[Gas.Tag] }.Max(Math.Abs);
        return (y, balance);
    }

    private static double SodDensity(double xi)
    {
        const double pStar = .303130178, uStar = .927452620, gamma = 1.4;
        double aLeft = Math.Sqrt(gamma);
        if (xi <= -aLeft) return 1;
        if (xi < uStar - aLeft * Math.Pow(pStar, 1.0 / 7))
            return Math.Pow(2 / (gamma + 1) - (gamma - 1) / (gamma + 1) * xi / aLeft, 2 / (gamma - 1));
        if (xi < uStar) return Math.Pow(pStar, 1 / gamma);
        double ratio = pStar / .1, beta = (gamma - 1) / (gamma + 1);
        double shock = Math.Sqrt(gamma * .1 / .125) * Math.Sqrt((gamma + 1) / (2 * gamma) * ratio + (gamma - 1) / (2 * gamma));
        return xi < shock ? .125 * (ratio + beta) / (beta * ratio + 1) : .125;
    }
}
