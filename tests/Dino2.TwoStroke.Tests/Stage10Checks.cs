using System.Diagnostics;
using Dino2.Core.Multicylinder;
using Dino2.Core.TwoStroke;
using Dino2.Core.Studies;

internal static class Stage10Checks
{
    private static void Require(bool predicate, string message)
    {
        if (!predicate) throw new Exception(message);
    }

    internal static MultiTwoStrokeConfiguration At(int n = 2, int rpm = 5000)
    {
        var c = MultiTwoStrokeConfiguration.Example(n);
        return c with { Cylinder = c.Cylinder with { Speeds = new(rpm, rpm, 1) } };
    }

    internal static MultiTwoStrokePoint Check(MultiTwoStrokeConfiguration c, bool trace = true)
    {
        long allocated = GC.GetTotalAllocatedBytes(true);
        var run = TwoStrokeSimulator.Sweep(c, trace).Runs.Single();
        Console.WriteLine($"n={c.Phases.Count}, a={c.SharedIntake is not null}, e={c.SharedExhaust is not null}, rpm={run.Rpm}, " +
            $"{run.Status}, cycles={run.Convergence?.Cycles}, error={run.Convergence?.Residual:G5}, dominant={run.Convergence?.Dominant}, " +
            $"W={run.Point?.ShaftPowerW:G9}, Mass={run.Point?.Balance.MassKg:E4}, U={run.Point?.Balance.EnergyJ:E4}, " +
            $"marker={run.Point?.Balance.FreshMarkerKg:E4}, eval={run.Point?.CellEvaluations}, ms={run.Milliseconds:F0}, bytes={GC.GetTotalAllocatedBytes(true) - allocated}, {run.Failure}");
        Require(run.Status == TwoStrokeRunStatus.Periodic, "Periodicidad global");
        var p = run.Point!;
        var b = p.Balance;
        Require(Math.Abs(b.MassKg) < 5e-12 && Math.Abs(b.EnergyJ) < 1e-6 && Math.Abs(b.FreshMarkerKg) < 5e-12 &&
            b.SpeciesKg.ToArray(true).Max(Math.Abs) < 5e-12 &&
            new[] { b.CarbonKg, b.HydrogenKg, b.OxygenKg, b.NitrogenKg }.Max(Math.Abs) < 5e-12 &&
            p.MaximumInterfaceSpeciesKg < 5e-12 && p.MaximumInterfaceEnergyJ < 1e-6, "Conservación de red");
        Require(Math.Abs(p.ShaftPowerW - p.ShaftTorqueNm * p.Rpm * Math.PI / 30) < 1e-8, "Frecuencia de trabajo 2T");
        if (trace)
        {
            Require(p.Cycle[0].AngleDeg == 0 && p.Cycle[^1].AngleDeg == 360 &&
                p.Cylinders.All(q => q.Point.Trace.Count == p.Cycle.Count), "Reloj global");
            double integral = 0;
            for (int i = 1; i < p.Cycle.Count; i++)
                integral += .5 * (p.Cycle[i - 1].GasTorqueNm + p.Cycle[i].GasTorqueNm) *
                    (p.Cycle[i].AngleDeg - p.Cycle[i - 1].AngleDeg) * Math.PI / 180;
            Require(Math.Abs(integral / p.Cylinders.Sum(q => q.Point.CylinderWorkJ + q.Point.CrankcaseWorkJ) - 1) < .008,
                "Integral independiente del par de los gases");
        }
        return p;
    }

