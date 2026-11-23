using System.Diagnostics;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Studies;

namespace Dino2.Core.TwoStroke;

public sealed record MultiTwoStrokeStudyDefinition(MultiTwoStrokeConfiguration Base, IReadOnlyList<TwoStrokeAxis> Axes,
    StudyObjective Objective);
public sealed record MultiTwoStrokeEvaluation(int Id, StudyStatus Status, MultiTwoStrokeSweep? Curve, string? Error, double Milliseconds);
public sealed record MultiTwoStrokeStudyRun(MultiTwoStrokeStudyDefinition Definition, IReadOnlyList<MultiTwoStrokeEvaluation> Rows,
    bool Cancelled, int Workers, double Milliseconds, StudyCheck? Check = null);

public sealed class MultiTwoStrokeStudy
{
    public MultiTwoStrokeStudyDefinition Definition { get; }
    public TwoStrokeStudy Grid { get; }
    public double EstimatedWork { get; }

    public MultiTwoStrokeStudy(MultiTwoStrokeStudyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Base is null) throw new ArgumentException("Falta el motor 2T base.");
        definition.Base.Validate();
        Grid = TwoStrokeStudy.Create(new(definition.Base.Cylinder, definition.Axes, definition.Objective));
        Definition = definition with { Axes = Grid.Definition.Axes };
        EstimatedWork = Grid.Variants.DistinctBy(v => v.Configuration).Sum(v => Cost(Variant(v.Id)));
        if (EstimatedWork > 160 || Grid.OperatingPoints > 36)
            throw new ArgumentException("El estudio supera el límite multicilíndrico. Reducí celdas, regímenes o variantes.");
    }

    public MultiTwoStrokeConfiguration Variant(int id) => Definition.Base with { Cylinder = Grid.Variants[id].Configuration };

    internal static double Cost(MultiTwoStrokeConfiguration c)
    {
        var basis = c.Cylinder;
        var a = basis.IntakeDuct;
        var e = basis.Expansion;
        double length = c.SharedExhaust?.Primary.LengthM ?? e.LengthM;
        int count = c.SharedExhaust?.Primary.Cells ?? e.Cells;
        double cells = c.Phases.Count * (a.Cells + count) + (c.SharedIntake?.Trunk.Cells ?? 0) +
            (c.SharedExhaust is null ? 0 : e.Cells);
        double waveStep = Math.Max(a.Cells / a.LengthM, Math.Max(count / length, e.Cells / e.LengthM));
        if (c.SharedIntake is { } intake)
            waveStep = Math.Max(waveStep, intake.Trunk.Cells / intake.Trunk.LengthM);
        return cells / 44 * Math.Max(waveStep / 86, .65 / basis.Numerics.MaximumStepDeg) * .44 / basis.Numerics.Courant *
            basis.Numerics.MaximumCycles / 100 * basis.Speeds.Values().Sum(r => 5000.0 / r);
    }

    public MultiTwoStrokeStudyRun Run(int workers = 1, CancellationToken token = default, IProgress<string>? progress = null)
    {
        if (workers is < 1 or > StudyPlan.MaximumWorkers) throw new ArgumentOutOfRangeException(nameof(workers));
        var timer = Stopwatch.StartNew();
        var rows = new MultiTwoStrokeEvaluation?[Grid.Variants.Count];
        int completed = 0;
        var groups = Grid.Variants.GroupBy(v => v.Configuration).ToArray();
        void Evaluate(IGrouping<TwoStrokeConfiguration, TwoStrokeVariant> group)
        {
            token.ThrowIfCancellationRequested();
            var variant = group.First();
            var elapsed = Stopwatch.StartNew();
            MultiTwoStrokeEvaluation evaluation;
            try
            {
                var curve = TwoStrokeSimulator.Sweep(Variant(variant.Id), false, cancellation: token);
                var failure = curve.Runs.FirstOrDefault(r => r.Point is null);
                var status = failure?.Status switch
                {
                    null => StudyStatus.Completed,
                    TwoStrokeRunStatus.NonConverged => StudyStatus.NonConverged,
                    TwoStrokeRunStatus.OutsideDomain => StudyStatus.Inadmissible,
                    _ => StudyStatus.NumericalFailure
                };
                evaluation = new(variant.Id, status, curve, failure?.Failure, elapsed.Elapsed.TotalMilliseconds);
            }
            catch (ArgumentException ex) { evaluation = new(variant.Id, StudyStatus.Inadmissible, null, ex.Message, elapsed.Elapsed.TotalMilliseconds); }
            foreach (var item in group)
            {
                rows[item.Id] = evaluation.Id == item.Id ? evaluation : evaluation with { Id = item.Id, Milliseconds = 0 };
                progress?.Report($"Estudio · {Interlocked.Increment(ref completed)}/{rows.Length} casos");
            }
        }
        bool cancelled = false;
        try
        {
            Evaluate(groups[0]);
            if (workers == 1)
                foreach (var group in groups.Skip(1)) Evaluate(group);
            else
                Parallel.ForEach(groups.Skip(1), new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = token }, Evaluate);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled = true; }
        return new(Definition, Array.AsReadOnly(rows.OfType<MultiTwoStrokeEvaluation>().ToArray()), cancelled, workers,
            timer.Elapsed.TotalMilliseconds);
    }

    public IReadOnlyList<RankEntry> Rank(MultiTwoStrokeStudyRun run)
    {
        var basis = run.Rows.FirstOrDefault(r => r.Id == 0)?.Curve;
        return run.Rows.Where(r => r.Id != 0 && r.Status == StudyStatus.Completed)
            .Select(r => new RankEntry(r.Id, Score(Definition.Objective, r.Curve, basis)))
            .Where(r => r.Objective.Eligible).OrderByDescending(r => Definition.Objective.Preference(r.Objective.Score!.Value))
            .ThenBy(r => r.Id).ToArray();
    }

    internal static ObjectiveValue Score(StudyObjective objective, MultiTwoStrokeSweep? curve, MultiTwoStrokeSweep? baseline)
    {
        StudyPoint[] Points(MultiTwoStrokeSweep? sweep) => sweep?.Complete == true
            ? sweep.Runs.Select(r =>
            {
                var p = r.Point!;
                return new StudyPoint(r.Rpm, p.IndicatedPowerW, p.ShaftPowerW, p.ShaftTorqueNm,
                    p.Cylinders.Average(z => z.Point.ChargingEfficiency), p.Cylinders.Sum(z => z.Point.FuelMassKg),
                    p.Cylinders.Sum(z => z.Point.CylinderWorkJ + z.Point.CrankcaseWorkJ),
                    p.Cylinders.Max(z => z.Point.PeakPressurePa), p.Cylinders.Max(z => z.Point.PeakTemperatureK),
                    r.Convergence!.Cycles, r.Convergence.Residual, p.Balance.MassKg, p.Balance.EnergyJ);
            }).ToArray() : [];
        return objective.Evaluate(Points(curve), Points(baseline), revolutionsPerCycle: 1);
    }

    public StudyCheck Verify(int variantId, CancellationToken token = default, IProgress<string>? progress = null)
    {
        if (variantId <= 0 || variantId >= Grid.Variants.Count) throw new ArgumentOutOfRangeException(nameof(variantId));
        return MultiTwoStrokeComparison.Verify(Variant(0), Variant(variantId), Definition.Base.Cylinder.Speeds,
            Definition.Objective, token, progress) with { CandidateId = variantId };
    }
}

