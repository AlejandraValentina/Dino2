namespace Dino2.Core.FourStroke;

// Physical balances only. The integrator and periodic-cycle controller live in FourStrokeSimulator.
public sealed class CylinderEquations
{
    public const int Dimension = 10;
    private readonly FourStrokeConfiguration config;
    private readonly double degreesPerSecond;
    private readonly double meanSpeed;
    private readonly double intakeEnthalpy;
    private readonly double exhaustEnthalpy;
    public double FuelMassKg { get; set; }
    public bool PumpingInterval { get; set; }
    public bool BurningInterval { get; set; }
    public double BurnStartDeg { get; }
    public double BurnDurationDeg { get; }
    public int Evaluations { get; private set; }

    public CylinderEquations(FourStrokeConfiguration configuration, int rpm)
    {
        config = configuration;
        degreesPerSecond = 6.0 * rpm;
        meanSpeed = 2 * config.Geometry.StrokeM * rpm / 60;
        intakeEnthalpy = AirProperties.Enthalpy(config.Intake.TemperatureK);
        exhaustEnthalpy = AirProperties.Enthalpy(config.Exhaust.TemperatureK);
        BurnStartDeg = 360 - config.Combustion.SparkAdvanceDeg + degreesPerSecond * config.Combustion.IgnitionDelayS;
        BurnDurationDeg = config.Combustion.CrankDuration(rpm);
    }

    public CycleSample Sample(double angle, ReadOnlySpan<double> y)
    {
        double t = AirProperties.Temperature(y[1] / y[0]);
        var position = SliderCrank.At(config.Geometry, angle);
        double p = y[0] * AirProperties.R * t / position.VolumeM3;
        return new(angle, position.VolumeM3, y[0], t, p, y[2], ValveFlow.Lift(config.IntakeValve, angle),
            ValveFlow.Lift(config.ExhaustValve, angle),
            ValveFlow.MassRate(ValveFlow.EffectiveArea(config.IntakeValve, angle), config.Intake.PressurePa, config.Intake.TemperatureK, p, t),
            ValveFlow.MassRate(ValveFlow.EffectiveArea(config.ExhaustValve, angle), config.Exhaust.PressurePa, config.Exhaust.TemperatureK, p, t),
            y[1], y[3], y[7], y[5], y[6]);
    }

    public void Evaluate(double angle, ReadOnlySpan<double> y, Span<double> derivative)
    {
        Evaluations++;
        double mass = y[0];
        if (!double.IsFinite(mass) || mass < 1e-10) throw new NumericalStateException("Masa de cilindro no positiva.");
        double t = AirProperties.Temperature(y[1] / mass);
        var position = SliderCrank.At(config.Geometry, angle);
        double p = mass * AirProperties.R * t / position.VolumeM3;
        double intake = ValveFlow.MassRate(ValveFlow.EffectiveArea(config.IntakeValve, angle),
            config.Intake.PressurePa, config.Intake.TemperatureK, p, t);
        double exhaust = ValveFlow.MassRate(ValveFlow.EffectiveArea(config.ExhaustValve, angle),
            config.Exhaust.PressurePa, config.Exhaust.TemperatureK, p, t);
        double cylinderH = AirProperties.Enthalpy(t);
        double enthalpy = intake * (intake >= 0 ? intakeEnthalpy : cylinderH) +
            exhaust * (exhaust >= 0 ? exhaustEnthalpy : cylinderH);
        double fresh = Math.Max(0, intake) + (Math.Min(0, intake) + Math.Min(0, exhaust)) * y[2] / mass;
        double wall = config.Thermal.HohenbergMultiplier *
            LossModels.HeatTransferCoefficient(position.VolumeM3, p, t, meanSpeed) *
            position.WettedAreaM2 * (t - config.Thermal.WallTemperatureK);
        double released = BurningInterval ? FuelMassKg * config.Combustion.LowerHeatingValueJPerKg *
            config.Combustion.HeatReleaseEfficiency * HeatRelease.RatePerDegree(angle, BurnStartDeg, BurnDurationDeg, config.Combustion.WiebeExponent) : 0;
        double work = p * position.VolumeDerivativeM3PerDeg;
        derivative[0] = (intake + exhaust) / degreesPerSecond;
        derivative[1] = released + (enthalpy - wall) / degreesPerSecond - work;
        derivative[2] = fresh / degreesPerSecond;
        derivative[3] = work;
        derivative[4] = PumpingInterval ? work : 0;
        derivative[5] = wall / degreesPerSecond;
        derivative[6] = enthalpy / degreesPerSecond;
        derivative[7] = released;
        derivative[8] = (Math.Max(0, intake) + Math.Max(0, exhaust)) / degreesPerSecond;
        derivative[9] = -(Math.Min(0, intake) + Math.Min(0, exhaust)) / degreesPerSecond;
    }
}