    internal static void Run(string[] args)
    {
        if (args is ["--benchmark", var countText] && int.TryParse(countText, out int benchmarkCount) && benchmarkCount is >= 1 and <= 4)
        {
            var c = At(Math.Max(2, benchmarkCount));
            var times = new double[3];
            var bytes = new long[3];
            for (int i = 0; i < times.Length; i++)
            {
                long allocation = GC.GetTotalAllocatedBytes(true);
                var clock = Stopwatch.StartNew();
                if (benchmarkCount == 1) TwoStrokeSimulator.Simulate(c.Cylinder, 5000);
                else Require(TwoStrokeSimulator.Sweep(c).Complete, "Benchmark periódico");
                times[i] = clock.Elapsed.TotalMilliseconds;
                bytes[i] = GC.GetTotalAllocatedBytes(true) - allocation;
            }
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                count = benchmarkCount, times, bytes, Process.GetCurrentProcess().PeakWorkingSet64,
                Environment.ProcessorCount, Runtime = Environment.Version.ToString()
            }));
            return;
        }
        if (args is ["--sensitivity"])
        {
            var a = At();
            var c = a.Cylinder;
            var baseline = Check(a, false);
            var cases = new (string Name, MultiTwoStrokeConfiguration Config)[]
            {
                ("Sin admisión común", a with { SharedIntake = null }),
                ("Sin escape común", a with { SharedExhaust = null }),
                ("Longitud del ramal de admisión 160 mm", a with { Cylinder = c with { IntakeDuct = c.IntakeDuct with { LengthM = .16 } } }),
                ("Volumen de cárter 190 cm³", a with { Cylinder = c with { CrankcaseMinimumM3 = .000190 } }),
                ("Techo transfer 44 mm", a with { Cylinder = c with { Transfer = c.Transfer with { RoofM = .044, HeightM = .010 } } }),
                ("Techo de escape 32 mm", a with { Cylinder = c with { Exhaust = c.Exhaust with { RoofM = .032 } } }),
                ("Escape primario 260 mm", a with { SharedExhaust = a.SharedExhaust! with { Primary = a.SharedExhaust!.Primary with { LengthM = .26 } } }),
                ("Fase irregular de 170°", a with { Phases = new(170) })
            };
            foreach (var (name, variant) in cases)
            {
                var p = Check(variant, false);
                double Delta(Func<TwoStrokePoint, double> select) =>
                    (p.Cylinders.Sum(z => select(z.Point)) - baseline.Cylinders.Sum(z => select(z.Point))) * 1e6;
                Console.WriteLine($"SENS {name}: deltaP={p.ShaftPowerW - baseline.ShaftPowerW:F6} W, deltaDelivered={Delta(z => z.FreshDeliveredKg):F6} mg, deltaTrapped={Delta(z => z.FreshTrappedKg):F6} mg");
            }
            var plan = new MultiTwoStrokeStudy(new(a,
                [new(TwoStrokeParameter.IntakeLength, 140, 160, 20), new(TwoStrokeParameter.SparkAdvance, 19, 20, 1)],
                new(ObjectiveKind.PeakPower)));
            var serial = plan.Run();
            var parallel = plan.Run(2);
            Require(serial.Rows.Count == 5 && serial.Rows.All(row => row.Status == StudyStatus.Completed) &&
                serial.Rows.SelectMany(r => r.Curve!.Runs).Select(r => r.Point!.ShaftPowerW)
                    .SequenceEqual(parallel.Rows.SelectMany(r => r.Curve!.Runs).Select(r => r.Point!.ShaftPowerW)) &&
                plan.Rank(serial).Count == 4 && a.Cylinder.IntakeDuct.LengthM == .14, "Estudio factorial reproducible");
            Console.WriteLine($"ESTUDIO ms1={serial.Milliseconds:F0}; ms2={parallel.Milliseconds:F0}");
            return;
        }
        if (args is ["--resolution"])
        {
            var a = At();
            a = a with
            {
                Cylinder = a.Cylinder with { IntakeDuct = a.Cylinder.IntakeDuct with { Cells = 8 },
                    Expansion = a.Cylinder.Expansion with { Cells = 16 } },
                SharedExhaust = a.SharedExhaust! with { Primary = a.SharedExhaust.Primary with { Cells = 8 } }
            };
            var b = a with { SharedExhaust = a.SharedExhaust! with { JunctionVolumeM3 = .00075 } };
            var result = MultiTwoStrokeComparison.Verify(a, b, 5000);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
            Require(result.Levels.Count == 3 && result.Levels.All(l => l.Error is null), "Campaña A/B a tres mallas");
            Check(a with { Cylinder = a.Cylinder with { Numerics = a.Cylinder.Numerics with { MaximumStepDeg = .20 } } }, false);
            Check(a, false);
            return;
        }
        if (args is ["--probe"])
        {
            foreach (int n in new[] { 2, 3, 4 })
                foreach (int rpm in new[] { 5000, 4000 })
                    Check(At(n, rpm));
            return;
        }

        var timer = Stopwatch.StartNew();
        var reference = TwoStrokeConfiguration.Example with { Speeds = new(5000, 5000, 1) };
        var mono = TwoStrokeSimulator.Simulate(reference, 5000);
        Console.WriteLine($"n=1, rpm=5000, P={mono.ShaftPowerW:G9}, cycles={mono.Cycles}, ms={timer.Elapsed.TotalMilliseconds:F0}");
        foreach (int count in new[] { 2, 3, 4 })
        {
            var frozen = new MultiTwoStrokeConfiguration(reference, FiringPhases.Even(count, 360));
            var isolated = Check(frozen);
            Require(Math.Abs(isolated.ShaftPowerW / (count * mono.ShaftPowerW) - 1) < .012 &&
                Math.Abs(isolated.ShaftTorqueNm / (count * mono.ShaftTorqueNm) - 1) < .012, "Límite independiente de potencia y par");
            foreach (var member in isolated.Cylinders)
            {
                var p = member.Point;
                Require(Math.Abs(p.CylinderWorkJ / mono.CylinderWorkJ - 1) < .012 &&
                    Math.Abs(p.FreshTrappedKg / mono.FreshTrappedKg - 1) < .012 &&
                    Math.Abs(p.PeakPressurePa / mono.PeakPressurePa - 1) < .012, "Límite independiente de masa, trabajo y presión");
                double peak = p.Trace.MaxBy(s => s.CylinderPressurePa).AngleDeg;
                double basePeak = isolated.Cylinders[0].Point.Trace.MaxBy(s => s.CylinderPressurePa).AngleDeg;
                Require(Math.Abs(Math.IEEERemainder(peak - basePeak - member.PhaseDeg, 360)) < 2.5,
                    "Pico de presión desplazado sobre el cigüeñal");
            }
            var coupled = Check(At(count));
            Require(coupled.Cycle.Max(s => s.IntakeJunctionPa) - coupled.Cycle.Min(s => s.IntakeJunctionPa) > 10 &&
                coupled.Cycle.Max(s => s.ExhaustJunctionPa) - coupled.Cycle.Min(s => s.ExhaustJunctionPa) > 10,
                "Ondas calculadas en ambos colectores");
        }
        Require(FiringPhases.Even(4, 360).FiringOrder().SequenceEqual([1, 3, 4, 2]), "Orden de encendido");
        var shortRun = At();
        shortRun = shortRun with { Cylinder = shortRun.Cylinder with { Numerics = shortRun.Cylinder.Numerics with { MaximumCycles = 3 } } };
        Require(TwoStrokeSimulator.Sweep(shortRun).Runs.Single() is { Status: TwoStrokeRunStatus.NonConverged, Point: null },
            "Ausencia de periodicidad sin curva aparente");
        try { TwoStrokeSimulator.Sweep(shortRun, cancellation: new CancellationToken(true)); throw new Exception("Cancelación ignorada."); }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS límites aislados, redes compartidas, balances, fases, periodicidad y cancelación 2T.");
    }
}