public sealed record MultiTwoStrokeDifference(int Rpm, double? DeltaPowerW, double? DeltaTorqueNm,
    double? DeltaFreshTrappedKg);

public static class MultiTwoStrokeComparison
{
    public static IReadOnlyList<MultiTwoStrokeDifference> Compare(MultiTwoStrokeSweep a, MultiTwoStrokeSweep b)
    {
        var other = a.Runs.ToDictionary(p => p.Rpm);
        return b.Runs.Where(p => other.ContainsKey(p.Rpm)).Select(r =>
        {
            var p = other[r.Rpm].Point;
            var q = r.Point;
            return new MultiTwoStrokeDifference(r.Rpm, q?.ShaftPowerW - p?.ShaftPowerW, q?.ShaftTorqueNm - p?.ShaftTorqueNm,
                q?.Cylinders.Sum(c => c.Point.FreshTrappedKg) - p?.Cylinders.Sum(c => c.Point.FreshTrappedKg));
        }).ToArray();
    }

    public static StudyCheck Verify(MultiTwoStrokeConfiguration a, MultiTwoStrokeConfiguration b, int rpm,
        CancellationToken token = default, IProgress<string>? progress = null) =>
        Verify(a, b, new(rpm, rpm, 1), new(ObjectiveKind.PeakTorque), token, progress);

