using System.Diagnostics;
using Dino2.Core.Studies;

namespace Dino2.Core.TwoStroke;

public enum TwoStrokeParameter
{
    SparkAdvance, Compression, CrankcaseVolume, TransferRoof, TransferHeight, TransferWidth, TransferThroat,
    ExhaustRoof, ExhaustHeight, ExhaustWidth, ExhaustThroat, ReedStiffness, ReedLift, ReedOpeningPressure,
    IntakeLength, IntakeDiameter, HeaderLength, DiffuserLength, BellyLength, BellyDiameter, ConeLength, StingerLength, StingerDiameter
}

public sealed record TwoStrokeParameterSpec(TwoStrokeParameter Id, string Label, string Unit,
    Func<TwoStrokeConfiguration, double> Read, Func<TwoStrokeConfiguration, double, TwoStrokeConfiguration> Set)
{
    public override string ToString() => $"{Label} [{Unit}]";
}

public static class TwoStrokeParameters
{
    public static IReadOnlyList<TwoStrokeParameterSpec> All { get; } = Array.AsReadOnly<TwoStrokeParameterSpec>(
    [
        new(TwoStrokeParameter.SparkAdvance, "Avance", "°", c => c.Combustion.AdvanceDeg, (c,v) => c with { Combustion = c.Combustion with { AdvanceDeg = v } }),
        new(TwoStrokeParameter.Compression, "Compresión geométrica", "1", c => c.Geometry.CompressionRatio, (c,v) => c with { Geometry = c.Geometry with { CompressionRatio = v } }),
        new(TwoStrokeParameter.CrankcaseVolume, "Volumen mínimo del cárter", "cm³", c => c.CrankcaseMinimumM3 * 1e6, (c,v) => c with { CrankcaseMinimumM3 = v / 1e6 }),
        new(TwoStrokeParameter.TransferRoof, "Techo de transfers desde PMS", "mm", c => c.Transfer.RoofM * 1e3, (c,v) => c with { Transfer = c.Transfer with { RoofM = v / 1e3 } }),
        new(TwoStrokeParameter.TransferHeight, "Altura de transfers", "mm", c => c.Transfer.HeightM * 1e3, (c,v) => c with { Transfer = c.Transfer with { HeightM = v / 1e3 } }),
        new(TwoStrokeParameter.TransferWidth, "Ancho agregado de transfers", "mm", c => c.Transfer.WidthM * 1e3, (c,v) => c with { Transfer = c.Transfer with { WidthM = v / 1e3 } }),
        new(TwoStrokeParameter.TransferThroat, "Garganta de transfers", "mm²", c => c.Transfer.ThroatM2 * 1e6, (c,v) => c with { Transfer = c.Transfer with { ThroatM2 = v / 1e6 } }),
        new(TwoStrokeParameter.ExhaustRoof, "Techo de escape desde PMS", "mm", c => c.Exhaust.RoofM * 1e3, (c,v) => c with { Exhaust = c.Exhaust with { RoofM = v / 1e3 } }),
        new(TwoStrokeParameter.ExhaustHeight, "Altura de escape", "mm", c => c.Exhaust.HeightM * 1e3, (c,v) => c with { Exhaust = c.Exhaust with { HeightM = v / 1e3 } }),
        new(TwoStrokeParameter.ExhaustWidth, "Ancho de escape", "mm", c => c.Exhaust.WidthM * 1e3, (c,v) => c with { Exhaust = c.Exhaust with { WidthM = v / 1e3 } }),
        new(TwoStrokeParameter.ExhaustThroat, "Garganta de escape", "mm²", c => c.Exhaust.ThroatM2 * 1e6, (c,v) => c with { Exhaust = c.Exhaust with { ThroatM2 = v / 1e6 } }),
        new(TwoStrokeParameter.ReedStiffness, "Rigidez de láminas", "N/m", c => c.Reed.StiffnessNPerM, (c,v) => c with { Reed = c.Reed with { StiffnessNPerM = v } }),
        new(TwoStrokeParameter.ReedLift, "Tope de láminas", "mm", c => c.Reed.LimitM * 1e3, (c,v) => c with { Reed = c.Reed with { LimitM = v / 1e3 } }),
        new(TwoStrokeParameter.ReedOpeningPressure, "Umbral de láminas", "Pa", c => c.Reed.OpeningPressurePa, (c,v) => c with { Reed = c.Reed with { OpeningPressurePa = v } }),
        new(TwoStrokeParameter.IntakeLength, "Longitud de admisión", "mm", c => c.IntakeDuct.LengthM * 1e3, (c,v) => c with { IntakeDuct = c.IntakeDuct with { LengthM = v / 1e3 } }),
        new(TwoStrokeParameter.IntakeDiameter, "Diámetro interior de admisión (lado láminas)", "mm", c => c.IntakeDuct.ValveDiameterM * 1e3, (c,v) => c with { IntakeDuct = c.IntakeDuct with { ValveDiameterM = v / 1e3 } }),
        new(TwoStrokeParameter.HeaderLength, "Longitud del colector", "mm", c => c.Expansion.HeaderM * 1e3, (c,v) => c with { Expansion = c.Expansion with { HeaderM = v / 1e3 } }),
        new(TwoStrokeParameter.DiffuserLength, "Longitud del difusor", "mm", c => c.Expansion.DiffuserM * 1e3, (c,v) => c with { Expansion = c.Expansion with { DiffuserM = v / 1e3 } }),
        new(TwoStrokeParameter.BellyLength, "Longitud del tramo central", "mm", c => c.Expansion.BellyM * 1e3, (c,v) => c with { Expansion = c.Expansion with { BellyM = v / 1e3 } }),
        new(TwoStrokeParameter.BellyDiameter, "Diámetro del tramo central", "mm", c => c.Expansion.BellyDiameterM * 1e3, (c,v) => c with { Expansion = c.Expansion with { BellyDiameterM = v / 1e3 } }),
        new(TwoStrokeParameter.ConeLength, "Longitud del cono convergente", "mm", c => c.Expansion.ConeM * 1e3, (c,v) => c with { Expansion = c.Expansion with { ConeM = v / 1e3 } }),
        new(TwoStrokeParameter.StingerLength, "Longitud de salida", "mm", c => c.Expansion.StingerM * 1e3, (c,v) => c with { Expansion = c.Expansion with { StingerM = v / 1e3 } }),
        new(TwoStrokeParameter.StingerDiameter, "Diámetro de salida", "mm", c => c.Expansion.StingerDiameterM * 1e3, (c,v) => c with { Expansion = c.Expansion with { StingerDiameterM = v / 1e3 } })
    ]);

