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
        Directory.CreateDirectory("artifacts/stage4");
        File.WriteAllText("artifacts/stage4/standalone.json", JsonSerializer.Serialize(new { sod = errors, reflection, temporal = new { coarse, fine } },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private delegate void InitialState(double x, Span<double> q);

    private static (double[] State, double Conservation) Evolve(DuctGeometry geometry, double end, double courant, InitialState initial)
    {
        var mesh = new DuctMesh(geometry, Gas);
        var y = new double[mesh.Size];
        var stage = new double[mesh.Size];
        var derivative = new double[mesh.Size];
        double Integral(int k) => Enumerable.Range(0, mesh.Cells).Sum(i => y[i * Gas.Size + k] * mesh.Volume(i));
        for (int i = 0; i < mesh.Cells; i++)
            initial((i + .5) * mesh.Dx, y.AsSpan(i * Gas.Size, Gas.Size));
        double[] totals = [Integral(0), Integral(Gas.Energy), Integral(Gas.Tag)];

        double Rates(double[] q)
        {
            Span<double> left = stackalloc double[Gas.Size];
            Span<double> right = stackalloc double[Gas.Size];
            left.Clear(); right.Clear();
            left[Gas.Momentum] = mesh.ReservoirArea * Gas.WallPressure(Gas.Decode(q.AsSpan(0, Gas.Size)), false);
            right[Gas.Momentum] = mesh.ValveArea * Gas.WallPressure(Gas.Decode(q.AsSpan(^Gas.Size)), true);
            return mesh.Rates(q, derivative, left, right);
        }

        double time = 0;
        while (time < end)
        {
            double dt = Math.Min(end - time, courant * Rates(y));
            for (int i = 0; i < y.Length; i++) stage[i] = y[i] + dt * derivative[i];
            Rates(stage);
            for (int i = 0; i < y.Length; i++) y[i] = .5 * (y[i] + stage[i] + dt * derivative[i]);
            time += dt;
        }

        double balance = new[] { Integral(0) - totals[0], (Integral(Gas.Energy) - totals[1]) / totals[1], Integral(Gas.Tag) - totals[2] }.Max(Math.Abs);
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
