using System.Diagnostics;

namespace Dino2.Core.TwoStroke;

// Optional timing of exclusive solver regions for the reproducible stage 7 campaign.
internal sealed class TwoStrokeProfile
{
    internal long CylinderThermo, CrankcaseThermo, DuctPreparation, InterfaceFlux, DuctBalance, Total;
    internal int Evaluations;

    internal static long Timestamp() => Stopwatch.GetTimestamp();
    internal static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}

public sealed partial class TwoStrokeSimulator
{
    internal static (TwoStrokePoint Point, TwoStrokeProfile Profile) ProfileOperatingPoint(TwoStrokeConfiguration configuration, int rpm)
    {
        configuration = configuration with { Speeds = new(rpm, rpm, 1) };
        configuration.Validate();
        configuration.ValidateOperatingPoint(rpm);
        var profile = new TwoStrokeProfile();
        var simulator = new TwoStrokeSimulator(configuration, rpm, profile);
        long start = TwoStrokeProfile.Timestamp();
        var point = simulator.Run(false, default, null, out _, out _);
        profile.Total = TwoStrokeProfile.Timestamp() - start;
        return (point, profile);
    }
}
