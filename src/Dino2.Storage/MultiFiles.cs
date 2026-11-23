using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dino2.Core;
using Dino2.Core.Multicylinder;
using Dino2.Core.Studies;

namespace Dino2.Storage;

public sealed record MultiStudyProject(MultiStudyDefinition Definition, StudyRun Run, StudyCheck? Check = null);
public sealed record MultiProject(MultiConfiguration Current, MultiConfiguration? Original = null,
    MultiResult? CurrentResult = null, MultiResult? OriginalResult = null, MultiStudyProject? Study = null,
    IReadOnlyList<MultiDifference>? Differences = null);

public static class MultiFiles
{
    private sealed record Document(int SchemaVersion, string Model, MultiProject Project, string? Integrity = null);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, RespectRequiredConstructorParameters = true,
        IgnoreReadOnlyProperties = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static void Save(string path, MultiProject project)
    {
        Validate(project);
        string destination = Path.GetFullPath(path);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream,
                    new Document(2, MultiConfiguration.ModelId, project, ProjectIntegrity.Digest(project, Options)), Options);
                stream.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static MultiProject Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<Document>(stream, Options);
        if (document is not { SchemaVersion: 1 or 2, Model: MultiConfiguration.ModelId, Project: { } project })
            throw new InvalidDataException("Archivo multicilíndrico o versión incompatible.");
        ProjectIntegrity.Check(project, document.Integrity, document.SchemaVersion == 2, Options);
        Validate(project);
        return project;
    }

    private static void Validate(MultiProject project)
    {
        if (project.Current is null) throw new InvalidDataException("Falta la configuración actual del motor.");
        project.Current.Validate();
        project.Original?.Validate();
        if (project.CurrentResult is { } b) ValidateCurve(project.Current, b);
        if (project.OriginalResult is { } a) ValidateCurve(project.Original, a);
        if (project.Differences is { } differences)
        {
            if (project.CurrentResult is null || project.OriginalResult is null)
                throw new InvalidDataException("El contraste requiere resultados A y B.");
            var raw = MultiComparison.Compare(project.OriginalResult, project.CurrentResult).ToDictionary(p => p.Rpm);
            if (differences.Count != raw.Count || differences.Any(d => d is null || !raw.TryGetValue(d.Rpm, out var r) ||
                r.PowerDifferenceW != d.PowerDifferenceW || r.TorqueDifferenceNm != d.TorqueDifferenceNm ||
                r.FillingDifference != d.FillingDifference || !ValidLevels(d.Levels, d.Verdict, d.ObservedBandW)) ||
                differences.Select(d => d.Rpm).Distinct().Count() != differences.Count)
                throw new InvalidDataException("El contraste no corresponde a las curvas A y B.");
        }
        if (project.Study is { } study)
        {
            if (study.Definition is null || study.Run?.Definition is null || study.Run.Evaluations is null)
                throw new InvalidDataException("Estudio multicilíndrico incompleto.");
            var plan = new MultiStudy(study.Definition).Grid;
            var spec = plan.Definition;
            var stored = study.Run.Definition;
            var rpm = spec.Speeds.Values().ToArray();
            var runs = study.Run.Evaluations;
            if (stored.Base != spec.Base || stored.Speeds != spec.Speeds || stored.Objective != spec.Objective ||
                stored.Axes is null || !stored.Axes.SequenceEqual(spec.Axes) ||
                runs.Any(r => !ValidEvaluation(r)) || runs.Select(r => r.Id).Distinct().Count() != runs.Count ||
                (!study.Run.Cancelled && runs.Count != plan.Variants.Count) ||
                study.Run.Workers is < 1 or > StudyPlan.MaximumWorkers)
                throw new InvalidDataException("Los resultados guardados no corresponden al plan multicilíndrico.");
            if (study.Check is { } check && (check.CandidateId <= 0 || check.CandidateId >= plan.Variants.Count ||
                check.ReferenceId != 0 || !ValidLevels(check.Levels, check.Verdict, check.ObservedBand)))
                throw new InvalidDataException("El contraste guardado no corresponde al estudio.");
            bool ValidEvaluation(StudyEvaluation r) => r is not null && r.Id >= 0 && r.Id < plan.Variants.Count &&
                r.Points is not null && r.Points.All(p => p is not null && rpm.Contains(p.Rpm)) &&
                r.Points.Select(p => p.Rpm).Distinct().Count() == r.Points.Count &&
                (r.Status != StudyStatus.Completed || r.Points.Select(p => p.Rpm).SequenceEqual(rpm));
        }
    }

    private static bool ValidLevels(IReadOnlyList<StudyLevel> levels, StudyVerdict verdict, double? band) =>
        levels is not null && levels.Count <= 3 && levels.All(l => l is not null) &&
        levels.Select(l => l.Scale).SequenceEqual(new[] { 1, 2, 4 }.Take(levels.Count)) &&
        StudyVerification.Classify(levels) == (verdict, band);

    private static void ValidateCurve(MultiConfiguration? configuration, MultiResult result)
    {
        if (configuration is null || result.Configuration != configuration || result.Points is null ||
            result.Points.Any(p => p is null) ||
            !configuration.Cylinder.Speeds.Values().SequenceEqual(result.Points.Select(p => p.Rpm)))
            throw new InvalidDataException("La curva guardada no corresponde a la configuración declarada.");
        foreach (var p in result.Points)
        {
            if (p.Cylinders is null || p.Cycle is null ||
                (p.Status == MultiStatus.Converged
                    ? p.Metrics is null || p.Balance is null || p.Cylinders.Count != configuration.Phases.Count ||
                      p.Cycle.Count is < 2 or > 800 || p.Cycle[0].CrankAngleDeg != 0 || p.Cycle[^1].CrankAngleDeg != 720 ||
                      p.Cylinders.Select((c, i) => c is not null && c.CylinderNumber == i + 1 &&
                          c.Trace is not null && c.Trace.Count == p.Cycle.Count).Any(valid => !valid)
                    : p.Metrics is not null || p.Balance is not null || p.Cylinders.Count != 0 || p.Cycle.Count != 0))
                throw new InvalidDataException("Un punto guardado contiene diagnósticos o trazas incompatibles.");
            if (p.Metrics is not { } metrics || p.Balance is not { } balance) continue;
            double omega = p.Rpm * Math.PI / 30, frequency = p.Rpm / 120.0, displacement = configuration.DisplacementM3;
            if (!Close(metrics.IndicatedPowerW, metrics.NetWorkJ * frequency) ||
                !Close(metrics.BrakePowerW, (metrics.NetWorkJ - metrics.FrictionWorkJ) * frequency) ||
                !Close(metrics.IndicatedPowerW, metrics.IndicatedTorqueNm * omega) ||
                !Close(metrics.BrakePowerW, metrics.BrakeTorqueNm * omega) ||
                !Close(metrics.ImepPa, metrics.NetWorkJ / displacement) ||
                !Close(metrics.BmepPa, (metrics.NetWorkJ - metrics.FrictionWorkJ) / displacement) ||
                !Close(metrics.IndicatedPowerW, p.Cylinders.Sum(c => c.IndicatedPowerW)) ||
                !Close(metrics.BrakePowerW, p.Cylinders.Sum(c => c.BrakePowerW)) ||
                metrics.VolumetricEfficiency < 0 || metrics.FuelKgPerCycle < 0 || metrics.FrictionWorkJ < 0 ||
                !(p.PeriodicError >= 0 && p.PeriodicError <= configuration.Cylinder.Solver.PeriodicTolerance) ||
                Math.Abs(balance.TotalEnergyJ) > 1e-6 * (1 + Math.Abs(metrics.NetWorkJ) + metrics.FuelKgPerCycle * 45e6) ||
                Math.Abs(balance.MassKg) > 1e-8 || p.Cylinders.Any(c => c.PeakPressurePa <= 0 || c.PeakTemperatureK is < 200 or > 6000))
                throw new InvalidDataException("La curva 4T contiene magnitudes incompatibles con el trabajo, la periodicidad o el estado del motor.");
        }
    }

    private static bool Close(double a, double b) => double.IsFinite(a) && double.IsFinite(b) &&
        Math.Abs(a - b) <= 1e-8 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    private static void Metadata(TextWriter writer, MultiConfiguration configuration, string name = "configuration_SI")
    {
        writer.WriteLine("# model=" + MultiConfiguration.ModelId);
        writer.WriteLine("# evidence=model prediction; compact mixed junctions; no experimental validation");
        writer.WriteLine($"# {name}=" + JsonSerializer.Serialize(configuration, Options));
    }

    private static void Values(TextWriter writer, params double?[] fields) =>
        writer.WriteLine(string.Join(",", fields.Select(v => v?.ToString("R", CultureInfo.InvariantCulture) ?? "")));

    public static void Export(string path, MultiResult result)
    {
        using var writer = File.CreateText(path);
        Metadata(writer, result.Configuration);
        writer.WriteLine("# provenance=" + JsonSerializer.Serialize(result.Provenance, Options));
        writer.WriteLine("status,rpm,indicated_power_W,brake_power_W,indicated_torque_Nm,brake_torque_Nm,imep_Pa,bmep_Pa,volumetric_efficiency,work_J,friction_J,net_intake_fuel_kg,mass_residual_kg,max_element_residual_kg,energy_residual_J,nitrogen_residual_kg,max_interface_species_residual_kg,max_interface_energy_residual_J,runner_momentum_residual_Ns,cycles,periodic_error,elapsed_ms,accepted_steps,rejected_steps,cell_evaluations");
        foreach (var p in result.Points)
        {
            var m = p.Metrics;
            writer.Write(p.Status + ",");
            Values(writer, p.Rpm, m?.IndicatedPowerW, m?.BrakePowerW, m?.IndicatedTorqueNm, m?.BrakeTorqueNm,
                m?.ImepPa, m?.BmepPa, m?.VolumetricEfficiency, m?.NetWorkJ, m?.FrictionWorkJ, m?.FuelKgPerCycle,
                p.Balance?.MassKg, p.Balance?.MaximumElementKg, p.Balance?.TotalEnergyJ,
                p.Balance?.NitrogenResidualKg, p.Balance?.MaximumInterfaceSpeciesResidualKg, p.Balance?.MaximumInterfaceEnergyResidualJ,
                p.Balance?.MaximumRunnerMomentumNs, p.Cycles, p.PeriodicError,
                p.ElapsedMilliseconds, p.AcceptedSteps, p.RejectedSteps, p.CellEvaluations);
        }
    }

    public static void ExportTrace(string path, MultiResult result, int rpm)
    {
        var point = result.Points.Single(p => p.Rpm == rpm);
        if (point.Status != MultiStatus.Converged) throw new ArgumentException("El ciclo requiere una solución periódica.");
        using var writer = File.CreateText(path);
        Metadata(writer, result.Configuration);
        writer.WriteLine("# provenance=" + JsonSerializer.Serialize(result.Provenance, Options));
        writer.WriteLine($"# rpm={rpm}");
        writer.WriteLine("cylinder,firing_offset_deg,crank_angle_deg,cylinder_pressure_Pa,cylinder_temperature_K,cylinder_mass_kg,volume_m3,intake_port_Pa,exhaust_port_Pa,intake_kg_s,exhaust_kg_s,intake_plenum_Pa,exhaust_plenum_Pa,intake_trunk_Pa,exhaust_trunk_Pa,total_indicated_torque_Nm,cylinder_work_J,fuel_kg,O2_kg,N2_kg,CO2_kg,H2O_kg,CO_kg,H2_kg");
        foreach (var cylinder in point.Cylinders)
            for (int i = 0; i < point.Cycle.Count; i++)
            {
                var s = cylinder.Trace[i];
                var m = point.Cycle[i];
                var c = s.Composition;
                Values(writer, cylinder.CylinderNumber, cylinder.FiringOffsetDeg, m.CrankAngleDeg, s.PressurePa,
                    s.TemperatureK, s.MassKg, s.VolumeM3, s.IntakePortPressurePa, s.ExhaustPortPressurePa, s.IntakeFlowKgPerS,
                    s.ExhaustFlowKgPerS, m.IntakeJunctionPa, m.ExhaustJunctionPa, m.IntakeTrunkPa, m.ExhaustTrunkPa,
                    m.IndicatedTorqueNm, s.WorkJ, c.FuelKg, c.OxygenKg, c.NitrogenKg, c.CarbonDioxideKg,
                    c.WaterKg, c.CarbonMonoxideKg, c.HydrogenKg);
            }
    }

    public static void ExportCylinders(string path, MultiResult result)
    {
        using var writer = File.CreateText(path);
        Metadata(writer, result.Configuration);
        writer.WriteLine("rpm,cylinder,firing_offset_deg,net_work_J,pumping_work_J,friction_work_J,indicated_power_W,brake_power_W,gross_intake_kg,net_intake_kg,fresh_air_at_intake_close_kg,residual_at_spark,peak_pressure_Pa,peak_temperature_K,periodic_error");
        foreach (var p in result.Points)
            foreach (var c in p.Cylinders)
                Values(writer, p.Rpm, c.CylinderNumber, c.FiringOffsetDeg, c.NetWorkJ, c.PumpingWorkJ, c.FrictionWorkJ,
                    c.IndicatedPowerW, c.BrakePowerW, c.GrossIntakeMassKg, c.NetIntakeMassKg,
                    c.FreshAirAtIntakeCloseKg, c.ResidualAtSpark, c.PeakPressurePa, c.PeakTemperatureK, c.PeriodicError);
    }

    public static void ExportComparison(string path, MultiResult a, MultiResult b, IReadOnlyList<MultiDifference> rows)
    {
        using var writer = File.CreateText(path);
        Metadata(writer, a.Configuration, "A_configuration_SI");
        Metadata(writer, b.Configuration, "B_configuration_SI");
        writer.WriteLine("# differences=B-A; classification uses three paired meshes; numerical band is empirical");
        foreach (var row in rows.Where(p => p.Levels.Count > 0))
            writer.WriteLine("# contrast=" + JsonSerializer.Serialize(new { row.Rpm, row.Levels }, Options));
        writer.WriteLine("verdict,rpm,delta_brake_power_W,delta_brake_torque_Nm,delta_filling,observed_power_band_W");
        foreach (var row in rows)
        {
            writer.Write(row.Verdict + ",");
            Values(writer, row.Rpm, row.PowerDifferenceW, row.TorqueDifferenceNm, row.FillingDifference, row.ObservedBandW);
        }
    }

    public static void ExportStudy(string path, MultiStudyProject study)
    {
        using var writer = File.CreateText(path);
        Metadata(writer, study.Definition.Base);
        writer.WriteLine("# study=" + JsonSerializer.Serialize(study.Definition, Options));
        if (study.Check is not null) writer.WriteLine("# contrast=" + JsonSerializer.Serialize(study.Check, Options));
        var plan = new MultiStudy(study.Definition);
        var rank = StudyRunner.Rank(study.Definition.Objective, study.Run.Evaluations).ToDictionary(r => r.Id);
        foreach (var row in study.Run.Evaluations)
            writer.WriteLine("# variant=" + JsonSerializer.Serialize(new { row.Id, plan.Grid.Variants[row.Id].Parameters, row.Error }, Options));
        writer.WriteLine("status,id,rpm,objective_score,indicated_power_W,brake_power_W,brake_torque_Nm,volumetric_efficiency,cycles,periodic_error");
        foreach (var row in study.Run.Evaluations)
        {
            double? score = rank.GetValueOrDefault(row.Id)?.Objective.Score;
            foreach (var p in row.Points)
            {
                writer.Write(row.Status + ",");
                Values(writer, row.Id, p.Rpm, score, p.IndicatedPowerW, p.BrakePowerW, p.BrakeTorqueNm,
                    p.VolumetricEfficiency, p.Cycles, p.PeriodicError);
            }
            if (row.Points.Count > 0) continue;
            writer.Write(row.Status + ",");
            Values(writer, row.Id, null, score, null, null, null, null, null, null);
        }
    }
}
