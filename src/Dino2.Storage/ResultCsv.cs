using System.Globalization;
using System.Text;
using Dino2.Core;

namespace Dino2.Storage;

public static class ResultCsv
{
    // Comment metadata + SI columns: export contains the exact input snapshot.
    public static void Save(string path, OttoResult result)
    {
        var c = result.Configuration;
        var text = new StringBuilder();
        text.AppendLine($"# model={OttoCycle.ModelId}; ideal indicated; not experimentally validated");
        text.AppendLine("# configuration_SI=" + System.Text.Json.JsonSerializer.Serialize(c));
        text.AppendLine(FormattableString.Invariant(
            $"# work_J={result.WorkJ:R}; heat_in_J={result.HeatInJ:R}; heat_out_J={result.HeatOutJ:R}; efficiency={result.Efficiency:R}; mass_kg={result.MassKg:R}; imep_Pa={result.ImepPa:R}"));
        foreach (var state in result.States)
            text.AppendLine(FormattableString.Invariant(
                $"# state={state.Number}; volume_m3={state.VolumeM3:R}; temperature_K={state.TemperatureK:R}; pressure_Pa={state.PressurePa:R}"));
        text.AppendLine("rpm,indicated_torque_Nm,indicated_power_W");
        foreach (var point in result.Points)
            text.AppendLine(string.Join(",", point.Rpm.ToString(CultureInfo.InvariantCulture),
                point.IndicatedTorqueNm.ToString("R", CultureInfo.InvariantCulture),
                point.IndicatedPowerW.ToString("R", CultureInfo.InvariantCulture)));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
}
