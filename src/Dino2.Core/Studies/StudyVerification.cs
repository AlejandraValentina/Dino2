using Dino2.Core.FourStroke;

namespace Dino2.Core.Studies;

public enum StudyVerdict { Superior, Inferior, Indistinguishable, DiscretizationSensitive, Unavailable }

public sealed record StudyLevel(int Scale, double? CandidateScore, double? ReferenceScore,
    double? Advantage, double? CandidateLowMarginNm, double? ReferenceLowMarginNm, string? Error);
public sealed record StudyCheck(int CandidateId, int ReferenceId, StudyVerdict Verdict, double? ObservedBand,
    IReadOnlyList<StudyLevel> Levels, double Milliseconds, int Workers = 1);

public static class StudyVerification
{
    public static StudyCheck Compare(StudyPlan plan, StudyRun run, int candidate, int reference,
        CancellationToken cancellation = default, IProgress<string>? progress = null, int? workers = null)
    {
        if (candidate <= 0 || reference == candidate || (uint)candidate >= plan.Variants.Count ||
            (uint)reference >= plan.Variants.Count)
            throw new ArgumentException("Elegí una variante y una referencia distinta.");
        if (run.Definition.Base != plan.Definition.Base || run.Definition.Speeds != plan.Definition.Speeds ||
            run.Definition.Objective != plan.Definition.Objective || !run.Definition.Axes.SequenceEqual(plan.Definition.Axes))
            throw new ArgumentException("Los resultados no corresponden al plan indicado.");
        int concurrency = workers ?? run.Workers;
        if (concurrency is < 1 or > StudyPlan.MaximumWorkers) throw new ArgumentOutOfRangeException(nameof(workers));
        var objective = plan.Definition.Objective;
        int[] ids = objective.Kind == ObjectiveKind.HighTorqueLimitedLowLoss
            ? new[] { candidate, reference, 0 }.Distinct().ToArray() : [candidate, reference];
        if (ids.Length * 2 * plan.Definition.Speeds.Values().Count() > StudyPlan.MaximumVerificationPoints)
            throw new ArgumentException("El contraste supera 72 puntos RPM adicionales. Reducí la banda de estudio.");
        var coarse = ids.ToDictionary(id => id, id =>
            run.Evaluations.SingleOrDefault(p => p.Id == id && p.Status == StudyStatus.Completed)
            ?? throw new ArgumentException("El contraste necesita curvas convergidas de los casos seleccionados."));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var levels = new List<StudyLevel>(3);
        var cache = new Dictionary<FourStrokeConfiguration, StudyEvaluation>();
        foreach (var (id, evaluation) in coarse) cache.TryAdd(plan.Variants[id].Configuration, evaluation);
        double requestedWork = ids.Select(id => plan.Variants[id].Configuration).Distinct().Sum(StudyPlan.DuctWork) * 20;
        if (requestedWork > StudyPlan.MaximumVerificationWork)
            throw new ArgumentException("El costo 1D de los refinamientos supera el límite. Acotá regímenes o usá una malla base menor.");
        foreach (int scale in new[] { 1, 2, 4 })
        {
            cancellation.ThrowIfCancellationRequested();
            var variants = new StudyVariant[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var variant = plan.Variants[ids[i]];
                if (scale != 1)
                {
                    try { variant = variant with { Configuration = Refine(variant.Configuration, scale) }; }
                    catch (ArgumentException ex) { return Unavailable(scale, ex.Message); }
                }
                variants[i] = variant;
            }
            var missing = variants.Where(v => !cache.ContainsKey(v.Configuration)).DistinctBy(v => v.Configuration).ToArray();
            var evaluations = new StudyEvaluation[missing.Length];
            if (missing.Length > 0)
                progress?.Report($"Resolución ×{scale} · {missing.Length} curvas nuevas");
            if (concurrency > 1 && missing.Length > 1)
                Parallel.For(0, missing.Length, new ParallelOptions
                {
                    MaxDegreeOfParallelism = concurrency,
                    CancellationToken = cancellation
                }, i => evaluations[i] = StudyRunner.Solve(missing[i], cancellation));
            else
                for (int i = 0; i < missing.Length; i++)
                    evaluations[i] = StudyRunner.Solve(missing[i], cancellation);

            for (int i = 0; i < missing.Length; i++) cache.Add(missing[i].Configuration, evaluations[i]);
            var rows = new Dictionary<int, StudyEvaluation>();
            foreach (var variant in variants)
            {
                var row = cache[variant.Configuration];
                if (row.Status != StudyStatus.Completed) return Unavailable(scale, $"{row.Status}: {row.Error}");
                rows.Add(variant.Id, row);
            }
            rows.TryGetValue(0, out var baseRow);
            var a = objective.Evaluate(rows[reference].Points, baseRow?.Points);
            var b = objective.Evaluate(rows[candidate].Points, baseRow?.Points);
            double? advantage = a.Score is double av && b.Score is double bv
                ? objective.Preference(bv) - objective.Preference(av) : null;
            levels.Add(new(scale, b.Score, a.Score, advantage, b.LowMarginNm, a.LowMarginNm, b.Exclusion ?? a.Exclusion));
        }

        var (verdict, band) = Classify(levels);
        return new(candidate, reference, verdict, band, levels.AsReadOnly(), clock.Elapsed.TotalMilliseconds, concurrency);

        StudyCheck Unavailable(int scale, string error)
        {
            levels.Add(new(scale, null, null, null, null, null, error));
            return new(candidate, reference, StudyVerdict.Unavailable, null, levels.AsReadOnly(), clock.Elapsed.TotalMilliseconds, concurrency);
        }
    }

