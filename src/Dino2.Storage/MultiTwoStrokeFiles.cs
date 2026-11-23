using System.Globalization;
using System.Text.Json;
using Dino2.Core;
using Dino2.Core.Studies;
using Dino2.Core.TwoStroke;

namespace Dino2.Storage;

public sealed record MultiTwoStrokeProject(MultiTwoStrokeConfiguration Current, MultiTwoStrokeConfiguration? Original = null,
    MultiTwoStrokeSweep? Curve = null, MultiTwoStrokeSweep? Baseline = null,
    TwoStrokeComparisonEvidence? Contrast = null, MultiTwoStrokeStudyRun? Study = null);

public static partial class TwoStrokeFiles
{
    private const string LegacyMultiFormat = "dino2-multicylinder-two-stroke-project-v1";
    private const string MultiFormat = "dino2-multicylinder-two-stroke-project-v2";
    private sealed record MultiDocument(string Format, string Model, MultiTwoStrokeProject Project, string? Integrity = null);

    public static MultiTwoStrokeProject LoadMulti(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<MultiDocument>(stream, Json);
        if (document is not { Format: MultiFormat or LegacyMultiFormat, Model: MultiTwoStrokeConfiguration.ModelId, Project: { } project })
            throw new InvalidDataException("Proyecto multicilíndrico 2T incompatible.");
        ValidateMulti(project);
        ProjectIntegrity.Check(project, document.Integrity, document.Format == MultiFormat, IntegrityJson);
        return project;
    }

    public static void SaveMulti(string path, MultiTwoStrokeProject project)
    {
        ValidateMulti(project);
        WriteJson(path, new MultiDocument(MultiFormat, MultiTwoStrokeConfiguration.ModelId, project,
            ProjectIntegrity.Digest(project, IntegrityJson)));
    }

    private static void ValidateMulti(MultiTwoStrokeProject p)
    {
        if (p.Current is null) throw new InvalidDataException("Falta el motor actual.");
        p.Current.Validate();
        p.Original?.Validate();
        CheckMulti(p.Current, p.Curve);
        CheckMulti(p.Original, p.Baseline);
        if (p.Contrast is { } contrast &&
            (!ValidCheck(contrast.Check, 1) || p.Baseline?.Runs.Any(r => r.Rpm == contrast.Rpm && r.Point is not null) != true ||
             p.Curve?.Runs.Any(r => r.Rpm == contrast.Rpm && r.Point is not null) != true))
            throw new InvalidDataException("Hay un contraste que no corresponde a las curvas A/B.");
        if (p.Study is not { } study) return;
        if (study.Definition is null || study.Rows is null || study.Workers is < 1 or > StudyPlan.MaximumWorkers ||
            study.Rows.Any(row => row is null))
            throw new InvalidDataException("Estudio multicilíndrico 2T incompleto.");
        var plan = new MultiTwoStrokeStudy(study.Definition);
        if ((!study.Cancelled && study.Rows.Count != plan.Grid.Variants.Count) ||
            study.Rows.Select(r => r.Id).Distinct().Count() != study.Rows.Count ||
            study.Rows.Any(r => r.Id < 0 || r.Id >= plan.Grid.Variants.Count || !Enum.IsDefined(r.Status) ||
                (r.Status == StudyStatus.Completed) != (r.Curve?.Complete == true)) ||
            (study.Check is { } check && (check.CandidateId <= 0 || check.CandidateId >= plan.Grid.Variants.Count ||
                !ValidCheck(check, check.CandidateId))))
            throw new InvalidDataException("Los resultados guardados no corresponden al estudio.");
        foreach (var row in study.Rows) CheckMulti(plan.Variant(row.Id), row.Curve, allowSummary: true);
    }

