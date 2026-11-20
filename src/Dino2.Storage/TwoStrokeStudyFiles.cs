using System.Globalization;
using System.Text.Json;
using Dino2.Core.Studies;
using Dino2.Core.TwoStroke;

namespace Dino2.Storage;

public static partial class TwoStrokeFiles
{
    private const string HistoricalStudyFormat = "dino2-two-stroke-study-v1";
    private const string StudyFormat = "dino2-two-stroke-study-v2";
    private sealed record StudyDocument(string Format, string Model, TwoStrokeStudyRun Run, string? Integrity = null);

    public static void SaveStudy(string path, TwoStrokeStudyRun run)
    {
        ValidateStudy(run);
        WriteJson(path, new StudyDocument(StudyFormat, TwoStrokeConfiguration.ModelId, run, ProjectIntegrity.Digest(run, IntegrityJson)));
    }

    public static TwoStrokeStudyRun LoadStudy(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<StudyDocument>(stream, Json);
        if (document is null || document.Format is not (StudyFormat or HistoricalStudyFormat) ||
            document.Model != TwoStrokeConfiguration.ModelId || document.Run is null)
            throw new InvalidDataException("Archivo de estudio 2T incompatible.");
        ValidateStudy(document.Run);
        ProjectIntegrity.Check(document.Run, document.Integrity, document.Format == StudyFormat, IntegrityJson);
        return document.Run;
    }

    private static void ValidateStudy(TwoStrokeStudyRun run)
    {
        var plan = TwoStrokeStudy.Create(run.Definition);
        if (run.Evaluations is null || run.Evaluations.Any(row => row is null) || run.Workers is < 1 or > StudyPlan.MaximumWorkers ||
            (!run.Cancelled && run.Evaluations.Count != plan.Variants.Count) ||
            run.Evaluations.Select(row => row.Id).Distinct().Count() != run.Evaluations.Count)
            throw new InvalidDataException("Evaluaciones 2T incompletas o duplicadas.");
        foreach (var row in run.Evaluations)
        {
            if ((uint)row.Id >= plan.Variants.Count || !Enum.IsDefined(row.Status) ||
                (row.Status == StudyStatus.Completed && row.Sweep?.Complete != true))
                throw new InvalidDataException("Evaluación 2T incompatible.");
            CheckResult(plan.Variants[row.Id].Configuration, row.Sweep);
        }
        if (run.Checks?.Any(check => check is null || check.Levels is null || check.Levels.Any(l => l is null) || check.CandidateId == check.ReferenceId ||
            !run.Evaluations.Any(row => row.Id == check.CandidateId && row.Status == StudyStatus.Completed) ||
            !run.Evaluations.Any(row => row.Id == check.ReferenceId && row.Status == StudyStatus.Completed)) == true)
            throw new InvalidDataException("Contraste 2T sin evaluaciones asociadas.");
    }

    public static void ExportStudy(string path, TwoStrokeStudyRun run)
    {
        var plan = TwoStrokeStudy.Create(run.Definition);
        var baseline = run.Evaluations.FirstOrDefault(r => r.Id == 0)?.Sweep;
        using var csv = File.CreateText(path);
        var axes = plan.Definition.Axes.Select(a => TwoStrokeParameters.Get(a.Parameter).ToString());
        csv.WriteLine("model,id,status,objective,score,unit,low_margin_nm,verification_vs_a,result_revision,verification_revision,rpm,shaft_w,shaft_nm,trapped_kg,periodic_error,point_status,diagnostic," +
            string.Join(",", axes.Select(Quote)));
        foreach (var row in run.Evaluations)
        {
            var score = plan.Score(row, baseline);
            var check = run.Checks?.LastOrDefault(c => c.CandidateId == row.Id && c.ReferenceId == 0);
            var prefix = FormattableString.Invariant($"{TwoStrokeConfiguration.ModelId},{row.Id},{row.Status},{run.Definition.Objective.Kind},{score.Score:R},{Quote(run.Definition.Objective.Unit)},{score.LowMarginNm:R},{check?.Verdict},{Quote(row.Sweep?.NumericalRevision ?? (row.Sweep is null ? "" : "stage6"))},{Quote(check?.NumericalRevision ?? (check is null ? "" : "stage6"))}");
            var parameters = plan.Variants[row.Id].Values;
            var suffix = string.Join(",", parameters.Count == 0 ? axes.Select(_ => "") :
                parameters.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
            if (row.Sweep is null || row.Sweep.Runs.Count == 0) csv.WriteLine($"{prefix},,,,,,,{Quote(row.Error ?? "")},{suffix}");
            else foreach (var point in row.Sweep.Runs)
                csv.WriteLine(FormattableString.Invariant($"{prefix},{point.Rpm},{point.Point?.ShaftPowerW:R},{point.Point?.ShaftTorqueNm:R},{point.Point?.FreshTrappedKg:R},{point.Point?.PeriodicError:R},{point.Status},{Quote(point.Failure ?? "")},{suffix}"));
        }
    }

    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
}
