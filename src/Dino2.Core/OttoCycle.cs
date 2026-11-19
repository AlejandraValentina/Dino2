namespace Dino2.Core;

public sealed record CycleState(int Number, double VolumeM3, double TemperatureK, double PressurePa);
public sealed record OperatingPoint(int Rpm, double IndicatedTorqueNm, double IndicatedPowerW);
public sealed record OttoResult(
    OttoConfiguration Configuration,
    double DisplacementM3, double MassKg, double HeatInJ, double HeatOutJ,
    double WorkJ, double Efficiency, double ImepPa,
    IReadOnlyList<CycleState> States, IReadOnlyList<OperatingPoint> Points);

public static class OttoCycle
{
    public const string ModelId = "otto-air-standard-v1";

    public static OttoResult Calculate(OttoConfiguration config)
    {
        config.Validate();
        double vd = Math.PI / 4 * config.BoreM * config.BoreM * config.StrokeM;
        double v2 = vd / (config.CompressionRatio - 1);
        double v1 = vd + v2;
        double r = config.CompressionRatio;
        double cv = config.GasConstantJPerKgK / (config.Gamma - 1);
        double mass = config.InitialPressurePa * v1 /
            (config.GasConstantJPerKgK * config.InitialTemperatureK);
        double t1 = config.InitialTemperatureK;
        double t2 = t1 * Math.Pow(r, config.Gamma - 1);
        double t3 = t2 + config.HeatInputJPerKg / cv;
        double t4 = t3 / Math.Pow(r, config.Gamma - 1);
        double heatIn = mass * config.HeatInputJPerKg;
        double heatOut = mass * cv * (t4 - t1);
        double efficiency = 1 - Math.Pow(r, 1 - config.Gamma);
        // Closed form avoids cancellation for the zero-heat reference case.
        double work = heatIn * efficiency;
        double torque = work / (4 * Math.PI); // one cycle per TWO revolutions
        var states = new[]
        {
            new CycleState(1, v1, t1, config.InitialPressurePa),
            new CycleState(2, v2, t2, mass * config.GasConstantJPerKgK * t2 / v2),
            new CycleState(3, v2, t3, mass * config.GasConstantJPerKgK * t3 / v2),
            new CycleState(4, v1, t4, mass * config.GasConstantJPerKgK * t4 / v1)
        };
        var points = new List<OperatingPoint>();
        // End is included only if reached by the step: start + k * step <= end.
        for (int rpm = config.RpmStart; rpm <= config.RpmEnd; rpm += config.RpmStep)
            points.Add(new(rpm, torque, work * rpm / 120));
        return new(config, vd, mass, heatIn, heatOut, work, efficiency, work / vd,
            Array.AsReadOnly(states), points.AsReadOnly());
    }
}