    private static bool ValidCheck(StudyCheck? check, int candidate) => check is not null &&
        check.CandidateId == candidate && check.ReferenceId == 0 && check.NumericalRevision == MultiTwoStrokeConfiguration.ModelId &&
        check.Levels is not null && check.Levels.Count is >= 1 and <= 3 && check.Levels.All(l => l is not null) &&
        check.Levels.Select(l => l.Scale).SequenceEqual(new[] { 1, 2, 4 }.Take(check.Levels.Count)) &&
        StudyVerification.Classify(check.Levels) == (check.Verdict, check.ObservedBand);

    private static void CheckMulti(MultiTwoStrokeConfiguration? c, MultiTwoStrokeSweep? sweep, bool allowSummary = false)
    {
        if (sweep is null) return;
        if (c is null || c != sweep.Configuration || sweep.Runs is null || sweep.Runs.Any(r => r is null) ||
            !c.Cylinder.Speeds.Values().SequenceEqual(sweep.Runs.Select(r => r.Rpm)))
            throw new InvalidDataException("Curva multicilíndrica incompatible con su configuración.");
        if (sweep.Provenance is { } provenance &&
            !ValidProvenance(provenance, MultiTwoStrokeConfiguration.ModelId, c.Cylinder.Revision + "-network-recovery-v1") &&
            !ValidProvenance(provenance, MultiTwoStrokeConfiguration.ModelId, c.Cylinder.Stage11Revision + "-network-recovery-v1"))
            throw new InvalidDataException("La revisión declarada de la curva multicilíndrica no corresponde a su configuración.");
        foreach (var run in sweep.Runs)
        {
            if (!Enum.IsDefined(run.Status) || (run.Point is not null) != (run.Status == TwoStrokeRunStatus.Periodic))
                throw new InvalidDataException("Estado de periodicidad inválido.");
            if (run.Point is not { } p) continue;
            if (p.Rpm != run.Rpm || run.Convergence is null || p.Balance is null || p.Cycle is null ||
                p.Cylinders is null || p.Cylinders.Count != c.Phases.Count ||
                p.Cylinders.Where((v, index) => v is null || v.Number != index + 1 || v.PhaseDeg != c.Phases.Values()[index] ||
                    v.Point is null || v.Point.Rpm != p.Rpm || v.Point.Trace is null || v.Point.Trace.Count != p.Cycle.Count ||
                    !Consistent(v.Point, c.Cylinder)).Any() ||
                (!(allowSummary && p.Cycle.Count == 0) && (p.Cycle.Count is < 2 or > 362 || p.Cycle[0].AngleDeg != 0 || p.Cycle[^1].AngleDeg != 360)))
                throw new InvalidDataException("Diagnósticos de cilindros o ciclo incompatibles.");
            double omega = p.Rpm * Math.PI / 30, volume = c.DisplacementM3;
            if (!Agree(p.Cylinders.Sum(e => e.Point.IndicatedPowerW), p.IndicatedPowerW) ||
                !Agree(p.Cylinders.Sum(e => e.Point.ShaftPowerW), p.ShaftPowerW) ||
                !Agree(p.IndicatedTorqueNm * omega, p.IndicatedPowerW) || !Agree(p.ShaftTorqueNm * omega, p.ShaftPowerW) ||
                !Agree(p.IndicatedPowerW * 60 / p.Rpm / volume, p.ImepPa) || !Agree(p.ShaftPowerW * 60 / p.Rpm / volume, p.BmepPa))
                throw new InvalidDataException("El rendimiento multicilíndrico no coincide con la suma de los cilindros, el par o el trabajo.");
        }
    }

    private static void MultiMetadata(TextWriter writer, MultiTwoStrokeConfiguration c, string key = "configuration_SI")
    {
        writer.WriteLine("# model=" + MultiTwoStrokeConfiguration.ModelId);
        writer.WriteLine("# evidence=numerical prediction; compact junctions; no engine experimental validation");
        writer.WriteLine($"# {key}=" + JsonSerializer.Serialize(c, new JsonSerializerOptions(Json) { WriteIndented = false }));
    }