    internal static StudyCheck Verify(MultiTwoStrokeConfiguration a, MultiTwoStrokeConfiguration b, RpmRange speeds,
        StudyObjective objective, CancellationToken token, IProgress<string>? progress)
    {
        if (a.Phases.Count != b.Phases.Count || (a.SharedIntake is null) != (b.SharedIntake is null) ||
            (a.SharedExhaust is null) != (b.SharedExhaust is null) || a.Cylinder.Numerics.Scheme != b.Cylinder.Numerics.Scheme ||
            a.Cylinder.Numerics.Exterior != b.Cylinder.Numerics.Exterior ||
            a.Cylinder.Combustion.DurationDeg.HasValue != b.Cylinder.Combustion.DurationDeg.HasValue ||
            a.Cylinder.Combustion.ProductEquilibrium != b.Cylinder.Combustion.ProductEquilibrium)
            throw new ArgumentException("El contraste requiere igual cantidad de cilindros, topología, esquema y química.");
        var timer = Stopwatch.StartNew();
        var levels = new List<StudyLevel>();
        foreach (int scale in new[] { 1, 2, 4 })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var ca = Refine(a, b, speeds, scale);
                var cb = Refine(b, a, speeds, scale);
                if (MultiTwoStrokeStudy.Cost(ca) + (ca == cb ? 0 : MultiTwoStrokeStudy.Cost(cb)) > 700)
                    throw new ArgumentException("El contraste excede el costo permitido. Usá menos RPM o una grilla base menor.");
                progress?.Report($"Contraste ×{scale} · A");
                var left = TwoStrokeSimulator.Sweep(ca, false, cancellation: token);
                if (ca != cb) progress?.Report($"Contraste ×{scale} · B");
                var right = ca == cb ? left : TwoStrokeSimulator.Sweep(cb, false, cancellation: token);
                if (!left.Complete || !right.Complete)
                    throw new ArgumentException(left.Runs.Concat(right.Runs).First(r => r.Point is null).Failure);
                var l = MultiTwoStrokeStudy.Score(objective, left, left);
                var r = MultiTwoStrokeStudy.Score(objective, right, left);
                double? advantage = l.Score is double ls && r.Score is double rs ? objective.Preference(rs) - objective.Preference(ls) : null;
                levels.Add(new(scale, r.Score, l.Score, advantage, r.LowMarginNm, l.LowMarginNm, r.Exclusion ?? l.Exclusion));
            }
            catch (ArgumentException ex)
            {
                levels.Add(new(scale, null, null, null, null, null, ex.Message));
                break;
            }
        }
        var (verdict, band) = StudyVerification.Classify(levels);
        return new(1, 0, verdict, band, levels.AsReadOnly(), timer.Elapsed.TotalMilliseconds,
            NumericalRevision: a.Cylinder.Revision + "-network-recovery-v1");
    }

    internal static MultiTwoStrokeConfiguration Refine(MultiTwoStrokeConfiguration a, MultiTwoStrokeConfiguration b,
        RpmRange speeds, int scale)
    {
        int Cells(double length, int cells, double otherLength, int otherCells) =>
            scale * (int)Math.Ceiling(length / Math.Min(length / cells, otherLength / otherCells) - 1e-9);
        DuctGeometry Grid(DuctGeometry x, DuctGeometry y) => x with { Cells = Cells(x.LengthM, x.Cells, y.LengthM, y.Cells) };
        var x = a.Cylinder;
        var y = b.Cylinder;
        var exp = x.Expansion with { Cells = Cells(x.Expansion.LengthM, x.Expansion.Cells, y.Expansion.LengthM, y.Expansion.Cells) };
        return a with
        {
            Cylinder = x with
            {
                Speeds = speeds, IntakeDuct = Grid(x.IntakeDuct, y.IntakeDuct), Expansion = exp,
                Numerics = x.Numerics with
                {
                    Courant = Math.Min(x.Numerics.Courant, y.Numerics.Courant),
                    MaximumStepDeg = Math.Min(x.Numerics.MaximumStepDeg, y.Numerics.MaximumStepDeg) / scale,
                    PeriodicTolerance = Math.Max(1e-8, Math.Min(x.Numerics.PeriodicTolerance, y.Numerics.PeriodicTolerance) / scale),
                    MaximumCycles = Math.Max(x.Numerics.MaximumCycles, y.Numerics.MaximumCycles)
                }
            },
            SharedIntake = a.SharedIntake is { } i ? i with { Trunk = Grid(i.Trunk, b.SharedIntake!.Trunk) } : null,
            SharedExhaust = a.SharedExhaust is { } e ? e with { Primary = Grid(e.Primary, b.SharedExhaust!.Primary) } : null
        };
    }
}
