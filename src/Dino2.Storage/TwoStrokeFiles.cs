using System.Globalization;
using System.Text.Json;
using Dino2.Core;
using Dino2.Core.TwoStroke;

namespace Dino2.Storage;

public sealed record TwoStrokeProject(TwoStrokeConfiguration Current, TwoStrokeConfiguration? Original,
    TwoStrokeSweep? CurrentResult = null, TwoStrokeSweep? OriginalResult = null,
    TwoStrokeComparisonEvidence? Resolution = null);

public static partial class TwoStrokeFiles
{
    private const string LegacyFormat = "dino2-two-stroke-project-v1";
    private const string Format = "dino2-two-stroke-project-v2";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions IntegrityJson = new(Json) { WriteIndented = false, IgnoreReadOnlyProperties = true };
    private sealed record Document(string Format, string Model, TwoStrokeProject Project, string? Integrity = null);

    public static TwoStrokeProject Load(string path)
    {
        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<Document>(stream, Json);
        if (doc is null || doc.Format is not (Format or LegacyFormat) || doc.Model != TwoStrokeConfiguration.ModelId || doc.Project is null)
            throw new InvalidDataException("Archivo de proyecto 2T incompatible.");
        Validate(doc.Project);
        ProjectIntegrity.Check(doc.Project, doc.Integrity, doc.Format == Format, IntegrityJson);
        return doc.Project;
    }

    public static void Save(string path, TwoStrokeProject project)
    {
        Validate(project);
        WriteJson(path, new Document(Format, TwoStrokeConfiguration.ModelId, project, ProjectIntegrity.Digest(project, IntegrityJson)));
    }

    private static void Validate(TwoStrokeProject project)
    {
        if (project.Current is null) throw new InvalidDataException("Falta la configuración 2T.");
        project.Current.Validate();
        project.Original?.Validate();
        CheckResult(project.Current, project.CurrentResult);
        CheckResult(project.Original, project.OriginalResult);
        if (project.Resolution is { } evidence &&
            (evidence.Check is null || evidence.Check.Levels is null || evidence.Check.Levels.Any(level => level is null) || evidence.Check.CandidateId != 1 || evidence.Check.ReferenceId != 0 ||
            project.OriginalResult?.Runs.Any(r => r.Rpm == evidence.Rpm && r.Point is not null) != true ||
            project.CurrentResult?.Runs.Any(r => r.Rpm == evidence.Rpm && r.Point is not null) != true))
            throw new InvalidDataException("El contraste necesita un régimen periódico común a A y B.");
    }

    private static void CheckResult(TwoStrokeConfiguration? configuration, TwoStrokeSweep? sweep)
    {
        if (sweep is null) return;
        if (configuration is null || sweep.Configuration != configuration || sweep.Runs is null ||
            sweep.Runs.Any(run => run is null) ||
            !sweep.Runs.Select(r => r.Rpm).SequenceEqual(configuration!.Speeds.Values().Take(sweep.Runs.Count)) ||
            sweep.Runs.Any(run => !Enum.IsDefined(run.Status) || (run.Point is { } point
                ? point.Rpm != run.Rpm || point.Trace is null || point.Balance is null || run.Status != TwoStrokeRunStatus.Periodic ||
                  run.Failure is not null || !Consistent(point, configuration)
                : run.Status == TwoStrokeRunStatus.Periodic || string.IsNullOrWhiteSpace(run.Failure))))
            throw new InvalidDataException("La curva 2T y su configuración deben corresponder.");
        if (sweep.Provenance is { } provenance &&
            !ValidProvenance(provenance, TwoStrokeConfiguration.ModelId, configuration.Revision) &&
            !ValidProvenance(provenance, TwoStrokeConfiguration.ModelId, configuration.Stage11Revision) &&
            !ValidProvenance(provenance, TwoStrokeConfiguration.ModelId, configuration.Stage13Revision) &&
            !ValidProvenance(provenance, TwoStrokeConfiguration.ModelId, configuration.Stage12Revision))
            throw new InvalidDataException("La revisión declarada de la curva 2T no corresponde a su configuración.");
    }

    private static bool ValidProvenance(ResultProvenance p, string model, string revision) =>
        p.PhysicsModel == model && p.NumericalRevision == revision && p.ResultSchema == 1 &&
        !string.IsNullOrWhiteSpace(p.Build) && p.SourceTree is "clean" or "dirty" or "unknown" && p.CalculatedUtc.Offset == TimeSpan.Zero;

    private static bool Agree(double expected, double actual) => double.IsFinite(actual) && double.IsFinite(expected) &&
        Math.Abs(expected - actual) <= 1e-8 * Math.Max(1, Math.Max(Math.Abs(expected), Math.Abs(actual)));