    public static TwoStrokeParameterSpec Get(TwoStrokeParameter parameter) =>
        All.FirstOrDefault(s => s.Id == parameter) ?? throw new ArgumentException("Parámetro 2T desconocido.");
}

public sealed record TwoStrokeAxis(TwoStrokeParameter Parameter, double Start, double End, double Step);
public sealed record TwoStrokeStudyDefinition(TwoStrokeConfiguration Base, IReadOnlyList<TwoStrokeAxis> Axes, StudyObjective Objective);
public sealed record TwoStrokeVariant(int Id, TwoStrokeConfiguration Configuration, IReadOnlyList<double> Values);
public sealed record TwoStrokeEvaluation(int Id, StudyStatus Status, TwoStrokeSweep? Sweep, string? Error, double Milliseconds);
public sealed record TwoStrokeStudyRun(TwoStrokeStudyDefinition Definition, IReadOnlyList<TwoStrokeEvaluation> Evaluations,
    bool Cancelled, double Milliseconds, int Workers, IReadOnlyList<StudyCheck>? Checks = null);
public sealed record TwoStrokeStudyProgress(TwoStrokeEvaluation Evaluation, int Evaluated, int Total);

public sealed class TwoStrokeStudy
{
    public const int MaximumPoints = 96;
    public TwoStrokeStudyDefinition Definition { get; }
    public IReadOnlyList<TwoStrokeVariant> Variants { get; }
    public int OperatingPoints { get; }