    private static FourStrokeConfiguration Refine(FourStrokeConfiguration c, int scale)
    {
        var solver = c.Solver with { MaximumStepDeg = c.Solver.MaximumStepDeg / scale,
            RelativeTolerance = Math.Max(1e-10, c.Solver.RelativeTolerance / (scale * scale)),
            PeriodicTolerance = Math.Max(1e-9, c.Solver.PeriodicTolerance / (scale * scale)) };
        if (solver.MaximumStepDeg < .025)
            throw new ArgumentException("El paso angular necesario queda por debajo del límite del solver.");
        if (c.Ducts is { } d)
        {
            if (d.Intake.Cells * scale > 256 || d.Exhaust.Cells * scale > 256)
                throw new ArgumentException("El refinamiento supera 256 celdas por conducto.");
            c = c with { Ducts = d with { Intake = d.Intake with { Cells = d.Intake.Cells * scale },
                Exhaust = d.Exhaust with { Cells = d.Exhaust.Cells * scale } } };
        }
        return c with { Solver = solver };
    }

    public static (StudyVerdict Verdict, double? Band) Classify(IReadOnlyList<StudyLevel> levels)
    {
        if (levels.Count != 3 || levels.Any(l => l.Advantage is not double d || !double.IsFinite(d)))
            return (StudyVerdict.Unavailable, null);
        var (a, b, c) = (levels[0].Advantage!.Value, levels[1].Advantage!.Value, levels[2].Advantage!.Value);
        double first = Math.Abs(b - a), last = Math.Abs(c - b), band = 2 * Math.Max(first, last);
        if (levels.Any(l => l.Error is not null)) return (StudyVerdict.DiscretizationSensitive, band);
        // Nonzero constraint margins are subject to the same empirical resolution check.
        foreach (var margins in new[] { levels.Select(l => l.CandidateLowMarginNm).ToArray(),
                                       levels.Select(l => l.ReferenceLowMarginNm).ToArray() })
        {
            if (margins.All(v => v is null)) continue;
            if (margins.Any(v => v is not double value || !double.IsFinite(value))) return (StudyVerdict.Unavailable, null);
            double x = margins[0]!.Value, y = margins[1]!.Value, z = margins[2]!.Value;
            double variation = 2 * Math.Max(Math.Abs(y - x), Math.Abs(z - y));
            if (z < variation) return (StudyVerdict.DiscretizationSensitive, band);
        }
        if (a == 0 && b == 0 && c == 0) return (StudyVerdict.Indistinguishable, band);
        if (Math.Sign(a) != Math.Sign(b) || Math.Sign(b) != Math.Sign(c) || last > first)
            return (StudyVerdict.DiscretizationSensitive, band);
        if (Math.Abs(c) <= band) return (StudyVerdict.Indistinguishable, band);
        return (c > 0 ? StudyVerdict.Superior : StudyVerdict.Inferior, band);
    }
}
