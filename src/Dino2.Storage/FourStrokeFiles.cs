using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dino2.Core.FourStroke;

namespace Dino2.Storage;

public sealed record FourStrokeProject(FourStrokeConfiguration Current, FourStrokeConfiguration? Original);

public static class FourStrokeFiles
{
    private sealed record Document(int SchemaVersion, string Model, FourStrokeProject Project);
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        IgnoreReadOnlyProperties = true
    };
    private static readonly JsonSerializerOptions FlatOptions = new(Options) { WriteIndented = false };
    public static void Save(string path, FourStrokeProject project)
    {
        project.Current.Validate(); project.Original?.Validate();
        string destination = Path.GetFullPath(path), temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, new Document(1, FourStrokeResult.ModelId, project), Options);
                stream.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static FourStrokeProject Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<Document>(stream, Options)
            ?? throw new InvalidDataException("Archivo 4T vacío.");
        if (document.SchemaVersion != 1 || document.Model != FourStrokeResult.ModelId || document.Project?.Current is null)
            throw new InvalidDataException("Modelo, versión o proyecto 4T incompatible.");
        document.Project.Current.Validate(); document.Project.Original?.Validate();
        return document.Project;
    }
    private static string Number(double value) => double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "NA";
    private static void Metadata(StringBuilder text, FourStrokeResult result)
    {
        text.AppendLine("# model=" + FourStrokeResult.ModelId);
        text.AppendLine("# evidence=uncalibrated synthetic example; pseudo-air 0D; prescribed Wiebe; Hohenberg; empirical friction; no experimental validation");
        text.AppendLine("# configuration_SI=" + JsonSerializer.Serialize(result.Configuration, FlatOptions));
        text.AppendLine("# units=SI; angle in degrees; NA=undefined (e.g. efficiency with zero fuel energy)");
    }
    public static void Export(string path, FourStrokeResult result)
    {
        var text = new StringBuilder(); Metadata(text, result);
        text.AppendLine("rpm,indicated_power_W,brake_power_W,indicated_torque_Nm,brake_torque_Nm,bmep_Pa,volumetric_efficiency,indicated_efficiency,brake_efficiency,fuel_kg,gross_work_J,pumping_work_J,net_work_J,friction_work_J,peak_pressure_Pa,peak_temperature_K,heat_released_J,wall_heat_J,flow_enthalpy_J,mass_residual_kg,energy_residual_J,periodic_error,cycles,accepted_steps,rejected_steps,rhs_evaluations,elapsed_ms");
        foreach (var p in result.Points)
            text.AppendLine(string.Join(",", new double[] { p.Rpm, p.IndicatedPowerW, p.BrakePowerW, p.IndicatedTorqueNm,
                p.BrakeTorqueNm, p.BmepPa, p.VolumetricEfficiency, p.IndicatedEfficiency, p.BrakeEfficiency, p.FuelMassKg,
                p.GrossWorkJ, p.PumpingWorkJ, p.NetWorkJ, p.FrictionWorkJ, p.PeakPressurePa, p.PeakTemperatureK,
                p.HeatReleasedJ, p.WallHeatJ, p.FlowEnthalpyJ, p.MassResidualKg, p.EnergyResidualJ, p.PeriodicError,
                p.Cycles, p.AcceptedSteps, p.RejectedSteps, p.RhsEvaluations, p.ElapsedMilliseconds }.Select(Number)));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
    public static void ExportTrace(string path, FourStrokeResult result, int rpm)
    {
        var p = result.Points.Single(p => p.Rpm == rpm);
        var text = new StringBuilder(); Metadata(text, result);
        text.AppendLine("# rpm=" + rpm);
        text.AppendLine("angle_deg,volume_m3,mass_kg,temperature_K,pressure_Pa,fresh_mass_kg,intake_lift_m,exhaust_lift_m,intake_flow_kg_s,exhaust_flow_kg_s,internal_energy_J,work_J,heat_released_J,wall_heat_J,flow_enthalpy_J");
        foreach (var s in p.Trace)
            text.AppendLine(string.Join(",", new double[] { s.AngleDeg, s.VolumeM3, s.MassKg, s.TemperatureK, s.PressurePa,
                s.FreshMassKg, s.IntakeLiftM, s.ExhaustLiftM, s.IntakeFlowKgPerS, s.ExhaustFlowKgPerS,
                s.InternalEnergyJ, s.WorkJ, s.HeatReleasedJ, s.WallHeatJ, s.FlowEnthalpyJ }.Select(Number)));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
    public static void ExportComparison(string path, FourStrokeResult original, FourStrokeResult variant)
    {
        var text = new StringBuilder(); Metadata(text, variant);
        text.AppendLine("# original_configuration_SI=" + JsonSerializer.Serialize(original.Configuration, FlatOptions));
        text.AppendLine("# exact common RPM only; no interpolation; difference=B-A");
        text.AppendLine("rpm,original_brake_power_W,variant_brake_power_W,power_difference_W,original_brake_torque_Nm,variant_brake_torque_Nm,torque_difference_Nm");
        foreach (var p in EngineComparison.Compare(original, variant))
            text.AppendLine(string.Join(",", new double[] { p.Rpm, p.OriginalBrakePowerW, p.VariantBrakePowerW,
                p.PowerDifferenceW, p.OriginalBrakeTorqueNm, p.VariantBrakeTorqueNm, p.TorqueDifferenceNm }.Select(Number)));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
}
