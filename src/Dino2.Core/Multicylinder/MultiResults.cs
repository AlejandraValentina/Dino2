using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.Multicylinder;

public enum MultiStatus { Converged, NotPeriodic, OutsideDomain, NumericalFailure }

public sealed record CylinderContribution(int CylinderNumber, double FiringOffsetDeg, double NetWorkJ,
    double PumpingWorkJ, double FrictionWorkJ, double IndicatedPowerW, double BrakePowerW,
    double FreshAirAtIntakeCloseKg, double GrossIntakeMassKg, double NetIntakeMassKg, double ResidualAtSpark,
    double PeakPressurePa, double PeakTemperatureK,
    double PeriodicError, bool BurnCorrelationExtrapolated, IReadOnlyList<CycleSample> Trace);

public readonly record struct ManifoldSample(double CrankAngleDeg, double IntakeJunctionPa, double ExhaustJunctionPa,
    double IntakeTrunkPa, double ExhaustTrunkPa, double IndicatedTorqueNm);

public sealed record MultiBalance(double MassKg, double MaximumElementKg, double TotalEnergyJ,
    double NitrogenResidualKg, double MaximumInterfaceSpeciesResidualKg, double MaximumInterfaceEnergyResidualJ,
    double MaximumRunnerMomentumNs);

public sealed record MultiPoint(int Rpm, MultiStatus Status, string? Detail, double PeriodicError, int Cycles,
    double ElapsedMilliseconds, MultiMetrics? Metrics, IReadOnlyList<CylinderContribution> Cylinders,
    IReadOnlyList<ManifoldSample> Cycle, MultiBalance? Balance, long AcceptedSteps = 0, long RejectedSteps = 0,
    long CellEvaluations = 0);

public sealed record MultiMetrics(double IndicatedPowerW, double BrakePowerW, double IndicatedTorqueNm,
    double BrakeTorqueNm, double ImepPa, double BmepPa, double VolumetricEfficiency, double NetWorkJ,
    double FrictionWorkJ, double FuelKgPerCycle);

public sealed record MultiResult(MultiConfiguration Configuration, IReadOnlyList<MultiPoint> Points,
    double ElapsedMilliseconds);
