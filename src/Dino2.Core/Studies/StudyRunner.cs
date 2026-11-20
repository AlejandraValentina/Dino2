using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.Studies;

public enum StudyStatus { Completed, Inadmissible, NonConverged, NumericalFailure }

public sealed record StudyPoint(int Rpm, double IndicatedPowerW, double BrakePowerW, double BrakeTorqueNm,
    double VolumetricEfficiency, double FuelMassKgPerCycle, double NetWorkJ, double PeakPressurePa,
    double PeakTemperatureK, int Cycles, double PeriodicError, double MassResidualKg, double EnergyResidualJ,
    double? SpeciesResidualKg = null, double? DuctEnergyResidualJ = null, double? DuctMomentumResidualNs = null,
    bool FlameCorrelationExtrapolated = false)
{
    internal static StudyPoint From(FourStrokePoint p) => new(p.Rpm, p.IndicatedPowerW, p.BrakePowerW, p.BrakeTorqueNm,
        p.VolumetricEfficiency, p.FuelMassKg, p.NetWorkJ, p.PeakPressurePa, p.PeakTemperatureK, p.Cycles, p.PeriodicError,
        p.MassResidualKg, p.EnergyResidualJ, p.Chemistry?.MaximumSpeciesResidualKg, p.GasDynamics?.EnergyResidualJ,
        p.GasDynamics?.MomentumResidualNs, p.Chemistry?.FlameCorrelationExtrapolated ?? false);
}

public sealed record StudyEvaluation(int Id, StudyStatus Status, IReadOnlyList<StudyPoint> Points, string? Error, double Milliseconds);
public sealed record StudyProgress(StudyEvaluation Evaluation, int Evaluated, int Total);
public sealed record StudyRun(StudyDefinition Definition, IReadOnlyList<StudyEvaluation> Evaluations, bool Cancelled,
    int Workers, int ExecutedCurves, double Milliseconds);
public sealed record RankEntry(int Id, ObjectiveValue Objective);

public static class StudyRunner
{
    public static StudyRun Run(StudyPlan plan, int workers = 1, CancellationToken cancellation = default,
        IProgress<StudyProgress>? progress = null) => Run(plan, Solve, workers, cancellation, progress);

    internal static StudyRun Run(StudyPlan plan, Func<StudyVariant, CancellationToken, Action, StudyEvaluation> solve,
        int workers, CancellationToken cancellation, IProgress<StudyProgress>? progress)
    {
        if (workers is < 1 or > StudyPlan.MaximumWorkers) throw new ArgumentOutOfRangeException(nameof(workers));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var items = new StudyEvaluation?[plan.Variants.Count];
        int executed = 0, evaluated = 0;
        void Execute(IGrouping<FourStrokeConfiguration, StudyVariant> group)
        {
            cancellation.ThrowIfCancellationRequested();
            var sample = group.First();
            var result = solve(sample, cancellation, () => Interlocked.Increment(ref executed));
            foreach (var identical in group)
            {
                var row = identical.Id == sample.Id ? result : result with { Id = identical.Id, Milliseconds = 0 };
                items[row.Id] = row;
                progress?.Report(new(row, Interlocked.Increment(ref evaluated), items.Length));
            }
        }
        bool cancelled = false;
        try
        {
            var groups = plan.Variants.GroupBy(v => v.Configuration).ToArray();
            Execute(groups[0]); // Baseline first, including any exactly identical variants.
            if (workers == 1)
            {
                foreach (var group in groups.Skip(1)) Execute(group);
            }
            else
            {
                Parallel.ForEach(groups.Skip(1), new ParallelOptions
                {
                    MaxDegreeOfParallelism = workers,
                    CancellationToken = cancellation
                }, Execute);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
        return new(plan.Definition, Array.AsReadOnly(items.OfType<StudyEvaluation>().ToArray()), cancelled,
            workers, executed, clock.Elapsed.TotalMilliseconds);
    }

    internal static StudyEvaluation Solve(StudyVariant variant, CancellationToken cancellation, Action? onExecute = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            variant.Configuration.Validate();
            onExecute?.Invoke();
            var result = EngineSimulation.Run(variant.Configuration, cancellation);
            return new(variant.Id, StudyStatus.Completed, Array.AsReadOnly(result.Points.Select(StudyPoint.From).ToArray()),
                null, clock.Elapsed.TotalMilliseconds);
        }
        catch (CycleConvergenceException ex) { return Failed(StudyStatus.NonConverged, ex.Message); }
        catch (ArgumentException ex) { return Failed(StudyStatus.Inadmissible, ex.Message); }
        catch (ModelDomainException ex) { return Failed(StudyStatus.Inadmissible, ex.Message); }
        catch (ArithmeticException ex) { return Failed(StudyStatus.NumericalFailure, ex.Message); }

        StudyEvaluation Failed(StudyStatus status, string message) =>
            new(variant.Id, status, Array.Empty<StudyPoint>(), message, clock.Elapsed.TotalMilliseconds);
    }

    public static IReadOnlyList<RankEntry> Rank(StudyObjective objective, IReadOnlyList<StudyEvaluation> evaluations)
    {
        var baseline = evaluations.FirstOrDefault(p => p.Id == 0 && p.Status == StudyStatus.Completed)?.Points;
        return evaluations.Where(p => p.Id > 0 && p.Status == StudyStatus.Completed)
            .Select(p => new RankEntry(p.Id, objective.Evaluate(p.Points, baseline)))
            .Where(p => p.Objective.Eligible)
            .OrderByDescending(p => objective.Preference(p.Objective.Score!.Value))
            .ThenBy(p => p.Id).ToArray();
    }
}
