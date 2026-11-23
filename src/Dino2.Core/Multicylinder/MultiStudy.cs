using System.Diagnostics;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Studies;

namespace Dino2.Core.Multicylinder;

public sealed record MultiStudyDefinition(MultiConfiguration Base, IReadOnlyList<ParameterRange> Axes, StudyObjective Objective);

public sealed class MultiStudy
{
    public MultiStudyDefinition Definition { get; }
    public StudyPlan Grid { get; }
    public double EstimatedWork { get; }

    public MultiStudy(MultiStudyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Base is null) throw new ArgumentException("El estudio requiere un motor base.");
        definition.Base.Validate();
        Grid = StudyPlan.Create(new(definition.Base.Cylinder, definition.Axes, definition.Base.Cylinder.Speeds, definition.Objective));
        Definition = definition with { Axes = Grid.Definition.Axes };
        EstimatedWork = Grid.Variants.Select(v => v.Configuration).Distinct()
            .Sum(v => Work(definition.Base with { Cylinder = v }));
        if (EstimatedWork > 160)
            throw new ArgumentException("Costo 1D multicilíndrico superior a 160 unidades. Reducí variantes, regímenes o malla.");
    }

    public MultiConfiguration Variant(int id) => Definition.Base with { Cylinder = Grid.Variants[id].Configuration };

    public StudyRun Run(int workers = 1, CancellationToken cancellation = default, IProgress<StudyProgress>? progress = null) =>
        StudyRunner.Run(Grid, (variant, token, started) => Evaluate(Variant(variant.Id), variant.Id, token, started),
            workers, cancellation, progress) with
        {
            Provenance = ResultProvenance.Capture(MultiConfiguration.ModelId,
                Definition.Base.Cylinder.ModelIdentifier + "-network-recovery-v1")
        };

    internal static StudyEvaluation Evaluate(MultiConfiguration configuration, int id, CancellationToken token, Action? started = null)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            configuration.Validate();
            started?.Invoke();
            var results = MultiSimulator.Run(configuration, false, token);
            var points = results.Points.Where(p => p.Metrics is not null).Select(Project).ToArray();
            var failed = results.Points.FirstOrDefault(p => p.Status != MultiStatus.Converged);
            return failed is null
                ? new(id, StudyStatus.Completed, Array.AsReadOnly(points), null, clock.Elapsed.TotalMilliseconds)
                : new(id, failed.Status switch
                    {
                        MultiStatus.NotPeriodic => StudyStatus.NonConverged,
                        MultiStatus.OutsideDomain => StudyStatus.Inadmissible,
                        _ => StudyStatus.NumericalFailure
                    }, Array.AsReadOnly(points), $"{failed.Rpm} RPM · {failed.Detail}", clock.Elapsed.TotalMilliseconds);
        }
        catch (ArgumentException ex) { return new(id, StudyStatus.Inadmissible, [], ex.Message, clock.Elapsed.TotalMilliseconds); }
    }

    internal static StudyPoint Project(MultiPoint p)
    {
        var m = p.Metrics!;
        return new(p.Rpm, m.IndicatedPowerW, m.BrakePowerW, m.BrakeTorqueNm, m.VolumetricEfficiency, m.FuelKgPerCycle,
            m.NetWorkJ, p.Cylinders.Max(c => c.PeakPressurePa), p.Cylinders.Max(c => c.PeakTemperatureK),
            p.Cycles, p.PeriodicError, p.Balance!.MassKg, p.Balance.TotalEnergyJ,
            p.Balance.MaximumInterfaceSpeciesResidualKg, p.Balance.MaximumInterfaceEnergyResidualJ, p.Balance.MaximumRunnerMomentumNs,
            p.Cylinders.Any(c => c.BurnCorrelationExtrapolated));
    }

    internal static double Work(MultiConfiguration c)
    {
        var ducts = c.Cylinder.Ducts!;
        var geometry = new List<DuctGeometry> { ducts.Intake, ducts.Exhaust };
        double cells = c.Phases.Count * (ducts.Intake.Cells + ducts.Exhaust.Cells);
        if (c.Manifolds is { } m)
        {
            geometry.Add(m.Intake.Trunk);
            geometry.Add(m.Exhaust.Trunk);
            cells += m.Intake.Trunk.Cells + m.Exhaust.Trunk.Cells;
        }
        double inverseDx = geometry.Max(d => d.Cells / d.LengthM);
        if (c.Manifolds is { } manifold)
        {
            double InverseLength(ManifoldSide side, DuctGeometry runner)
            {
                double trunk = side.Trunk.ValveDiameterM;
                double diameter = runner.ReservoirDiameterM;
                return Math.PI / 4 * (trunk * trunk + c.Phases.Count * diameter * diameter) / side.VolumeM3;
            }
            inverseDx = Math.Max(inverseDx, Math.Max(InverseLength(manifold.Intake, ducts.Intake),
                InverseLength(manifold.Exhaust, ducts.Exhaust)));
        }
        double steps = c.Cylinder.Speeds.Values().Sum(r => Math.Max(
            inverseDx * .45 / ducts.Courant * 3000.0 / r, 20 * .5 / c.Cylinder.Solver.MaximumStepDeg));
        return cells * steps / 6400 * c.Cylinder.Solver.MaximumCycles / 40;
    }
}