    private static bool Consistent(TwoStrokePoint p, TwoStrokeConfiguration configuration)
    {
        double displacement = configuration.Geometry.DisplacementM3;
        double indicatedWork = p.CylinderWorkJ + p.CrankcaseWorkJ;
        double shaftWork = p.ShaftTorqueNm * 2 * Math.PI;
        double massScale = p.FreshDeliveredKg + p.FreshExhaustReturnKg + p.FreshTrappedKg + p.ResidualMassKg;
        double energyScale = 1 + Math.Abs(indicatedWork) + Math.Abs(p.WallHeatJ) + p.FuelMassKg * 45e6;
        return p.Balance is not null && Agree(indicatedWork * p.Rpm / 60, p.IndicatedPowerW) && Agree(shaftWork * p.Rpm / 60, p.ShaftPowerW) &&
            Agree(indicatedWork / displacement, p.ImepPa) && Agree(shaftWork / displacement, p.BmepPa) &&
            (p.ShaftPowerW <= p.IndicatedPowerW || Agree(p.ShaftPowerW, p.IndicatedPowerW)) &&
            p.PeakPressurePa > 0 && p.PeakTemperatureK is >= 200 and <= 6000 &&
            p.Cycles >= 1 && p.Cycles <= configuration.Numerics.MaximumCycles &&
            p.PeriodicError >= 0 && p.PeriodicError <= configuration.Numerics.PeriodicTolerance &&
            p.FuelMassKg >= 0 && p.FuelBurnedKg >= 0 &&
            p.DeliveryRatio >= 0 && p.ChargingEfficiency >= 0 && p.ScavengingEfficiency is >= 0 and <= 1.000001 &&
            (p.TrappingEfficiency is null or >= 0 and <= 1.000001) &&
            p.FreshDeliveredKg >= -1e-12 && p.FreshTransferReturnKg >= -1e-12 &&
            p.FreshExhaustOutKg >= -1e-12 && p.FreshExhaustReturnKg >= -1e-12 &&
            p.FreshTrappedKg >= -1e-12 && p.ResidualMassKg >= -1e-12 &&
            Math.Abs(p.Balance.MassKg) <= 1e-12 + massScale * 1e-6 &&
            Math.Abs(p.Balance.EnergyJ) <= 1e-6 * energyScale &&
            p.TrappedCompositionKg.ToArray(true).All(m => m >= -1e-12) &&
            p.ResidualCompositionKg.ToArray(true).All(m => m >= -1e-12) &&
            Agree(p.TrappedCompositionKg.Sum, p.FreshTrappedKg + p.ResidualMassKg) &&
            Agree(p.ResidualCompositionKg.Sum, p.ResidualMassKg);
    }