    public static void ExportMulti(string path, MultiTwoStrokeSweep curve)
    {
        using var csv = File.CreateText(path);
        MultiMetadata(csv, curve.Configuration);
        CsvProvenance(csv, curve.Provenance, "provenance");
        csv.WriteLine("rpm,status,indicated_power_W,shaft_power_W,indicated_torque_Nm,shaft_torque_Nm,imep_Pa,bmep_Pa,bsfc_g_kWh,cycles,periodic_error,mass_error_kg,energy_error_J,maximum_interface_species_error_kg,maximum_interface_energy_error_J,elapsed_ms,cell_evaluations,detail");
        foreach (var r in curve.Runs)
        {
            var p = r.Point;
            csv.WriteLine(FormattableString.Invariant($"{r.Rpm},{r.Status},{p?.IndicatedPowerW:R},{p?.ShaftPowerW:R},{p?.IndicatedTorqueNm:R},{p?.ShaftTorqueNm:R},{p?.ImepPa:R},{p?.BmepPa:R},{p?.BsfcGPerKwh:R},{r.Convergence?.Cycles},{r.Convergence?.Residual:R},{p?.Balance.MassKg:R},{p?.Balance.EnergyJ:R},{p?.MaximumInterfaceSpeciesKg:R},{p?.MaximumInterfaceEnergyJ:R},{r.Milliseconds:R},{p?.CellEvaluations},{Quote(r.Failure ?? "")}"));
        }
    }

    public static void ExportMultiCycle(string path, MultiTwoStrokeSweep curve, int rpm)
    {
        var p = curve.Runs.Single(r => r.Rpm == rpm).Point ?? throw new ArgumentException("El régimen no tiene ciclo periódico.");
        using var csv = File.CreateText(path);
        MultiMetadata(csv, curve.Configuration);
        CsvProvenance(csv, curve.Provenance, "provenance");
        csv.WriteLine("rpm,cylinder,phase_deg,angle_from_cylinder1_EVO_deg,p_cylinder_Pa,t_cylinder_K,p_crankcase_Pa,t_crankcase_K,p_intake_port_Pa,p_exhaust_port_Pa,p_intake_junction_Pa,p_exhaust_junction_Pa,p_intake_trunk_Pa,p_exhaust_trunk_Pa,reed_kg_s,transfer_kg_s,exhaust_out_kg_s,fresh_cylinder_kg,mass_cylinder_kg,indicated_gas_torque_Nm");
        foreach (var member in p.Cylinders)
            for (int i = 0; i < p.Cycle.Count; i++)
            {
                var z = member.Point.Trace[i];
                var m = p.Cycle[i];
                csv.WriteLine(FormattableString.Invariant($"{rpm},{member.Number},{member.PhaseDeg:R},{m.AngleDeg:R},{z.CylinderPressurePa:R},{z.CylinderTemperatureK:R},{z.CrankcasePressurePa:R},{z.CrankcaseTemperatureK:R},{z.IntakeValvePressurePa:R},{z.ExhaustPortPressurePa:R},{m.IntakeJunctionPa:R},{m.ExhaustJunctionPa:R},{m.IntakeTrunkPa:R},{m.ExhaustTrunkPa:R},{z.ReedKgPerS:R},{z.TransferKgPerS:R},{z.ExhaustOutKgPerS:R},{z.FreshCylinderKg:R},{z.CylinderMassKg:R},{m.GasTorqueNm:R}"));
            }
    }

