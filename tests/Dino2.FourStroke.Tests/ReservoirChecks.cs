using Dino2.Core.GasDynamics;
using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;
using Dino2.Core.TwoStroke;

internal static class ReservoirChecks
{
    private static readonly GasEquation Gas = new(1);
    private static readonly double[] Pure = [1];
    private const double P = 100000, T = 300;

    internal static void Run()
    {
        const double small = 12;
        double sound = Math.Sqrt(1.4 * 287 * T);
        double Pulse(double x, double amplitude) => amplitude * Math.Exp(-Math.Pow((x - .5) / .055, 2));
        (double Min, double Max) Reflection(ExteriorBoundary? mode, double amp)
        {
            var state = Evolve(.85 / sound, (x, q) =>
            {
                double p = P + Pulse(x, amp);
                double t = T * Math.Pow(p / P, 2.0 / 7);
                Gas.Encode(q, Pure, p, t, -2 / .4 * (Math.Sqrt(1.4 * 287 * t) - sound), 0);
            }, mode, null);
            return (state.Min() - P, state.Max() - P);
        }
        var pressure = Reflection(ExteriorBoundary.PressureReservoir, small);
        var matched = Reflection(ExteriorBoundary.HistoricalRiemann, small);
        var closed = Reflection(null, small);
        var strong = Reflection(ExteriorBoundary.PressureReservoir, 20000);
        Console.WriteLine($"REFLECTION matched={matched.Max / small:F5}, pressure={pressure.Min / small:F5}, closed={closed.Max / small:F5}, strong={strong.Min / 20000:F5}");
        Require(pressure.Min / small is > -1.02 and < -.91 && closed.Max / small is > .91 and < 1.02 &&
            Math.Max(Math.Abs(matched.Min), Math.Abs(matched.Max)) < .02 * small && strong.Min < -12000,
            "Reflexión en boca física, pared cerrada y referencia adaptada");

        var nodes = new List<double>();
        double? previous = null;
        Evolve(14 / sound, (x, q) => Gas.Encode(q, Pure, P + small * Math.Sin(Math.PI * x / 2), T, 0, 0),
            ExteriorBoundary.PressureReservoir, (time, sensor) =>
            {
                if (sensor >= P && previous is double before && before < P) nodes.Add(time);
                previous = sensor;
            });
        Require(nodes.Count >= 3, "Modo de cuarto de onda visible");
        double period = (nodes[^1] - nodes[0]) / (nodes.Count - 1);
        Console.WriteLine($"QUARTER measured={period:G8}s analytic={4 / sound:G8}s");
        Require(Math.Abs(period * sound / 4 - 1) < .025, "Período de cuarto de onda");
        FluxCases();
        WallCases();
        EngineCases();
    }

    private static void EngineCases()
    {
        var basis = ThermochemicalOptions.EquilibriumExample;
        var four = basis with
        {
            Thermochemistry = basis.Thermochemistry! with { BurnLaw = BurnLaw.PrescribedWiebe },
            Ducts = GasPath.RecoveryExample, Speeds = new(4500, 4500, 1),
            Solver = basis.Solver with { PeriodicTolerance = .0001, MaximumCycles = 125 }
        };
        var solved = EngineSimulation.Run(four);
        Require(solved.Complete && solved.Model.Contains("pressure-boundary-wall") &&
            Math.Abs(solved.Points[0].GasDynamics!.EnergyResidualJ) < 1e-7, "4T boca más pared, balance integrado");
        var problem = basis with { Ducts = GasPath.Example with { Scheme = DuctScheme.Muscl }, Speeds = new(7500, 9000, 1500) };
        var partial = EngineSimulation.Run(problem);
        Require(partial.Points.Single().Rpm == 7500 &&
            partial.Failures.Single() is { Rpm: 9000, Kind: FourStrokeFailureKind.OutsideDomain }, "F-06 conserva la RPM válida");
        var two = TwoStrokeConfiguration.RecoveryExample;
        var result = TwoStrokeSimulator.Sweep(two with { Speeds = new(5000, 5000, 1),
            Expansion = two.Expansion with { Wall = new(.00004, 500) } }, false);
        Require(result.Complete && Math.Abs(result.Runs[0].Point!.Balance.EnergyJ) < 1e-7, "2T boca más pared, balance integrado");
        var rich = two with { Speeds = new(5000, 5000, 1), Combustion = two.Combustion with { AirFuelRatio = 13.5 } };
        Require(TwoStrokeSimulator.Sweep(rich, false).Runs[0] is { Status: TwoStrokeRunStatus.OutsideDomain, Point: null },
            "La mezcla rica no produce una curva engañosa");
    }

