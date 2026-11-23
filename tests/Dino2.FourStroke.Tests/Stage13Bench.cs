using System.Diagnostics;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;

internal static class Stage13Bench
{
    internal static void Micro(string folder)
    {
        Directory.CreateDirectory(folder);
        var gas = new GasEquation(1);
        Span<double> tank = stackalloc double[gas.Size], edge = stackalloc double[gas.Size], flux = stackalloc double[gas.Size];
        var rows = new List<string> { "tank_pa,edge_pa,alpha,iterations,median_us_per_evaluation" };
        const int samples = 50_000;
        foreach (var (pTank, pEdge, alpha) in new[] { (100500.0, 100000.0, .3), (100000.0, 100500.0, .3),
            (250000.0, 100000.0, .3), (100000.0, 250000.0, .3), (250000.0, 100000.0, 1.0), (250000.0, 100000.0, 0.0) })
        {
            var reservoir = gas.Encode(tank, [1], pTank, 300, 0, .7);
            var pipe = gas.Encode(edge, [1], pEdge, 300, 0, .1);
            for (int i = 0; i < 10_000; i++) PortBoundary.Flux(gas, .001, alpha * .001, tank, reservoir, edge, pipe, flux);
            PortTrace? trace = null;
            var elapsed = new double[5];
            for (int j = 0; j < elapsed.Length; j++)
            {
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < samples; i++) PortBoundary.Flux(gas, .001, alpha * .001, tank, reservoir, edge, pipe, flux, out trace);
                elapsed[j] = watch.Elapsed.TotalMicroseconds / samples;
            }
            Array.Sort(elapsed);
            rows.Add(FormattableString.Invariant($"{pTank},{pEdge},{alpha},{trace?.Iterations},{elapsed[2]:F6}"));
        }
        File.WriteAllLines(Path.Combine(folder, "micro.csv"), rows);
    }

    private const string Header = "feeding,ratio,alpha,cells,T0,composition,mass_port_kg_s,mass_far_kg_s,pressure_bulk_pa,pressure_throat_pa,T_throat_k,M_throat,bulk_u_m_s,mid_p_pa,mid_T_k,mid_M,energy_error_j,mass_error_kg,iterations,elapsed_ms,species_error_kg,momentum_error_ns,status";
    internal static void Run(string folder, bool thermal)
    {
        Directory.CreateDirectory(folder);
        var jobs = new List<(bool Feeding, double Ratio, double Alpha, int Cells, double Temperature, string Composition)>();
        if (thermal)
        {
            foreach (bool feeding in new[] { true, false })
            foreach (double ratio in new[] { 1.05, 1.20, 2.5 })
            foreach (double temperature in new[] { 300.0, 900.0 })
            foreach (string composition in new[] { "air", "products" })
                jobs.Add((feeding, ratio, .30, 64, temperature, composition));
        }
        else
        {
            foreach (bool feeding in new[] { true, false })
            foreach (double ratio in new[] { 1.005, 1.01, 1.02, 1.05, 1.20, 1.60, 2.50 })
            foreach (double alpha in new[] { .10, .30, .60, 1.0 })
            foreach (int cells in new[] { 64, 128 })
                jobs.Add((feeding, ratio, alpha, cells, 300, "ideal"));
        }
        var rows = new string[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            var j = jobs[i];
            try { rows[i] = Case(j.Feeding, j.Ratio, j.Alpha, j.Cells, j.Temperature, j.Composition); }
            catch (ArithmeticException ex)
            {
                rows[i] = Prefix(j.Feeding, j.Ratio, j.Alpha, j.Cells, j.Temperature, j.Composition) +
                    new string(',', 17) + ex.Message.Replace(',', ';');
            }
            Console.WriteLine(rows[i]);
        });
        File.WriteAllLines(Path.Combine(folder, thermal ? "thermal.csv" : "aperture.csv"), [Header, .. rows]);
    }

    private static string Prefix(bool feeding, double ratio, double alpha, int cells, double temperature, string composition) =>
        FormattableString.Invariant($"{(feeding ? 1 : 0)},{ratio},{alpha},{cells},{temperature},{composition},");

    private static string Case(bool feeding, double ratio, double alpha, int cells, double temperature, string composition)
    {
        var timer = Stopwatch.StartNew();
        double[] fractions = composition switch
        {
            "air" => [0,.233,.767,0,0,0,0],
            "products" => [0,.02,.74,.15,.09,0,0],
            _ => [1]
        };
        var gas = new GasEquation(fractions.Length);
        var mesh = new DuctMesh(new(.4, .04, .04, cells), gas, DuctScheme.Muscl);
        double pTank = feeding ? ratio * 100000 : 100000, pFar = feeding ? 100000 : ratio * 100000;
        var tank = new double[gas.Size]; var far = new double[gas.Size];
        double[] tankMixture = composition switch
        {
            "air" when !feeding => [0,.02,.74,.15,.09,0,0],
            "products" when !feeding => [0,.233,.767,0,0,0,0],
            _ => fractions
        };
        var tankState = gas.Encode(tank, tankMixture, pTank, temperature, 0, .7);
        var farState = gas.Encode(far, fractions, pFar, temperature, 0, .1);
        var y = new double[mesh.Size];
        for (int i = 0; i < cells; i++) far.CopyTo(y, i * gas.Size);
        var stage = new double[y.Length]; var v1 = new double[y.Length]; var v2 = new double[y.Length];
        var left = new double[gas.Size]; var right = new double[gas.Size];
        var e = new double[gas.Size];
        PortTrace? trace = null;
        double pressure = 0;
        double Rates(double[] state, double[] rate)
        {
            mesh.Prepare(state);
            var farFace = mesh.BoundaryState(state, false, e);
            ReservoirBoundary.Flux(gas, ExteriorBoundary.PressureReservoir, far, farState, e, farFace, left);
            for (int k = 0; k < left.Length; k++) left[k] *= mesh.ReservoirArea;
            var near = mesh.BoundaryState(state, true, e);
            pressure = PortBoundary.Flux(gas, mesh.ValveArea, alpha * mesh.ValveArea, tank, tankState, e, near, right, out trace);
            return mesh.Rates(state, rate, left, right);
        }
        double Inventory(double[] state, int column)
        {
            double sum = 0;
            for (int i = 0; i < cells; i++) sum += mesh.Volume(i) * state[i * gas.Size + column];
            return sum;
        }
        var initial = Enumerable.Range(0, gas.Size).Select(k => Inventory(y, k)).ToArray();
        var integrated = new double[gas.Size];
        var balanceFirst = new double[gas.Size];
        double WeightedMass(double[] flux) => flux.Take(gas.SpeciesCount).Sum();
        double time = 0;
        const double end = .10;
        while (time < end)
        {
            double dt = Math.Min(end - time, .35 * Rates(y, v1));
            for (int k = 0; k < gas.Size; k++) balanceFirst[k] = left[k] - right[k];
            for (int i = 0; i < y.Length; i++) stage[i] = y[i] + dt * v1[i];
            Rates(stage, v2);
            for (int k = 0; k < gas.Size; k++) integrated[k] += dt * .5 * (balanceFirst[k] + left[k] - right[k]);
            for (int i = 0; i < y.Length; i++) y[i] += dt * .5 * (v1[i] + v2[i]);
            time += dt;
        }
        Rates(y, v1);
        var error = Enumerable.Range(0, gas.Size).Select(k => Inventory(y, k) - initial[k] - integrated[k]).ToArray();
        var speciesErrors = error.Take(gas.SpeciesCount).ToArray();
        var mid = gas.Decode(y.AsSpan(cells / 2 * gas.Size, gas.Size));
        return Prefix(feeding, ratio, alpha, cells, temperature, composition) + FormattableString.Invariant(
            $"{WeightedMass(right):G10},{WeightedMass(left):G10},{pressure:G10},{trace?.ThroatPressurePa:G10},{trace?.ThroatTemperatureK:G10},{trace?.ThroatMach:G10},{trace?.MixedVelocityMps:G10},{mid.Pressure:G10},{mid.Temperature:G10},{mid.Velocity / mid.SoundSpeed:G10},{error[gas.Energy]:E4},{speciesErrors.Sum():E4},{trace?.Iterations},{timer.ElapsedMilliseconds},{speciesErrors.Max(Math.Abs):E4},{error[gas.Momentum]:E4},ok");
    }
}
