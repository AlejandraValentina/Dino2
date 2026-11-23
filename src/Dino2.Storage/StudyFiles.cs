using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dino2.Core.Studies;

namespace Dino2.Storage;

public sealed record StudyDocument(StudyRun Run, IReadOnlyList<StudyCheck> Checks);

public static class StudyFiles
{
    private sealed record Envelope(int SchemaVersion, string SolverModel, StudyDocument Study, string? Integrity = null);
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        IgnoreReadOnlyProperties = true, Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions Flat = new(Options) { WriteIndented = false };

    public static void Save(string path, StudyDocument study)
    {
        var plan = Validate(study);
        string destination = Path.GetFullPath(path), temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew))
            {
                JsonSerializer.Serialize(stream, new Envelope(2, study.Run.Provenance?.PhysicsModel ?? plan.Definition.Base.ModelIdentifier, study,
                    ProjectIntegrity.Digest(study, Flat)), Options);
                stream.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static StudyDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("El estudio supera el tamaño admitido.");
        var envelope = JsonSerializer.Deserialize<Envelope>(stream, Options) ?? throw new InvalidDataException("Estudio vacío.");
        if (envelope.SchemaVersion is not (1 or 2)) throw new InvalidDataException("Versión de estudio incompatible.");
        ProjectIntegrity.Check(envelope.Study, envelope.Integrity, envelope.SchemaVersion == 2, Flat);
        var plan = Validate(envelope.Study);
        if (envelope.SolverModel != plan.Definition.Base.ModelIdentifier &&
            envelope.SolverModel != plan.Definition.Base.Ducts?.Stage11Identifier &&
            envelope.SolverModel != plan.Definition.Base.Ducts?.Stage12Identifier)
            throw new InvalidDataException("La identidad del solver no coincide con la base del estudio.");
        var document = envelope.Study;
        return document with
        {
            Run = document.Run with
            {
                Definition = plan.Definition,
                Evaluations = Array.AsReadOnly(document.Run.Evaluations.Select(e =>
                    e with { Points = Array.AsReadOnly(e.Points.ToArray()) }).OrderBy(e => e.Id).ToArray())
            },
            Checks = Array.AsReadOnly(document.Checks.Select(c => c with
                { Levels = Array.AsReadOnly(c.Levels.ToArray()) }).ToArray())
        };
    }

    public static void Export(string path, StudyDocument study)
    {
        var plan = Validate(study);
        var data = study.Run.Evaluations.ToDictionary(p => p.Id);
        var objective = plan.Definition.Objective;
        var rank = StudyRunner.Rank(objective, study.Run.Evaluations)
            .Select((row, i) => (row.Id, Position: i + 1)).ToDictionary(p => p.Id, p => p.Position);
        var baseline = data.GetValueOrDefault(0)?.Points;
        var output = new StringBuilder();
        output.AppendLine("# solver_model=" + (study.Run.Provenance?.PhysicsModel ?? "unrecorded"));
        output.AppendLine("# provenance=" + JsonSerializer.Serialize(study.Run.Provenance, Flat));
        output.AppendLine("# study_definition=" + JsonSerializer.Serialize(plan.Definition, Flat));
        output.AppendLine("# cancellation=" + study.Run.Cancelled + "; objective_unit=" + objective.Unit + "; sampled RPM only");
        foreach (var check in study.Checks) output.AppendLine("# resolution=" + JsonSerializer.Serialize(check, Flat));
        output.AppendLine("variant,status,parameters,rank_sampled,objective,low_margin_Nm,objective_exclusion,rpm,indicated_power_W,brake_power_W,brake_torque_Nm,volumetric_efficiency,fuel_kg_per_cycle,net_work_J,peak_pressure_Pa,peak_temperature_K,cycles,periodic_error,mass_residual_kg,energy_residual_J,species_residual_kg,duct_energy_residual_J,duct_momentum_residual_Ns,flame_correlation_extrapolated,simulation_ms,error");
        foreach (var variant in plan.Variants)
        {
            var row = data.GetValueOrDefault(variant.Id);
            var value = row is { Status: StudyStatus.Completed } ? objective.Evaluate(row.Points, baseline) : null;
            string parameters = string.Join(" | ", variant.Parameters.Select(p =>
            {
                var spec = StudyParameters.Get(p.Parameter);
                return spec.Id + "=" + Number(p.Value) + " " + spec.Unit;
            }));
            string common = string.Join(",", variant.Id, row?.Status.ToString() ?? "Pending", Quote(parameters),
                rank.TryGetValue(variant.Id, out int position) ? position.ToString(CultureInfo.InvariantCulture) : "",
                Number(value?.Score), Number(value?.LowMarginNm), Quote(value?.Exclusion));
            if (row?.Status != StudyStatus.Completed)
                output.AppendLine(common + new string(',', 18) + Number(row?.Milliseconds) + "," + Quote(row?.Error));
            else foreach (var p in row.Points)
                output.AppendLine(common + "," + string.Join(",", new double?[] { p.Rpm, p.IndicatedPowerW,
                    p.BrakePowerW, p.BrakeTorqueNm, p.VolumetricEfficiency, p.FuelMassKgPerCycle, p.NetWorkJ, p.PeakPressurePa,
                    p.PeakTemperatureK, p.Cycles, p.PeriodicError, p.MassResidualKg, p.EnergyResidualJ, p.SpeciesResidualKg,
                    p.DuctEnergyResidualJ, p.DuctMomentumResidualNs }.Select(Number)) + "," +
                    p.FlameCorrelationExtrapolated + "," + Number(row.Milliseconds) + ",");
        }
        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
    }

    private static StudyPlan Validate(StudyDocument document)
    {
        if (document?.Run is not { } run || run.Evaluations is null || document.Checks is null ||
            run.Workers is < 1 or > StudyPlan.MaximumWorkers || !double.IsFinite(run.Milliseconds) || run.Milliseconds < 0)
            throw new InvalidDataException("Metadatos de ejecución inválidos.");
        var plan = StudyPlan.Create(run.Definition);
        if (run.ExecutedCurves < 0 || run.ExecutedCurves > plan.DistinctCurves ||
            run.Evaluations.Count > plan.Variants.Count || (!run.Cancelled && run.Evaluations.Count != plan.Variants.Count) ||
            run.Evaluations.Any(p => p is null) || run.Evaluations.Select(p => p.Id).Distinct().Count() != run.Evaluations.Count)
            throw new InvalidDataException("Lista de evaluaciones incompatible con la grilla.");
        var rpms = plan.Definition.Speeds.Values().ToArray();
        if (run.Provenance is { } stamp && (stamp.PhysicsModel != stamp.NumericalRevision ||
            (stamp.PhysicsModel != plan.Definition.Base.ModelIdentifier && stamp.PhysicsModel != plan.Definition.Base.Ducts?.Stage11Identifier &&
                stamp.PhysicsModel != plan.Definition.Base.Ducts?.Stage12Identifier) ||
            stamp.ResultSchema != 1 || string.IsNullOrWhiteSpace(stamp.Build) || stamp.SourceTree is not ("clean" or "dirty" or "unknown")))
            throw new InvalidDataException("La procedencia declarada no coincide con el modelo del estudio.");
        foreach (var row in run.Evaluations)
        {
            if ((uint)row.Id >= plan.Variants.Count || !Enum.IsDefined(row.Status) || row.Points is null ||
                !double.IsFinite(row.Milliseconds) || row.Milliseconds < 0)
                throw new InvalidDataException("Evaluación inválida.");
            if (row.Points.Any(p => p is null) ||
                !row.Points.Select(p => p.Rpm).SequenceEqual(rpms.Intersect(row.Points.Select(p => p.Rpm))))
                throw new InvalidDataException("Curva incompatible con los regímenes solicitados.");
            if (row.Status == StudyStatus.Completed ? row.Error is not null || row.Points.Count != rpms.Length
                : string.IsNullOrWhiteSpace(row.Error))
                throw new InvalidDataException("La evaluación no corresponde a su estado declarado.");
            if (row.Points.Count > 0)
            {
                var configuration = plan.Variants[row.Id].Configuration;
                configuration.Validate(false);
                if (row.Points.Any(p => p.Cycles < 3 || p.Cycles > configuration.Solver.MaximumCycles ||
                    p.PeriodicError < 0 || p.PeriodicError >= configuration.Solver.PeriodicTolerance ||
                    p.PeakPressurePa <= 0 || p.PeakTemperatureK is < 200 or > 6000 ||
                    Math.Abs(p.IndicatedPowerW - p.NetWorkJ * p.Rpm / 120) > 1e-8 * (1 + Math.Abs(p.IndicatedPowerW)) ||
                    Math.Abs(p.BrakePowerW - p.BrakeTorqueNm * p.Rpm * Math.PI / 30) > 1e-8 * (1 + Math.Abs(p.BrakePowerW)) ||
                    p.BrakePowerW > p.IndicatedPowerW + 1e-8 || p.VolumetricEfficiency < 0 || p.FuelMassKgPerCycle < 0 ||
                    Math.Abs(p.MassResidualKg) > 1e-8))
                    throw new InvalidDataException("La curva guardada no cumple las condiciones de periodicidad o estado.");
            }
        }
        var ids = run.Evaluations.Where(e => e.Status == StudyStatus.Completed).Select(e => e.Id).ToHashSet();
        var comparisons = new HashSet<(int, int)>();
        foreach (var check in document.Checks)
        {
            if (check is null || check.CandidateId <= 0 || check.ReferenceId == check.CandidateId ||
                !ids.Contains(check.CandidateId) || !ids.Contains(check.ReferenceId) ||
                !comparisons.Add((check.CandidateId, check.ReferenceId)) || check.Levels is null ||
                check.Levels.Count is < 1 or > 3 || check.Levels.Any(l => l is null) || !check.Levels.Select(l => l.Scale).SequenceEqual(new[] { 1, 2, 4 }.Take(check.Levels.Count)) ||
                !double.IsFinite(check.Milliseconds) || check.Milliseconds < 0 || check.Workers is < 1 or > StudyPlan.MaximumWorkers)
                throw new InvalidDataException("Contraste de resolución inválido.");
            var objective = plan.Definition.Objective;
            foreach (var level in check.Levels.Where(l => l.Advantage.HasValue))
            {
                if (level.CandidateScore is not double candidate || level.ReferenceScore is not double reference ||
                    level.Advantage != objective.Preference(candidate) - objective.Preference(reference))
                    throw new InvalidDataException("La ventaja guardada no corresponde al objetivo.");
            }
            var classified = StudyVerification.Classify(check.Levels);
            if (classified.Verdict != check.Verdict || classified.Band != check.ObservedBand)
                throw new InvalidDataException("El contraste guardado no coincide con sus tres resoluciones.");
        }
        return plan;
    }

    private static string Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
    private static string Quote(string? value) => value is null ? "" : "\"" + value.Replace("\"", "\"\"") + "\"";
}