    public static void ExportMultiCylinders(string path, MultiTwoStrokeSweep curve)
    {
        using var csv = File.CreateText(path);
        MultiMetadata(csv, curve.Configuration);
        CsvProvenance(csv, curve.Provenance, "provenance");
        csv.WriteLine("rpm,cylinder,phase_deg,indicated_power_W,shaft_power_W,cylinder_work_J,crankcase_work_J,wall_heat_J,delivery_ratio,trapping_efficiency,scavenging_efficiency,charging_efficiency,fresh_delivered_kg,fresh_trapped_kg,fresh_exhaust_out_kg,fresh_exhaust_return_kg,residual_kg,p_peak_Pa,periodic_error");
        foreach (var r in curve.Runs)
            foreach (var member in r.Point?.Cylinders ?? [])
            {
                var p = member.Point;
                csv.WriteLine(FormattableString.Invariant($"{r.Rpm},{member.Number},{member.PhaseDeg:R},{p.IndicatedPowerW:R},{p.ShaftPowerW:R},{p.CylinderWorkJ:R},{p.CrankcaseWorkJ:R},{p.WallHeatJ:R},{p.DeliveryRatio:R},{p.TrappingEfficiency:R},{p.ScavengingEfficiency:R},{p.ChargingEfficiency:R},{p.FreshDeliveredKg:R},{p.FreshTrappedKg:R},{p.FreshExhaustOutKg:R},{p.FreshExhaustReturnKg:R},{p.ResidualMassKg:R},{p.PeakPressurePa:R},{p.PeriodicError:R}"));
            }
    }

    public static void ExportMultiComparison(string path, MultiTwoStrokeSweep a, MultiTwoStrokeSweep b,
        TwoStrokeComparisonEvidence? check)
    {
        using var csv = File.CreateText(path);
        MultiMetadata(csv, a.Configuration, "A_configuration_SI");
        MultiMetadata(csv, b.Configuration, "B_configuration_SI");
        CsvProvenance(csv, a.Provenance, "A_provenance");
        CsvProvenance(csv, b.Provenance, "B_provenance");
        if (check is not null) csv.WriteLine("# contrast=" + JsonSerializer.Serialize(check, new JsonSerializerOptions(Json) { WriteIndented = false }));
        csv.WriteLine("rpm,delta_power_W,delta_torque_Nm,delta_fresh_trapped_kg,verdict,observed_band_Nm");
        foreach (var d in MultiTwoStrokeComparison.Compare(a, b))
        {
            StudyCheck? contrast = check?.Rpm == d.Rpm ? check.Check : null;
            csv.WriteLine(FormattableString.Invariant($"{d.Rpm},{d.DeltaPowerW:R},{d.DeltaTorqueNm:R},{d.DeltaFreshTrappedKg:R},{contrast?.Verdict},{contrast?.ObservedBand:R}"));
        }
    }

    public static void ExportMultiStudy(string path, MultiTwoStrokeStudyRun study)
    {
        var plan = new MultiTwoStrokeStudy(study.Definition);
        var rank = plan.Rank(study).ToDictionary(r => r.Id);
        using var csv = File.CreateText(path);
        MultiMetadata(csv, study.Definition.Base);
        var flat = new JsonSerializerOptions(Json) { WriteIndented = false };
        csv.WriteLine("# axes=" + JsonSerializer.Serialize(study.Definition.Axes, flat));
        if (study.Check is not null) csv.WriteLine("# contrast=" + JsonSerializer.Serialize(study.Check, flat));
        csv.WriteLine("id,status,rpm,objective,shaft_power_W,shaft_torque_Nm,periodic_error,parameters,detail");
        foreach (var r in study.Rows)
        {
            var parameters = Quote(string.Join(";", plan.Grid.Variants[r.Id].Values.Select(x => x.ToString("R", CultureInfo.InvariantCulture))));
            double? score = rank.GetValueOrDefault(r.Id)?.Objective.Score;
            if (r.Curve is null) csv.WriteLine(FormattableString.Invariant($"{r.Id},{r.Status},,,,,,{parameters},{Quote(r.Error ?? "")}"));
            else foreach (var z in r.Curve.Runs)
                csv.WriteLine(FormattableString.Invariant($"{r.Id},{r.Status},{z.Rpm},{score:R},{z.Point?.ShaftPowerW:R},{z.Point?.ShaftTorqueNm:R},{z.Convergence?.Residual:R},{parameters},{Quote(z.Failure ?? "")}"));
        }
    }
}