public sealed record MultiDifference(int Rpm, double? PowerDifferenceW, double? TorqueDifferenceNm, double? FillingDifference,
    StudyVerdict Verdict, double? ObservedBandW, IReadOnlyList<StudyLevel> Levels);

public static class MultiComparison
{
    public static IReadOnlyList<MultiDifference> Compare(MultiResult a, MultiResult b)
    {
        var bySpeed = a.Points.ToDictionary(p => p.Rpm);
        return b.Points.Where(p => bySpeed.ContainsKey(p.Rpm)).Select(p =>
        {
            var basis = bySpeed[p.Rpm].Metrics;
            var variant = p.Metrics;
            return new MultiDifference(p.Rpm, variant?.BrakePowerW - basis?.BrakePowerW,
                variant?.BrakeTorqueNm - basis?.BrakeTorqueNm, variant?.VolumetricEfficiency - basis?.VolumetricEfficiency,
                StudyVerdict.Unavailable, null, []);
        }).ToArray();
    }

    public static IReadOnlyList<MultiDifference> Verify(MultiResult a, MultiResult b, int rpm,
        CancellationToken cancellation = default, IProgress<string>? progress = null)
    {
        RequireComparable(a.Configuration, b.Configuration);
        var raw = Compare(a, b).SingleOrDefault(p => p.Rpm == rpm)
            ?? throw new ArgumentException("Elegí una RPM presente en ambas curvas.");
        if (raw.PowerDifferenceW is null) throw new ArgumentException("El contraste requiere dos puntos periódicos.");
        var speeds = new RpmRange(rpm, rpm, 1);
        var levels = EvaluatePair(a.Configuration, b.Configuration, speeds,
            new(ObjectiveKind.PeakPower), cancellation, progress);
        var (verdict, band) = StudyVerification.Classify(levels);
        return Compare(a, b).Select(p => p.Rpm == rpm ? p with
            { Verdict = verdict, ObservedBandW = band, Levels = levels } : p).ToArray();
    }

    public static IReadOnlyList<StudyLevel> VerifyObjective(MultiStudy study, int id, CancellationToken cancellation = default,
        IProgress<string>? progress = null)
    {
        if (id <= 0 || id >= study.Grid.Variants.Count) throw new ArgumentException("Elegí una variante del estudio.");
        return EvaluatePair(study.Variant(0), study.Variant(id), study.Definition.Base.Cylinder.Speeds,
            study.Definition.Objective, cancellation, progress);
    }

    private static void RequireComparable(MultiConfiguration a, MultiConfiguration b)
    {
        if (a.Phases.Count != b.Phases.Count || a.Cylinder.Ducts!.Scheme != b.Cylinder.Ducts!.Scheme ||
            a.Cylinder.Ducts.IntakeBoundary != b.Cylinder.Ducts.IntakeBoundary ||
            a.Cylinder.Ducts.ExhaustBoundary != b.Cylinder.Ducts.ExhaustBoundary ||
            a.Cylinder.Combustion.DurationDeg.HasValue != b.Cylinder.Combustion.DurationDeg.HasValue ||
            a.Cylinder.Thermochemistry!.BurnLaw != b.Cylinder.Thermochemistry!.BurnLaw ||
            (a.Manifolds is null) != (b.Manifolds is null))
            throw new ArgumentException("El contraste exige igual número de cilindros, topología, esquema y ley de combustión.");
    }

