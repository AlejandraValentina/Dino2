using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;

namespace Dino2.Storage;

public sealed record FourStrokeProject(FourStrokeConfiguration Current, FourStrokeConfiguration? Original);

public static class FourStrokeFiles
{
    private sealed record Document(int SchemaVersion, string Model, FourStrokeProject Project);
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions FlatOptions = new(Options) { WriteIndented = false };
    private static readonly JsonSerializerOptions DiagnosticOptions = new(FlatOptions) { IgnoreReadOnlyProperties = false };
    private static string ProjectModel(FourStrokeProject project) =>
        project.Current.Ducts?.Scheme == DuctScheme.Muscl || project.Original?.Ducts?.Scheme == DuctScheme.Muscl ? GasPath.MusclModelId :
        project.Current.Ducts is not null || project.Original?.Ducts is not null ? GasPath.ModelId :
        project.Current.Thermochemistry?.UsesEquilibrium == true || project.Original?.Thermochemistry?.UsesEquilibrium == true
            ? ThermochemicalOptions.EquilibriumModelId
            : project.Current.Thermochemistry?.Identifier ?? project.Original?.Thermochemistry?.Identifier ?? FourStrokeResult.ModelId;

    public static void Save(string path, FourStrokeProject project)
    {
        project.Current.Validate(); project.Original?.Validate();
        string destination = Path.GetFullPath(path), temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                string model = ProjectModel(project);
                JsonSerializer.Serialize(stream, new Document(1, model, project), Options);
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
        if (document.SchemaVersion != 1 || document.Project?.Current is null)
            throw new InvalidDataException("Modelo, versión o proyecto 4T incompatible.");
        if (document.Model != ProjectModel(document.Project)) throw new InvalidDataException("El modelo declarado no coincide con las opciones termoquímicas.");
        document.Project.Current.Validate(); document.Project.Original?.Validate();
        return document.Project;
    }
    private static string Number(double value) => double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "NA";
    private static void Metadata(StringBuilder text, FourStrokeResult result)
    {
        string expected=result.Configuration.ModelIdentifier;
        if(result.Model!=expected)throw new InvalidDataException("El resultado y la configuración declaran modelos diferentes.");
        text.AppendLine("# model=" + result.Model);
        text.AppendLine(result.Configuration.Thermochemistry is null
            ? "# evidence=model prediction; pseudo-air 0D; prescribed Wiebe; Hohenberg; empirical friction requires calibration; no experimental validation; input measurements not certified"
            : result.Configuration.Thermochemistry.UsesEquilibrium
                ? "# evidence=seven species; reduced UV equilibrium until exhaust opening; composition frozen during flow; no experimental engine validation"
                : "# evidence=five-species frozen products; total energy includes formation enthalpy; semiempirical burn law; no experimental engine validation; parameter provenance is user declared");
        if (result.Configuration.Thermochemistry is not null)
            text.AppendLine("# chemical_energy=internal_energy_J and flow_enthalpy_J include formation energies; heat_released_J is diagnostic ONLY, never add it to the total-energy balance");
        if (result.Configuration.Ducts is not null)
            text.AppendLine("# ducts=quasi-1D Euler; pressure and mass flow are resolved at valve ends; inviscid adiabatic pipes; no experimental validation");
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
        foreach (var p in result.Points.Where(p => p.Chemistry is not null))
            text.AppendLine("# chemistry_diagnostics=" + JsonSerializer.Serialize(new { p.Rpm, p.Chemistry }, DiagnosticOptions));
        foreach (var p in result.Points.Where(p => p.GasDynamics is not null))
            text.AppendLine("# duct_diagnostics=" + JsonSerializer.Serialize(new { p.Rpm, p.GasDynamics }, DiagnosticOptions));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
    public static void ExportTrace(string path, FourStrokeResult result, int rpm)
    {
        var p = result.Points.Single(p => p.Rpm == rpm);
        var text = new StringBuilder(); Metadata(text, result);
        text.AppendLine("# rpm=" + rpm);
        bool reactive = result.Configuration.Thermochemistry is not null;
        bool ducts = result.Configuration.Ducts is not null;
        bool extended = result.Configuration.Thermochemistry?.UsesEquilibrium == true;
        text.AppendLine("angle_deg,volume_m3,mass_kg,temperature_K,pressure_Pa,fresh_mass_kg,intake_lift_m,exhaust_lift_m,intake_flow_kg_s,exhaust_flow_kg_s,internal_energy_J,work_J,heat_released_J,wall_heat_J,flow_enthalpy_J" +
            (reactive ? ",fuel_kg,oxygen_kg,nitrogen_kg,carbon_dioxide_kg,water_kg" : "") +
            (extended ? ",carbon_monoxide_kg,hydrogen_kg" : "") +
            (ducts ? ",intake_port_pressure_Pa,exhaust_port_pressure_Pa" : ""));
        foreach (var s in p.Trace)
            text.AppendLine(string.Join(",", new double[] { s.AngleDeg, s.VolumeM3, s.MassKg, s.TemperatureK, s.PressurePa,
                s.FreshMassKg, s.IntakeLiftM, s.ExhaustLiftM, s.IntakeFlowKgPerS, s.ExhaustFlowKgPerS,
                s.InternalEnergyJ, s.WorkJ, s.HeatReleasedJ, s.WallHeatJ, s.FlowEnthalpyJ }.Select(Number)) +
                (reactive ? "," + string.Join(",", s.Composition.ToArray(extended).Select(Number)) : "") +
                (ducts ? $",{Number(s.IntakePortPressurePa)},{Number(s.ExhaustPortPressurePa)}" : ""));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
    public static void ExportComparison(string path, FourStrokeResult original, FourStrokeResult variant)
    {
        var text = new StringBuilder(); Metadata(text, variant);
        text.AppendLine("# original_configuration_SI=" + JsonSerializer.Serialize(original.Configuration, FlatOptions));
        text.AppendLine("# original_model="+original.Model);
        text.AppendLine("# exact common RPM only; no interpolation; difference=B-A");
        if (original.Configuration.Ducts is not null || variant.Configuration.Ducts is not null)
            text.AppendLine("# numerical_resolution=raw differences at the configured grids; a separate mesh study is required at each RPM");
        text.AppendLine("rpm,original_brake_power_W,variant_brake_power_W,power_difference_W,original_brake_torque_Nm,variant_brake_torque_Nm,torque_difference_Nm,volumetric_efficiency_difference");
        foreach (var p in EngineComparison.Compare(original, variant))
            text.AppendLine(string.Join(",", new double[] { p.Rpm, p.OriginalBrakePowerW, p.VariantBrakePowerW,
                p.PowerDifferenceW, p.OriginalBrakeTorqueNm, p.VariantBrakeTorqueNm, p.TorqueDifferenceNm, p.FillingDifference }.Select(Number)));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
}
