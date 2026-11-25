using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;
using System.Text.Json.Serialization;

namespace Dino2.Core.TwoStroke;

public readonly record struct TwoStrokeSample(double AngleDeg, double CylinderPressurePa, double CylinderTemperatureK,
    double CrankcasePressurePa, double CrankcaseTemperatureK, double ReedKgPerS, double TransferKgPerS,
    double ExhaustOutKgPerS, double CylinderVolumeM3, double CrankcaseVolumeM3, double FreshCylinderKg,
    double CylinderMassKg, double IntakeValvePressurePa, double ExhaustPortPressurePa)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double ReedLiftM { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ExhaustMapCd { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ExhaustMapMassResidualKgPerS { get; init; }
}

public sealed record TwoStrokeBalance(double MassKg, SpeciesMasses SpeciesKg, double CarbonKg, double HydrogenKg,
    double OxygenKg, double NitrogenKg, double EnergyJ, double FreshMarkerKg);

public sealed record TwoStrokePoint(int Rpm, double IndicatedPowerW, double ShaftPowerW, double ShaftTorqueNm,
    double ImepPa, double BmepPa, double CylinderWorkJ, double CrankcaseWorkJ, double WallHeatJ,
    double FuelMassKg, double FuelBurnedKg, double? BsfcGPerKwh, double FreshDeliveredKg, double FreshTransferReturnKg,
    double FreshTrappedKg, double FreshExhaustOutKg, double FreshExhaustReturnKg, double DeliveryRatio,
    double? TrappingEfficiency, double ScavengingEfficiency, double ChargingEfficiency,
    double ResidualMassKg, SpeciesMasses TrappedCompositionKg, SpeciesMasses ResidualCompositionKg,
    double PeakPressurePa, double PeakTemperatureK, int Cycles, int Steps, int TotalAcceptedSteps, int TotalRejectedSteps, double PeriodicError,
    TwoStrokeBalance Balance, IReadOnlyList<TwoStrokeSample> Trace)
{
    public double IndicatedTorqueNm => IndicatedPowerW / (Rpm * Math.PI / 30);
    public double NetFreshExhaustKg => FreshExhaustOutKg - FreshExhaustReturnKg;
    public double NetFreshTransferKg => FreshDeliveredKg - FreshTransferReturnKg;
    public double? TrappingPerTransferDelivery => FreshDeliveredKg > 0 ? FreshTrappedKg / FreshDeliveredKg : null;
    public double FreshAccountingResidualKg => FreshTrappedKg - NetFreshTransferKg + NetFreshExhaustKg;
}

public enum TwoStrokeRunStatus { Periodic, NonConverged, OutsideDomain, NumericalFailure }
public sealed record TwoStrokeConvergence(int Cycles, double Residual, double? PreviousResidual, string Dominant,
    int AcceptedSteps, int RejectedSteps);

public sealed record TwoStrokeRun(int Rpm, TwoStrokePoint? Point, string? Failure, TwoStrokeRunStatus Status,
    TwoStrokeConvergence? Convergence = null);
public sealed record TwoStrokeSweep(TwoStrokeConfiguration Configuration, IReadOnlyList<TwoStrokeRun> Runs,
    string? NumericalRevision = null)
{
    public string ModelId => TwoStrokeConfiguration.ModelId;
    public ResultProvenance? Provenance { get; init; }
    public bool Complete => Runs.Count == Configuration.Speeds.Values().Count() && Runs.All(run => run.Point is not null);
}

public sealed partial class TwoStrokeSimulator
{
    public static TwoStrokeSweep Sweep(TwoStrokeConfiguration configuration, bool trace = true,
        IProgress<TwoStrokeRun>? progress = null, CancellationToken cancellation = default)
    {
        configuration.Validate();
        var results = new List<TwoStrokeRun>();
        foreach (int rpm in configuration.Speeds.Values())
        {
            cancellation.ThrowIfCancellationRequested();
            var run = Solve(configuration, rpm, trace, cancellation, null, out _);
            results.Add(run);
            progress?.Report(run);
        }
        return new(configuration, results.AsReadOnly(), configuration.Revision)
            { Provenance = ResultProvenance.Capture(TwoStrokeConfiguration.ModelId, configuration.Revision) };
    }

    public static TwoStrokePoint Simulate(TwoStrokeConfiguration configuration, int rpm, bool trace = true,
        CancellationToken cancellation = default)
    {
        configuration = configuration with { Speeds = new RpmRange(rpm, rpm, 1) };
        configuration.Validate();
        configuration.ValidateOperatingPoint(rpm);
        return new TwoStrokeSimulator(configuration, rpm).Run(trace, cancellation, null, out _, out _);
    }

    // A converged state is transferred only within a single fixed engine and at the same EVO phase.
    // Kept as a diagnostic so reference sweeps retain independent initial conditions.
    internal static IReadOnlyList<TwoStrokeRun> ExploreContinuation(TwoStrokeConfiguration configuration,
        IEnumerable<int> sequence, CancellationToken cancellation = default)
    {
        configuration.Validate();
        var allowedSpeeds = configuration.Speeds.Values().ToHashSet();
        var results = new List<TwoStrokeRun>();
        double[]? seed = null;
        foreach (int rpm in sequence)
        {
            if (!allowedSpeeds.Contains(rpm)) throw new ArgumentException("RPM fuera de la grilla de continuación.");
            cancellation.ThrowIfCancellationRequested();
            results.Add(Solve(configuration, rpm, false, cancellation, seed, out seed));
        }
        return results.AsReadOnly();
    }

    private static TwoStrokeRun Solve(TwoStrokeConfiguration configuration, int rpm, bool trace,
        CancellationToken cancellation, double[]? seed, out double[]? periodicState)
    {
        TwoStrokeConvergence? diagnostic = null;
        periodicState = null;
        try
        {
            configuration.ValidateOperatingPoint(rpm);
            var point = new TwoStrokeSimulator(configuration, rpm).Run(trace, cancellation, seed,
                out periodicState, out diagnostic);
            return new(rpm, point, null, TwoStrokeRunStatus.Periodic, diagnostic);
        }
        catch (ArithmeticException exception)
        {
            var status = exception switch
            {
                CycleConvergenceException => TwoStrokeRunStatus.NonConverged,
                ModelDomainException => TwoStrokeRunStatus.OutsideDomain,
                _ => TwoStrokeRunStatus.NumericalFailure
            };
            return new(rpm, null, exception.Message, status, diagnostic);
        }
    }
}