    private static IReadOnlyList<StudyLevel> EvaluatePair(MultiConfiguration a, MultiConfiguration b, RpmRange speeds,
        StudyObjective objective, CancellationToken token, IProgress<string>? progress)
    {
        RequireComparable(a, b);
        var levels = new List<StudyLevel>(3);
        foreach (int scale in new[] { 1, 2, 4 })
        {
            token.ThrowIfCancellationRequested();
            MultiConfiguration x, y;
            try
            {
                x = RefinePair(a, b, speeds, scale);
                y = RefinePair(b, a, speeds, scale);
                // Guard the refined grid count and bound individual contrast campaigns.
                if (MultiStudy.Work(x) + (x == y ? 0 : MultiStudy.Work(y)) > 480)
                    throw new ArgumentException("Costo estimado del contraste superior al límite.");
            }
            catch (ArgumentException ex)
            {
                levels.Add(new(scale, null, null, null, null, null, ex.Message));
                break;
            }
            progress?.Report($"Contraste · resolución ×{scale}");
            var left = MultiStudy.Evaluate(x, 0, token);
            var right = x == y ? left : MultiStudy.Evaluate(y, 1, token);
            if (left.Status != StudyStatus.Completed || right.Status != StudyStatus.Completed)
            {
                levels.Add(new(scale, null, null, null, null, null, left.Error ?? right.Error));
                break;
            }
            var l = objective.Evaluate(left.Points, left.Points);
            var r = objective.Evaluate(right.Points, left.Points);
            double? difference = l.Score is double ls && r.Score is double rs
                ? objective.Preference(rs) - objective.Preference(ls) : null;
            levels.Add(new(scale, r.Score, l.Score, difference, r.LowMarginNm, l.LowMarginNm, r.Exclusion ?? l.Exclusion));
        }
        return levels.AsReadOnly();
    }

    private static MultiConfiguration RefinePair(MultiConfiguration a, MultiConfiguration b, RpmRange speeds, int scale)
    {
        DuctGeometry Grid(DuctGeometry x, DuctGeometry y)
        {
            double dx = Math.Min(x.LengthM / x.Cells, y.LengthM / y.Cells);
            int cells = (int)Math.Ceiling(x.LengthM / dx - 1e-9) * scale;
            if (cells > 256) throw new ArgumentException("El contraste supera 256 celdas por conducto.");
            return x with { Cells = cells };
        }
        var da = a.Cylinder.Ducts!;
        var db = b.Cylinder.Ducts!;
        var na = a.Manifolds;
        var nb = b.Manifolds;
        var sa = a.Cylinder.Solver;
        var sb = b.Cylinder.Solver;
        double step = Math.Min(sa.MaximumStepDeg, sb.MaximumStepDeg) / scale;
        if (step < .025) throw new ArgumentException("El paso del contraste quedaría por debajo de 0,025°.");
        return a with
        {
            Cylinder = a.Cylinder with
            {
                Speeds = speeds,
                Solver = sa with
                {
                    MaximumStepDeg = step,
                    PeriodicTolerance = Math.Max(1e-9, Math.Min(sa.PeriodicTolerance, sb.PeriodicTolerance) / (scale * scale)),
                    MaximumCycles = Math.Max(sa.MaximumCycles, sb.MaximumCycles)
                },
                Ducts = da with
                {
                    Courant = Math.Min(da.Courant, db.Courant),
                    Intake = Grid(da.Intake, db.Intake), Exhaust = Grid(da.Exhaust, db.Exhaust)
                }
            },
            Manifolds = na is null ? null : new(
                na.Intake with { Trunk = Grid(na.Intake.Trunk, nb!.Intake.Trunk) },
                na.Exhaust with { Trunk = Grid(na.Exhaust.Trunk, nb!.Exhaust.Trunk) })
        };
    }
}