    private TwoStrokeStudy(TwoStrokeStudyDefinition definition, List<TwoStrokeVariant> variants, int rpmCount)
    {
        Definition = definition;
        Variants = variants.AsReadOnly();
        OperatingPoints = variants.Select(v => v.Configuration).Distinct().Count() * rpmCount;
    }

    public static TwoStrokeStudy Create(TwoStrokeStudyDefinition definition)
    {
        if (definition?.Base is null || definition.Objective is null || definition.Axes is null ||
            definition.Axes.Count is < 1 or > 2 || definition.Axes.Any(a => a is null) ||
            definition.Axes.Select(a => a.Parameter).Distinct().Count() != definition.Axes.Count)
            throw new ArgumentException("Elegí uno o dos parámetros 2T distintos.");
        var basis = definition.Base;
        basis.Validate();
        var speeds = basis.Speeds.Values().ToArray();
        if (speeds[^1] != basis.Speeds.End) throw new ArgumentException("El paso debe alcanzar la última RPM.");
        definition.Objective.Validate(speeds, true);
        var axes = definition.Axes.ToArray();
        var values = axes.Select(Values).ToArray();
        int count = values.Aggregate(1, (total, axis) => total * axis.Length);
        if (count >= MaximumPoints) throw new ArgumentException("Demasiadas variantes 2T.");
        var variants = new List<TwoStrokeVariant> { new(0, basis, Array.Empty<double>()) };
        var selection = new double[axes.Length];
        void Generate(TwoStrokeConfiguration variant, int axis)
        {
            if (axis == axes.Length)
            {
                variant = MatchSpacing(basis, variant);
                variants.Add(new(variants.Count, variant, Array.AsReadOnly((double[])selection.Clone())));
                return;
            }
            var spec = TwoStrokeParameters.Get(axes[axis].Parameter);
            foreach (double value in values[axis])
            {
                selection[axis] = value;
                Generate(value == spec.Read(variant) ? variant : spec.Set(variant, value), axis + 1);
            }
        }

        Generate(basis, 0);
        var plan = new TwoStrokeStudy(definition with { Axes = Array.AsReadOnly(axes) }, variants, speeds.Length);
        if (plan.OperatingPoints > MaximumPoints ||
            variants.Select(v => v.Configuration).Distinct().Where(Admissible).Sum(Work) > 400)
            throw new ArgumentException("El estudio supera el límite de regímenes o costo 1D. Reducí la malla o el rango.");
        return plan;
    }

    private static double[] Values(TwoStrokeAxis axis)
    {
        var spec = TwoStrokeParameters.Get(axis.Parameter);
        double intervals = (axis.End - axis.Start) / axis.Step;
        double count = Math.Round(intervals);
        if (!double.IsFinite(axis.Start) || !double.IsFinite(axis.End) || !double.IsFinite(axis.Step) ||
            axis.Step <= 0 || axis.End < axis.Start || !double.IsFinite(count) || count >= MaximumPoints ||
            Math.Abs(intervals - count) > 1e-8 || (axis.End > axis.Start && (count == 0 || axis.Start + axis.Step == axis.Start)))
            throw new ArgumentException($"Grilla inválida de {spec}. El incremento debe alcanzar el final.");
        return Enumerable.Range(0, (int)count + 1).Select(i => i == count ? axis.End : axis.Start + i * axis.Step).ToArray();
    }

    private static TwoStrokeConfiguration MatchSpacing(TwoStrokeConfiguration basis, TwoStrokeConfiguration variant)
    {
        static int Match(int cells, double length, double initial, int minimum)
        {
            double desired = Math.Round(cells * length / initial);
            return double.IsFinite(desired) && desired is >= 0 and <= 256 ? Math.Max(minimum, (int)desired) : 257;
        }
        return variant with
        {
            IntakeDuct = variant.IntakeDuct with { Cells = Match(basis.IntakeDuct.Cells, variant.IntakeDuct.LengthM, basis.IntakeDuct.LengthM, 8) },
            Expansion = variant.Expansion with { Cells = Match(basis.Expansion.Cells, variant.Expansion.LengthM, basis.Expansion.LengthM, 12) }
        };
    }

