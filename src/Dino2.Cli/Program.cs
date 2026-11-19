using System.Text.Json;
using Dino2.Core;
using Dino2.Storage;

if (args.Length != 2)
{
    Console.Error.WriteLine("Uso: Dino2.Cli configuracion.json resultados.csv");
    return 2;
}
try
{
    var result = OttoCycle.Calculate(ConfigurationFile.Load(args[0]));
    ResultCsv.Save(args[1], result);
    Console.WriteLine(FormattableString.Invariant(
        $"{OttoCycle.ModelId}: {result.Points.Count} puntos; W={result.WorkJ:R} J/ciclo; eta={result.Efficiency:R}; par={result.Points[0].IndicatedTorqueNm:R} Nm indicados ideales."));
    return 0;
}
catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
