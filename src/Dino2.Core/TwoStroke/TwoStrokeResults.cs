using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.TwoStroke;

public readonly record struct TwoStrokeSample(double AngleDeg, double CylinderPressurePa, double CylinderTemperatureK,
    double CrankcasePressurePa, double CrankcaseTemperatureK, double ReedKgPerS, double TransferKgPerS,
    double ExhaustOutKgPerS, double CylinderVolumeM3, double CrankcaseVolumeM3, double FreshCylinderKg,
    double CylinderMassKg, double IntakeValvePressurePa, double ExhaustPortPressurePa);

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
}

public enum TwoStrokeRunStatus { Periodic, NonConverged, OutsideDomain, NumericalFailure }
public sealed record TwoStrokeRun(int Rpm, TwoStrokePoint? Point, string? Failure, TwoStrokeRunStatus Status);
public sealed record TwoStrokeSweep(TwoStrokeConfiguration Configuration, IReadOnlyList<TwoStrokeRun> Runs)
{
    public string ModelId => TwoStrokeConfiguration.ModelId;
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
            TwoStrokeRun run;
            try { run = new(rpm, new TwoStrokeSimulator(configuration, rpm).Run(trace, cancellation), null, TwoStrokeRunStatus.Periodic); }
            catch (ArithmeticException exception)
            {
                run = new(rpm, null, exception.Message, exception switch
                {
                    CycleConvergenceException => TwoStrokeRunStatus.NonConverged,
                    ModelDomainException => TwoStrokeRunStatus.OutsideDomain,
                    _ => TwoStrokeRunStatus.NumericalFailure
                });
            }
            results.Add(run);
            progress?.Report(run);
        }
        return new(configuration, results.AsReadOnly());
    }

    public static TwoStrokePoint Simulate(TwoStrokeConfiguration configuration, int rpm, bool trace = true,
        CancellationToken cancellation = default)
    {
        configuration = configuration with { Speeds = new RpmRange(rpm, rpm, 1) };
        configuration.Validate();
        return new TwoStrokeSimulator(configuration, rpm).Run(trace, cancellation);
    }
}
