namespace Dino2.Core.FourStroke;

public sealed record CycleSample(double AngleDeg, double VolumeM3, double MassKg, double TemperatureK,
    double PressurePa, double FreshMassKg, double IntakeLiftM, double ExhaustLiftM,
    double IntakeFlowKgPerS, double ExhaustFlowKgPerS, double InternalEnergyJ, double WorkJ,
    double HeatReleasedJ, double WallHeatJ, double FlowEnthalpyJ);

public sealed record FourStrokePoint(int Rpm, double IndicatedPowerW, double BrakePowerW,
    double IndicatedTorqueNm, double BrakeTorqueNm, double GrossWorkJ, double PumpingWorkJ,
    double NetWorkJ, double FrictionWorkJ, double BmepPa, double VolumetricEfficiency,
    double IndicatedEfficiency, double BrakeEfficiency, double FuelMassKg,
    double PeakPressurePa, double PeakTemperatureK, double HeatReleasedJ, double WallHeatJ,
    double FlowEnthalpyJ, double MassResidualKg, double EnergyResidualJ, double PeriodicError,
    int Cycles, int AcceptedSteps, int RejectedSteps, double ElapsedMilliseconds,
    IReadOnlyList<CycleSample> Trace);

public sealed record FourStrokeResult(FourStrokeConfiguration Configuration,
    IReadOnlyList<FourStrokePoint> Points, double ElapsedMilliseconds)
{
    public const string ModelId = "four-stroke-0d-air-v1";
}

public sealed record ComparisonPoint(int Rpm, double OriginalBrakePowerW, double VariantBrakePowerW,
    double PowerDifferenceW, double OriginalBrakeTorqueNm, double VariantBrakeTorqueNm, double TorqueDifferenceNm);

public static class EngineComparison
{
    public static IReadOnlyList<ComparisonPoint> Compare(FourStrokeResult original, FourStrokeResult variant)
    {
        var byRpm = variant.Points.ToDictionary(p => p.Rpm);
        return original.Points.Where(p => byRpm.ContainsKey(p.Rpm)).Select(p =>
        {
            var b = byRpm[p.Rpm];
            return new ComparisonPoint(p.Rpm, p.BrakePowerW, b.BrakePowerW, b.BrakePowerW - p.BrakePowerW,
                p.BrakeTorqueNm, b.BrakeTorqueNm, b.BrakeTorqueNm - p.BrakeTorqueNm);
        }).ToList().AsReadOnly();
    }
}