    private static void FluxCases()
    {
        var g = new GasEquation(7);
        var ext = new double[g.Size]; var edge = new double[g.Size]; var f = new double[g.Size];
        var a = g.Encode(ext, [0, .23, .77, 0, 0, 0, 0], P, T, 0, 1);
        var b = g.Encode(edge, [0, 0, 0, 1, 0, 0, 0], .94 * P, 432, 0, .2);
        ReservoirBoundary.Flux(g, ExteriorBoundary.PressureReservoir, ext, a, edge, b, f);
        double mass = f.Take(7).Sum();
        Require(mass > 0 && f[3] == 0 && Math.Abs(f[g.Tag] / mass - 1) < 1e-12 &&
            Math.Abs(f[g.Energy] / mass - (a.Energy + a.Pressure) / a.Density) < 1e-8, "Entrada transporta entalpía de remanso y especies donantes");
        b = g.Encode(edge, [0, 0, 0, 1, 0, 0, 0], 1.06 * P, 432, 0, .2);
        ReservoirBoundary.Flux(g, ExteriorBoundary.PressureReservoir, ext, a, edge, b, f);
        Require(f[3] < 0 && f[1] == 0 && Math.Abs(f[g.Tag] / f[3] - .2) < 1e-12 && f[g.Momentum] > P,
            "Descarga subsónica adopta presión de la boca y composición interior");
        b = g.Encode(edge, [0, 0, 0, 1, 0, 0, 0], 1.06 * P, 432, -800, .2);
        ReservoirBoundary.Flux(g, ExteriorBoundary.PressureReservoir, ext, a, edge, b, f);
        var expected = new double[g.Size];
        g.PhysicalFlux(edge, b, expected);
        Require(f.SequenceEqual(expected), "Descarga supersónica sin condición impuesta");
    }

    private static void WallCases()
    {
        double diameter = .04, mu = 1.846e-5;
        Span<double> state = stackalloc double[4];
        var p = Gas.Encode(state, Pure, P, T, .1, 0);
        var wall = new DuctWall(0, null);
        var source = wall.Sources(p, diameter);
        double poisson = -32 * mu * .1 / (diameter * diameter);
        Require(Math.Abs(source.Momentum / poisson - 1) < .006 && source.Energy == 0,
            "Hagen–Poiseuille y energía adiabática");
        var hot = new DuctWall(0, 400).Sources(p, diameter);
        var cold = new DuctWall(0, 280).Sources(p, diameter);
        Require(hot.Energy > 0 && cold.Energy < 0 && Math.Abs(hot.Energy / cold.Energy + 5) < 1e-12, "Signo de intercambio térmico");
        var geometry = new DuctGeometry(.5, diameter, diameter, 24) { Wall = new(0, 400) };
        var mesh = new DuctMesh(geometry, Gas, DuctScheme.Muscl);
        var y = new double[mesh.Size]; var rate = new double[mesh.Size];
        for (int i = 0; i < mesh.Cells; i++) Gas.Encode(y.AsSpan(i * 4, 4), Pure, P, T, 0, 0);
        mesh.Prepare(y);
        double[] flank = [0, mesh.ValveArea * P, 0, 0];
        mesh.Rates(y, rate, flank, flank, out _, out double heat);
        double sum = Enumerable.Range(0, mesh.Cells).Sum(i => mesh.Volume(i) * rate[i * 4 + Gas.Energy]);
        Require(heat > 0 && Math.Abs(heat - sum) < 1e-11, "Balance de calor con extremos impermeables");
        var fast = Gas.Encode(state, Pure, P, T, 30, 0);
        Require(new DuctWall(.00010, null).Sources(fast, diameter).Momentum < wall.Sources(fast, diameter).Momentum,
            "La rugosidad incrementa la resistencia turbulenta");
    }

    private delegate void Initial(double x, Span<double> y);
    private static double[] Evolve(double end, Initial initial, ExteriorBoundary? mode, Action<double, double>? probe)
    {
        var mesh = new DuctMesh(new(1, .04, .04, 200), Gas, DuctScheme.Muscl);
        int n = Gas.Size;
        var y = new double[mesh.Size]; var stage = new double[y.Length]; var rate = new double[y.Length];
        var exterior = new double[n]; var ambient = Gas.Encode(exterior, Pure, P, T, 0, 0);
        for (int i = 0; i < mesh.Cells; i++) initial((i + .5) * mesh.Dx, y.AsSpan(i * n, n));
        double t = 0;
        while (t < end)
        {
            double step = Math.Min(end - t, .4 * Rates(y));
            for (int i = 0; i < y.Length; i++) stage[i] = y[i] + step * rate[i];
            Rates(stage);
            for (int i = 0; i < y.Length; i++) y[i] = .5 * (y[i] + stage[i] + step * rate[i]);
            t += step;
            probe?.Invoke(t, Gas.Decode(y.AsSpan((mesh.Cells - 4) * n, n)).Pressure);
        }
        return Enumerable.Range(0, mesh.Cells).Select(i => Gas.Decode(y.AsSpan(i * n, n)).Pressure).ToArray();

        double Rates(double[] q)
        {
            Span<double> left = stackalloc double[n], right = stackalloc double[n], face = stackalloc double[n];
            mesh.Prepare(q);
            var a = mesh.BoundaryState(q, false, face);
            if (mode is { } exteriorMode) ReservoirBoundary.Flux(Gas, exteriorMode, exterior, ambient, face, a, left);
            else { left.Clear(); left[Gas.Momentum] = Gas.WallPressure(a, false); }
            for (int i = 0; i < n; i++) left[i] *= mesh.ReservoirArea;
            var b = mesh.BoundaryState(q, true, face);
            right.Clear(); right[Gas.Momentum] = Gas.WallPressure(b, true) * mesh.ValveArea;
            return mesh.Rates(q, rate, left, right);
        }
    }

    private static void Require(bool ok, string note)
    {
        if (!ok) throw new Exception(note);
    }
}