    private static void WriteJson<T>(string path, T value)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary)) JsonSerializer.Serialize(stream, value, Json);
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    public static void Export(string path, TwoStrokeSweep sweep)
    {
        using var csv = File.CreateText(path);
        const string header = "model,rpm,status,diagnostic,pi_w,p_shaft_w,t_shaft_nm,imep_pa,bmep_pa,bsfc_g_kwh,delivery_ratio,trapping_efficiency,scavenging_efficiency,charging_efficiency,fresh_delivered_kg,fresh_transfer_return_kg,fresh_trapped_kg,fresh_exhaust_out_kg,fresh_exhaust_return_kg,residual_kg,peak_p_pa,peak_t_k,cycles,steps,periodic_error,mass_error_kg,energy_error_j,fresh_marker_error_kg,cycle_steps_total,rejected_steps,fuel_supplied_kg,fuel_burned_kg,work_cylinder_j,work_crankcase_j,wall_heat_j,residual_fuel_kg,residual_o2_kg,residual_n2_kg,residual_co2_kg,residual_h2o_kg,residual_co_kg,residual_h2_kg,numerical_revision,net_fresh_exhaust_kg,trapping_per_transfer_delivery,fresh_accounting_residual_kg,completed_cycles,final_periodic_residual,previous_periodic_residual,dominant_component,accepted_steps_diagnostic,rejected_steps_diagnostic";
        csv.WriteLine(header);
        foreach (var run in sweep.Runs)
        {
            var d = run.Convergence;
            string snapshot = FormattableString.Invariant($"{d?.Cycles},{d?.Residual:R},{d?.PreviousResidual:R},{Quote(d?.Dominant ?? "")},{d?.AcceptedSteps},{d?.RejectedSteps}");
            if (run.Point is not { } p)
            {
                csv.WriteLine($"{sweep.ModelId},{run.Rpm},{run.Status},{Quote(run.Failure ?? "")}" + new string(',', header.Count(c => c == ',') - 12) + string.Join(',', Quote(sweep.NumericalRevision ?? "stage6"), "", "", "", snapshot));
                continue;
            }
            var residual = p.ResidualCompositionKg;
            csv.WriteLine(FormattableString.Invariant($"{sweep.ModelId},{p.Rpm},periodic,,{p.IndicatedPowerW:R},{p.ShaftPowerW:R},{p.ShaftTorqueNm:R},{p.ImepPa:R},{p.BmepPa:R},{p.BsfcGPerKwh:R},{p.DeliveryRatio:R},{p.TrappingEfficiency:R},{p.ScavengingEfficiency:R},{p.ChargingEfficiency:R},{p.FreshDeliveredKg:R},{p.FreshTransferReturnKg:R},{p.FreshTrappedKg:R},{p.FreshExhaustOutKg:R},{p.FreshExhaustReturnKg:R},{p.ResidualMassKg:R},{p.PeakPressurePa:R},{p.PeakTemperatureK:R},{p.Cycles},{p.Steps},{p.PeriodicError:R},{p.Balance.MassKg:R},{p.Balance.EnergyJ:R},{p.Balance.FreshMarkerKg:R},{p.TotalAcceptedSteps},{p.TotalRejectedSteps},{p.FuelMassKg:R},{p.FuelBurnedKg:R},{p.CylinderWorkJ:R},{p.CrankcaseWorkJ:R},{p.WallHeatJ:R},{residual.FuelKg:R},{residual.OxygenKg:R},{residual.NitrogenKg:R},{residual.CarbonDioxideKg:R},{residual.WaterKg:R},{residual.CarbonMonoxideKg:R},{residual.HydrogenKg:R},{Quote(sweep.NumericalRevision ?? "stage6")},{p.NetFreshExhaustKg:R},{p.TrappingPerTransferDelivery:R},{p.FreshAccountingResidualKg:R},{snapshot}"));
        }
        CsvProvenance(csv, sweep.Provenance, "provenance");
    }

    public static void ExportTrace(string path, TwoStrokePoint point, ResultProvenance? provenance = null)
    {
        using var csv = File.CreateText(path);
        csv.WriteLine("rpm,angle_from_tdc_deg,p_cylinder_pa,t_cylinder_k,p_crankcase_pa,t_crankcase_k,reed_kg_s,transfer_kg_s,exhaust_out_kg_s,v_cylinder_m3,v_crankcase_m3,fresh_cylinder_kg,total_cylinder_kg,reed_face_pressure_pa,exhaust_face_pressure_pa");
        foreach (var s in point.Trace)
            csv.WriteLine(FormattableString.Invariant($"{point.Rpm},{s.AngleDeg:R},{s.CylinderPressurePa:R},{s.CylinderTemperatureK:R},{s.CrankcasePressurePa:R},{s.CrankcaseTemperatureK:R},{s.ReedKgPerS:R},{s.TransferKgPerS:R},{s.ExhaustOutKgPerS:R},{s.CylinderVolumeM3:R},{s.CrankcaseVolumeM3:R},{s.FreshCylinderKg:R},{s.CylinderMassKg:R},{s.IntakeValvePressurePa:R},{s.ExhaustPortPressurePa:R}"));
        CsvProvenance(csv, provenance, "provenance");
    }

    private static void CsvProvenance(TextWriter writer, ResultProvenance? provenance, string label)
    {
        if (provenance is not null) writer.WriteLine($"# {label}=" + JsonSerializer.Serialize(provenance, IntegrityJson));
    }

    public static void ExportComparison(string path, TwoStrokeSweep a, TwoStrokeSweep b)
    {
        using var csv = File.CreateText(path);
        csv.WriteLine("rpm,a_shaft_w,b_shaft_w,delta_w,a_shaft_nm,b_shaft_nm,delta_nm,a_fresh_trapped_kg,b_fresh_trapped_kg,a_numerical_revision,b_numerical_revision");
        foreach (var (left, right) in a.Runs.Join(b.Runs, x => x.Rpm, x => x.Rpm, (a, b) => (a.Point, b.Point)))
        {
            if (left is null || right is null) continue;
            csv.WriteLine(FormattableString.Invariant($"{left.Rpm},{left.ShaftPowerW:R},{right.ShaftPowerW:R},{right.ShaftPowerW - left.ShaftPowerW:R},{left.ShaftTorqueNm:R},{right.ShaftTorqueNm:R},{right.ShaftTorqueNm - left.ShaftTorqueNm:R},{left.FreshTrappedKg:R},{right.FreshTrappedKg:R},{Quote(a.NumericalRevision ?? "stage6")},{Quote(b.NumericalRevision ?? "stage6")}"));
        }
        CsvProvenance(csv, a.Provenance, "A_provenance");
        CsvProvenance(csv, b.Provenance, "B_provenance");
    }
    public static void ExportComparisonResolution(string path, TwoStrokeComparisonEvidence evidence)
    {
        using var csv = File.CreateText(path);
        csv.WriteLine("model,rpm,scale,shaft_a_nm,shaft_b_nm,delta_nm,observed_band_nm,verdict,numerical_revision,diagnostic");
        foreach (var level in evidence.Check.Levels)
            csv.WriteLine(FormattableString.Invariant($"{TwoStrokeConfiguration.ModelId},{evidence.Rpm},{level.Scale},{level.ReferenceScore:R},{level.CandidateScore:R},{level.Advantage:R},{evidence.Check.ObservedBand:R},{evidence.Check.Verdict},{Quote(evidence.Check.NumericalRevision ?? "stage6")},{Quote(level.Error ?? "")}"));
    }
}
