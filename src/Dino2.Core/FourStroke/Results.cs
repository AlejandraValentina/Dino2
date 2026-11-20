namespace Dino2.Core.FourStroke;

public readonly record struct CycleSample(double AngleDeg, double VolumeM3, double MassKg, double TemperatureK,
    double PressurePa, double FreshMassKg, double IntakeLiftM, double ExhaustLiftM,
    double IntakeFlowKgPerS, double ExhaustFlowKgPerS, double InternalEnergyJ, double WorkJ,
    double HeatReleasedJ, double WallHeatJ, double FlowEnthalpyJ)
{
    public double IntakePortPressurePa { get; init; }
    public double ExhaustPortPressurePa { get; init; }
    public Thermochemistry.SpeciesMasses Composition { get; init; }
}

public sealed record FourStrokePoint(int Rpm, double IndicatedPowerW, double BrakePowerW,
    double IndicatedTorqueNm, double BrakeTorqueNm, double GrossWorkJ, double PumpingWorkJ,
    double NetWorkJ, double FrictionWorkJ, double BmepPa, double VolumetricEfficiency,
    double IndicatedEfficiency, double BrakeEfficiency, double FuelMassKg,
    double PeakPressurePa, double PeakTemperatureK, double HeatReleasedJ, double WallHeatJ,
    double FlowEnthalpyJ, double MassResidualKg, double EnergyResidualJ, double PeriodicError,
    int Cycles, int AcceptedSteps, int RejectedSteps, int RhsEvaluations, double ElapsedMilliseconds,
    IReadOnlyList<CycleSample> Trace)
{
    public DuctDiagnostics? GasDynamics { get; init; }
    public Thermochemistry.ReactiveDiagnostics? Chemistry { get; init; }
}

public sealed record DuctDiagnostics(double MassResidualKg, double MaximumSpeciesResidualKg, double EnergyResidualJ,
    double PeriodicError, long CellEvaluations, double MomentumResidualNs = 0);

public enum FourStrokeFailureKind { OutsideDomain, NonConverged, NumericalFailure }
public sealed record FourStrokeFailure(int Rpm, FourStrokeFailureKind Kind, string Message);

public sealed record FourStrokeResult(FourStrokeConfiguration Configuration,
    IReadOnlyList<FourStrokePoint> Points, double ElapsedMilliseconds)
{
    public const string ModelId = "four-stroke-0d-air-v1";
    public string Model { get; init; } = ModelId;
    public ResultProvenance? Provenance { get; init; }
    public IReadOnlyList<FourStrokeFailure> Failures { get; init; } = [];
    public bool Complete => Failures.Count == 0 && Points.Count == Configuration.Speeds.Values().Count();
}

internal static class FourStrokeSweep
{
    internal static FourStrokeResult Execute(FourStrokeConfiguration c, Func<int, FourStrokePoint> solve,
        CancellationToken cancellation, IProgress<string>? progress)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var points = new List<FourStrokePoint>();
        var failures = new List<FourStrokeFailure>();
        foreach (int rpm in c.Speeds.Values())
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report($"Simulando {rpm} RPM…");
            try { points.Add(solve(rpm)); }
            catch (ArithmeticException error)
            {
                var kind = error switch
                {
                    ModelDomainException => FourStrokeFailureKind.OutsideDomain,
                    CycleConvergenceException => FourStrokeFailureKind.NonConverged,
                    _ => FourStrokeFailureKind.NumericalFailure
                };
                failures.Add(new(rpm, kind, error.Message));
            }
        }
        return new(c, points.AsReadOnly(), clock.Elapsed.TotalMilliseconds)
            { Model = c.ModelIdentifier, Failures = failures.AsReadOnly(),
                Provenance = ResultProvenance.Capture(c.ModelIdentifier, "four-stroke-recovery-v1" +
                    (c.Combustion.DurationDeg.HasValue ? "-wiebe-angle" : "")) };
    }
}

public sealed record ComparisonPoint(int Rpm, double OriginalBrakePowerW, double VariantBrakePowerW,
    double PowerDifferenceW, double OriginalBrakeTorqueNm, double VariantBrakeTorqueNm, double TorqueDifferenceNm)
{
    public double FillingDifference { get; init; }
}

public static class EngineComparison
{
    public static IReadOnlyList<ComparisonPoint> Compare(FourStrokeResult original, FourStrokeResult variant)
    {
        var byRpm = variant.Points.ToDictionary(p => p.Rpm);
        return original.Points.Where(p => byRpm.ContainsKey(p.Rpm)).Select(p =>
        {
            var b = byRpm[p.Rpm];
            return new ComparisonPoint(p.Rpm, p.BrakePowerW, b.BrakePowerW, b.BrakePowerW - p.BrakePowerW,
                p.BrakeTorqueNm, b.BrakeTorqueNm, b.BrakeTorqueNm - p.BrakeTorqueNm) { FillingDifference = b.VolumetricEfficiency - p.VolumetricEfficiency };
        }).ToList().AsReadOnly();
    }
}
