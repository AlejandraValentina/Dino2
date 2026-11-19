using System.Text.Json;
using Dino2.Core;
using Dino2.Storage;
using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

if (args.Length is < 2 or > 4)
{
    Console.Error.WriteLine("Uso: Dino2.Cli otto.json resultados.csv | --4t proyecto.json resultados.csv [traza.csv] | --example-4t proyecto.json");
    return 2;
}
try
{
    if ((args[0] == "--example-4t" || args[0] == "--example-reactive" || args[0] == "--example-equilibrium") && args.Length == 2)
    {
        FourStrokeFiles.Save(args[1], new(args[0] switch
        {
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
