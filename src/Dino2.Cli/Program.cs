using System.Text.Json;
using Dino2.Core;
using Dino2.Storage;
using Dino2.Core.FourStroke;
using Dino2.Core.TwoStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;

if (args.Length is < 2 or > 4)
{
    Console.Error.WriteLine("Uso: Dino2.Cli otto.json resultados.csv | --4t proyecto.json resultados.csv [traza.csv] | --example-4t proyecto.json | --example-2t proyecto.json | --2t proyecto.json curva.csv [traza.csv]");
    return 2;
}
try
{
    if (args[0] == "--example-2t" && args.Length == 2)
    {
        TwoStrokeFiles.Save(args[1], new(TwoStrokeConfiguration.Example, null));
        return 0;
    }
    if (args[0] == "--2t" && args.Length >= 3)
    {
        var project = TwoStrokeFiles.Load(args[1]);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var current = TwoStrokeSimulator.Sweep(project.Current, args.Length == 4);
        TwoStrokeFiles.Export(args[2], current);
        if (args.Length == 4 && current.Runs.FirstOrDefault(r => r.Point is not null)?.Point is { } p)
            TwoStrokeFiles.ExportTrace(args[3], p);
        if (project.Original is not null)
        {
            var original = TwoStrokeSimulator.Sweep(project.Original, false);
            TwoStrokeFiles.ExportComparison(Path.ChangeExtension(args[2], ".comparison.csv"), original, current);
        }
        foreach (var run in current.Runs)
            Console.WriteLine(run.Point is { } point
                ? FormattableString.Invariant($"{point.Rpm}: Pi={point.IndicatedPowerW:F2} W, P={point.ShaftPowerW:F2} W, T={point.ShaftTorqueNm:F4} Nm, fresh={point.FreshTrappedKg:E4} kg, cycles={point.Cycles}, periodic={point.PeriodicError:E3}")
                : $"{run.Rpm}: {run.Failure}");
        Console.WriteLine(FormattableString.Invariant($"{current.ModelId}: {timer.Elapsed.TotalSeconds:F3} s."));
        return current.Complete ? 0 : 1;
    }
    if ((args[0] == "--example-4t" || args[0] == "--example-reactive" || args[0] == "--example-equilibrium" || args[0] == "--example-ducts") && args.Length == 2)
    {
        FourStrokeFiles.Save(args[1], new(args[0] switch
        {
            "--example-ducts" => ThermochemicalOptions.EquilibriumExample with
            {
                Thermochemistry = ThermochemicalOptions.Default with { Products = ProductChemistry.ClosedCycleEquilibrium, BurnLaw = BurnLaw.PrescribedWiebe },
                Ducts = GasPath.Example, Speeds = new(2000, 6000, 1000)
            },
            "--example-equilibrium" => ThermochemicalOptions.EquilibriumExample,
            "--example-reactive" => ThermochemicalOptions.Example,
            _ => FourStrokeConfiguration.Example
        }, null));
        return 0;
    }
    if (args[0] == "--4t" && args.Length >= 3)
    {
        var project = FourStrokeFiles.Load(args[1]);
        var simulation = EngineSimulation.Run(project.Current);
        FourStrokeFiles.Export(args[2], simulation);
        if (args.Length == 4) FourStrokeFiles.ExportTrace(args[3], simulation, simulation.Points[0].Rpm);
        if (project.Original is not null)
        {
            var original = EngineSimulation.Run(project.Original);
            FourStrokeFiles.ExportComparison(Path.ChangeExtension(args[2], ".comparison.csv"), original, simulation);
        }
        Console.WriteLine($"{simulation.Model}: {simulation.Points.Count} RPM, {simulation.ElapsedMilliseconds:F1} ms. Sin validación experimental.");
        foreach (var p in simulation.Points)
            Console.WriteLine(FormattableString.Invariant($"{p.Rpm}: Pi={p.IndicatedPowerW:F2} W, Pb={p.BrakePowerW:F2} W, Tb={p.BrakeTorqueNm:F4} Nm, VE={p.VolumetricEfficiency:F5}, cycles={p.Cycles}, periodic={p.PeriodicError:E3}"));
        return 0;
    }
    if (args.Length != 2) throw new ArgumentException("Argumentos inválidos. Use --4t proyecto.json resultados.csv [traza.csv].");
    var result = OttoCycle.Calculate(ConfigurationFile.Load(args[0]));
    ResultCsv.Save(args[1], result);
    Console.WriteLine(FormattableString.Invariant(
        $"{OttoCycle.ModelId}: {result.Points.Count} puntos; W={result.WorkJ:R} J/ciclo; eta={result.Efficiency:R}; par={result.Points[0].IndicatedTorqueNm:R} Nm indicados ideales."));
    return 0;
}
catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArithmeticException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