    private static bool Admissible(TwoStrokeConfiguration configuration)
    {
        try { configuration.Validate(); return true; }
        catch (ArgumentException) { return false; }
    }

    private static double Work(TwoStrokeConfiguration c) =>
        (c.IntakeDuct.Cells + c.Expansion.Cells) / 44.0 *
        Math.Max(c.IntakeDuct.Cells / c.IntakeDuct.LengthM, c.Expansion.Cells / c.Expansion.LengthM) / 86 *
        Math.Max(1, .65 / c.Numerics.MaximumStepDeg) * .44 / c.Numerics.Courant *
        c.Numerics.MaximumCycles / 100.0 * c.Speeds.Values().Sum(r => 5000.0 / r);

    public TwoStrokeStudyRun Run(int workers = 1, CancellationToken token = default, IProgress<TwoStrokeStudyProgress>? progress = null)
    {
        if (workers is < 1 or > StudyPlan.MaximumWorkers) throw new ArgumentOutOfRangeException(nameof(workers));
        var clock = Stopwatch.StartNew();
        var rows = new TwoStrokeEvaluation?[Variants.Count];
        var groups = Variants.GroupBy(v => v.Configuration).ToArray();
        int evaluated = 0;
        void Execute(IGrouping<TwoStrokeConfiguration, TwoStrokeVariant> group)
        {
            token.ThrowIfCancellationRequested();
            var row = Solve(group.First(), token);
            foreach (var variant in group)
            {
                var result = variant.Id == row.Id ? row : row with { Id = variant.Id, Milliseconds = 0 };
                rows[variant.Id] = result;
                progress?.Report(new(result, Interlocked.Increment(ref evaluated), rows.Length));
            }
        }

        bool cancelled = false;
        try
        {
            Execute(groups[0]);
            if (workers == 1)
                foreach (var group in groups.Skip(1)) Execute(group);
            else
                Parallel.ForEach(groups.Skip(1), new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = token }, Execute);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled = true; }
        return new(Definition, Array.AsReadOnly(rows.OfType<TwoStrokeEvaluation>().ToArray()), cancelled, clock.Elapsed.TotalMilliseconds, workers);
    }

    private static TwoStrokeEvaluation Solve(TwoStrokeVariant variant, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var sweep = TwoStrokeSimulator.Sweep(variant.Configuration, false, cancellation: token);
            var status = sweep.Complete ? StudyStatus.Completed :
                sweep.Runs.Any(r => r.Status == TwoStrokeRunStatus.NumericalFailure) ? StudyStatus.NumericalFailure :
                sweep.Runs.Any(r => r.Status == TwoStrokeRunStatus.OutsideDomain) ? StudyStatus.Inadmissible : StudyStatus.NonConverged;
            return new(variant.Id, status, sweep, sweep.Runs.FirstOrDefault(r => r.Failure is not null)?.Failure, clock.Elapsed.TotalMilliseconds);
        }
        catch (ArgumentException ex) { return new(variant.Id, StudyStatus.Inadmissible, null, ex.Message, clock.Elapsed.TotalMilliseconds); }
    }

    private static StudyPoint[]? Points(TwoStrokeSweep? sweep) => sweep?.Complete == true
        ? sweep.Runs.Select(run => run.Point!).Select(p => new StudyPoint(p.Rpm, p.IndicatedPowerW, p.ShaftPowerW,
            p.ShaftTorqueNm, p.ChargingEfficiency, p.FuelMassKg, p.CylinderWorkJ + p.CrankcaseWorkJ,
            p.PeakPressurePa, p.PeakTemperatureK, p.Cycles, p.PeriodicError, p.Balance.MassKg, p.Balance.EnergyJ)).ToArray() : null;

    public ObjectiveValue Score(TwoStrokeEvaluation row, TwoStrokeSweep? baseline) =>
        Definition.Objective.Evaluate(Points(row.Sweep) ?? [], Points(baseline), revolutionsPerCycle: 1);

    public StudyCheck Verify(TwoStrokeStudyRun run, int candidateId, int referenceId = 0, CancellationToken token = default,
        IProgress<string>? progress = null)
    {
        if (run.Definition.Base != Definition.Base || !run.Definition.Axes.SequenceEqual(Definition.Axes) ||
            run.Definition.Objective != Definition.Objective || candidateId == referenceId ||
            (uint)candidateId >= Variants.Count || (uint)referenceId >= Variants.Count)
            throw new ArgumentException("Candidatos o estudio 2T incompatibles.");
        int[] ids = Definition.Objective.Kind == ObjectiveKind.HighTorqueLimitedLowLoss
            ? new[] { candidateId, referenceId, 0 }.Distinct().ToArray() : [candidateId, referenceId];
        var coarse = ids.ToDictionary(id => id, id => run.Evaluations.SingleOrDefault(r => r.Id == id && r.Status == StudyStatus.Completed)
            ?? throw new ArgumentException("El contraste requiere curvas periódicas completas."));
        if (ids.Length * Definition.Base.Speeds.Values().Count() * 2 > StudyPlan.MaximumVerificationPoints ||
            ids.Sum(id => Work(Variants[id].Configuration)) * 20 > 600)
            throw new ArgumentException("El refinamiento supera el límite de costo. Reducí el rango de RPM o la malla base.");
        var clock = Stopwatch.StartNew();
        var levels = new List<StudyLevel>();
        foreach (int scale in new[] { 1, 2, 4 })
        {
            token.ThrowIfCancellationRequested();
            Dictionary<int, TwoStrokeEvaluation> rows = coarse;
            if (scale == 1 && coarse.Values.Any(row => row.Sweep?.NumericalRevision != TwoStrokeConfiguration.NumericalRevision))
            {
                progress?.Report("Recalculando la base con el operador numérico actual");
                rows = ids.ToDictionary(id => id, id => Solve(Variants[id], token));
            }
            if (scale > 1)
            {
                progress?.Report($"Verificando resolución ×{scale}");
                var cache = new Dictionary<TwoStrokeConfiguration, TwoStrokeEvaluation>();
                rows = new();
                foreach (int id in ids)
                {
                    var v = Variants[id];
                    var refined = Refine(v.Configuration, scale);
                    if (!cache.TryGetValue(refined, out var row))
                    {
                        row = Solve(v with { Configuration = refined }, token);
                        cache.Add(refined, row);
                    }
                    rows.Add(id, row);
                }
            }
            var baseline = rows.GetValueOrDefault(0)?.Sweep;
            var a = Score(rows[referenceId], baseline);
            var b = Score(rows[candidateId], baseline);
            double? advantage = a.Score is double av && b.Score is double bv
                ? Definition.Objective.Preference(bv) - Definition.Objective.Preference(av) : null;
            levels.Add(new(scale, b.Score, a.Score, advantage, b.LowMarginNm, a.LowMarginNm,
                rows[candidateId].Error ?? rows[referenceId].Error ?? b.Exclusion ?? a.Exclusion));
        }
        var (verdict, band) = StudyVerification.Classify(levels);
        return new(candidateId, referenceId, verdict, band, levels.AsReadOnly(), clock.Elapsed.TotalMilliseconds,
            NumericalRevision: TwoStrokeConfiguration.NumericalRevision);
    }

    public static TwoStrokeConfiguration Refine(TwoStrokeConfiguration configuration, int scale)
    {
        if (scale is not (1 or 2 or 4)) throw new ArgumentOutOfRangeException(nameof(scale));
        return configuration with
        {
            IntakeDuct = configuration.IntakeDuct with { Cells = configuration.IntakeDuct.Cells * scale },
            Expansion = configuration.Expansion with { Cells = configuration.Expansion.Cells * scale },
            Numerics = configuration.Numerics with
            {
                MaximumStepDeg = configuration.Numerics.MaximumStepDeg / scale,
                PeriodicTolerance = Math.Max(1e-8, configuration.Numerics.PeriodicTolerance / scale)
            }
        };
    }
}
