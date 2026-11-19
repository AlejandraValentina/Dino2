using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;

namespace Dino2.Core.Studies;

public sealed record StudyDefinition(FourStrokeConfiguration Base, IReadOnlyList<ParameterRange> Axes, RpmRange Speeds,
    StudyObjective Objective);
public sealed record StudyVariant(int Id, FourStrokeConfiguration Configuration, IReadOnlyList<ParameterSample> Parameters);

public sealed class StudyPlan
{
    public const int MaximumVariants = 96;
    public const int MaximumOperatingPoints = 240;
    public const int MaximumDuctOperatingPoints = 120;
    public const int MaximumWorkers = 4;
    public const int MaximumVerificationPoints = 72;
    public const double MaximumDuctWork = 240;
    public const double MaximumVerificationWork = 500;
    public StudyDefinition Definition { get; }
    public IReadOnlyList<StudyVariant> Variants { get; }
    public int DistinctCurves { get; }
    public int OperatingPoints { get; }
    public double EstimatedDuctWork { get; }

    private StudyPlan(StudyDefinition definition, List<StudyVariant> variants, int points)
    {
        Definition = definition;
        Variants = variants.AsReadOnly();
        DistinctCurves = variants.Select(v => v.Configuration).Distinct().Count();
        OperatingPoints = DistinctCurves * points;
        EstimatedDuctWork = variants.Select(v => v.Configuration).Distinct().Sum(DuctWork);
    }

    public static StudyPlan Create(StudyDefinition input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Base is null || input.Speeds is null || input.Objective is null || input.Axes is null ||
            input.Axes.Count is < 1 or > 3 || input.Axes.Any(a => a is null) ||
            input.Axes.Select(a => a.Parameter).Distinct().Count() != input.Axes.Count)
            throw new ArgumentException("Elegí entre uno y tres parámetros distintos y una base 4T.");
        input.Base.Validate();
        var speeds = input.Speeds.Values().ToArray();
        if (speeds[^1] != input.Speeds.End)
            throw new ArgumentException("El incremento de RPM debe alcanzar exactamente el régimen final.");
        input.Objective.Validate(speeds, input.Base.Thermochemistry is not null);
        var definition = input with { Axes = Array.AsReadOnly(input.Axes.ToArray()) };
        var basis = input.Base with { Speeds = input.Speeds };
        var specs = definition.Axes.Select(a => StudyParameters.Get(a.Parameter)).ToArray();
        if (basis.Ducts is null && specs.Any(p => p.RequiresDucts))
            throw new ArgumentException("La geometría de conductos requiere un motor 1D.");
        var values = definition.Axes.Select((a, i) => Values(a, specs[i])).ToArray();
        int count = values.Aggregate(1, (product, axis) => product * axis.Length);
        if (count > MaximumVariants)
            throw new ArgumentException($"La grilla contiene {count} variantes. El límite es {MaximumVariants}.");

        var variants = new List<StudyVariant>(count + 1) { new(0, basis, Array.Empty<ParameterSample>()) };
        var parameters = new ParameterSample[values.Length];
        void Generate(int index, FourStrokeConfiguration configuration)
        {
            if (index == specs.Length)
            {
                if (configuration.Ducts is { } duct && basis.Ducts is { } original)
                    configuration = configuration with { Ducts = duct with
                    {
                        Intake = MatchSpacing(original.Intake, duct.Intake),
                        Exhaust = MatchSpacing(original.Exhaust, duct.Exhaust)
                    } };
                variants.Add(new(variants.Count, configuration, Array.AsReadOnly((ParameterSample[])parameters.Clone())));
                return;
            }
            var spec = specs[index];
            foreach (double value in values[index])
            {
                parameters[index] = new(spec.Id, value);
                Generate(index + 1, value == spec.Read(configuration) ? configuration : spec.Write(configuration, value));
            }
        }

        Generate(0, basis);
        var plan = new StudyPlan(definition, variants, speeds.Length);
        int limit = basis.Ducts is null ? MaximumOperatingPoints : MaximumDuctOperatingPoints;
        if (plan.OperatingPoints > limit)
            throw new ArgumentException($"El estudio pide {plan.OperatingPoints} puntos RPM únicos. El límite de este modelo es {limit}.");
        if (plan.EstimatedDuctWork > MaximumDuctWork)
            throw new ArgumentException($"El costo 1D estimado supera {MaximumDuctWork:G} unidades. Reducí malla, rango o variantes.");
        return plan;
    }

    // Relative spatial work per cycle from cell count times the maximum inverse step length.
    // This bounds dense/short pipes at low RPM without claiming a wall time.
    internal static double DuctWork(FourStrokeConfiguration c)
    {
        if (c.Ducts is not { } d || d.Intake.Cells > 256 || d.Exhaust.Cells > 256) return 0;
        double cells = d.Intake.Cells + d.Exhaust.Cells;
        double inverseDx = Math.Max(d.Intake.Cells / d.Intake.LengthM, d.Exhaust.Cells / d.Exhaust.LengthM);
        return cells * inverseDx / 6400 * (.45 / d.Courant) * c.Speeds.Values().Sum(r => 3000.0 / r);
    }

    // Keep the base cell size approximately fixed when lengths vary, subject to the solver minimum.
    private static DuctGeometry MatchSpacing(DuctGeometry original, DuctGeometry variant) => variant with
    {
        Cells = Math.Max(8, (int)Math.Round(original.Cells * variant.LengthM / original.LengthM, MidpointRounding.AwayFromZero))
    };

    private static double[] Values(ParameterRange range, ParameterSpec spec)
    {
        if (!double.IsFinite(range.Start) || !double.IsFinite(range.End) || !double.IsFinite(range.Increment) ||
            range.Start < spec.Minimum || range.End > spec.Maximum || range.End < range.Start || range.Increment <= 0)
            throw new ArgumentException($"{spec.Display}: usá valores entre {spec.Minimum:G} y {spec.Maximum:G} e incremento positivo.");
        double intervals = (range.End - range.Start) / range.Increment;
        var rounded = Math.Round(intervals);
        if (intervals >= MaximumVariants || (range.Start < range.End && rounded == 0) || Math.Abs(intervals - rounded) > 1e-8)
            throw new ArgumentException($"{spec.Display}: el incremento debe alcanzar el final y generar hasta {MaximumVariants} valores.");
        var values = new double[(int)rounded + 1];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = i == values.Length - 1 ? range.End : range.Start + i * range.Increment;
            if (i > 0 && values[i] <= values[i - 1])
                throw new ArgumentException($"{spec.Display}: el incremento es menor que la resolución de entrada.");
        }
        return values;
    }
}
