using Dino2.Core.GasDynamics;

namespace Dino2.Core.FourStroke;

public readonly record struct PistonPosition(double VolumeM3, double VolumeDerivativeM3PerDeg, double WettedAreaM2);

public static class SliderCrank
{
    public static PistonPosition At(CylinderGeometry g, double degrees)
    {
        double theta = degrees * Math.PI / 180;
        double radius = g.StrokeM / 2;
        double sin = Math.Sin(theta), cos = Math.Cos(theta);
        double root = Math.Sqrt(g.RodLengthM * g.RodLengthM - radius * radius * sin * sin);
        double x = radius * (1 - cos) + g.RodLengthM - root;
        double area = Math.PI / 4 * g.BoreM * g.BoreM;
        double clearance = g.DisplacementM3 / (g.CompressionRatio - 1);
        double derivative = (radius * sin + radius * radius * sin * cos / root) * Math.PI / 180;
        return new(clearance + area * x, area * derivative,
            2 * area + Math.PI * g.BoreM * (clearance / area + x));
    }
}

public static class ValveFlow
{
    public static double Lift(PoppetValve valve, double degrees)
    {
        if (valve.LiftProfile is { } profile)
        {
            double profilePhase = degrees % 720;
            if (profilePhase < 0) profilePhase += 720;
            int low = 0, high = profile.Count - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (profile[middle].CrankAngleDeg <= profilePhase) low = middle;
                else high = middle;
            }
            var a = profile[low];
            var b = profile[high];
            double fraction = (profilePhase - a.CrankAngleDeg) / (b.CrankAngleDeg - a.CrankAngleDeg);
            return a.LiftM + fraction * (b.LiftM - a.LiftM);
        }
        double phase = (degrees - valve.OpensDeg) % 720;
        if (phase < 0) phase += 720;
        double duration = valve.ClosesDeg - valve.OpensDeg;
        if (phase >= duration) return 0;
        double sine = Math.Sin(Math.PI * phase / duration);
        return valve.MaximumLiftM * sine * sine;
    }

    public static double GeometricArea(PoppetValve valve, double degrees) =>
        valve.Count * Math.Min(Math.PI * valve.DiameterM * Lift(valve, degrees),
            Math.PI * valve.DiameterM * valve.DiameterM / 4);

    public static double EffectiveArea(PoppetValve valve, double degrees) => valve.DischargeCoefficient * GeometricArea(valve, degrees);

    public static double EffectiveArea(PoppetValve valve, double degrees, double upstreamPressurePa, double downstreamPressurePa)
    {
        return EffectiveArea(valve, degrees, upstreamPressurePa, downstreamPressurePa, true);
    }

    public static double EffectiveArea(PoppetValve valve, double degrees, double pressure1Pa, double pressure2Pa, bool forward)
    {
        double geometric = GeometricArea(valve, degrees);
        if (pressure1Pa == pressure2Pa) return valve.DischargeCoefficient * geometric;
        double upstream = Math.Max(pressure1Pa, pressure2Pa), downstream = Math.Min(pressure1Pa, pressure2Pa);
        DischargeMap? map = forward ? valve.DischargeMap : valve.ReverseDischargeMap;
        if (map is null && !forward && valve.DischargeMap is { } bidirectional &&
            string.Equals(bidirectional.Metadata.Direction, "both", StringComparison.OrdinalIgnoreCase)) map = bidirectional;
        if (map is null)
        {
            if (valve.DischargeMap is not null || valve.ReverseDischargeMap is not null)
                throw new InvalidOperationException("El flujo invirtió dirección, pero no hay un mapa Cd para ese sentido; no se sustituye silenciosamente por el Cd constante.");
            return valve.DischargeCoefficient * geometric;
        }
        if (!string.Equals(map.Metadata.CompatibleModel, "four-stroke-0d-isentropic-nozzle", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(map.Metadata.ReferenceArea, "curtain_area", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(map.Metadata.PressureConvention, "upstream_stagnation_to_downstream_static", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Convención Cd incompatible con el modelo 4T 0D isentrópico.");
        if (!string.Equals(map.Metadata.Direction, "both", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(map.Metadata.Direction, forward ? "forward" : "reverse", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El mapa Cd no declara el sentido de flujo solicitado.");
        double opening = valve.MaximumLiftM <= 0 ? 0 : Lift(valve, degrees) / valve.MaximumLiftM;
        return map.Evaluate(opening, downstream / upstream) * geometric;
    }

    // Positive from side 1 to side 2; upstream reservoir/cylinder treated as stagnation state.
    // Local gamma approximation for the nozzle; the cylinder uses temperature-dependent energy.
    public static double MassRate(double effectiveArea, double p1, double t1, double p2, double t2)
    {
        if (effectiveArea <= 0 || p1 == p2) return 0;
        double sign = p1 > p2 ? 1 : -1;
        double pu = Math.Max(p1, p2), pd = Math.Min(p1, p2), tu = sign > 0 ? t1 : t2;
        double gamma = AirProperties.Gamma(tu);
        double ratio = pd / pu;
        double critical = Math.Pow(2 / (gamma + 1), gamma / (gamma - 1));
        double flow = ratio <= critical
            ? Math.Sqrt(gamma) * Math.Pow(2 / (gamma + 1), (gamma + 1) / (2 * (gamma - 1)))
            : Math.Sqrt(2 * gamma / (gamma - 1) * (Math.Pow(ratio, 2 / gamma) - Math.Pow(ratio, (gamma + 1) / gamma)));
        return sign * effectiveArea * pu / Math.Sqrt(AirProperties.R * tu) * flow;
    }
}

public static class HeatRelease
{
    private const double A = 6.908;
    private static readonly double Normalization = 1 - Math.Exp(-A);
    public static double Fraction(double phase, double exponent)
    {
        if (phase <= 0) return 0;
        if (phase >= 1) return 1;
        return (1 - Math.Exp(-A * Math.Pow(phase, exponent + 1))) / Normalization;
    }
    public static double RatePerDegree(double degrees, double start, double duration, double exponent)
    {
        double x = (degrees - start) / duration;
        if (x <= 0 || x > 1) return 0;
        return A * (exponent + 1) * Math.Pow(x, exponent) * Math.Exp(-A * Math.Pow(x, exponent + 1)) / (duration * Normalization);
    }
}

public static class LossModels
{
    // Hohenberg: V in m3, p in bar, T in K, mean piston speed in m/s. W/(m2 K).
    public static double HeatTransferCoefficient(double volume, double pressure, double temperature, double meanSpeed) =>
        130 * Math.Pow(volume, -.06) * Math.Pow(pressure / 1e5, .8) * Math.Pow(temperature, -.4) * Math.Pow(meanSpeed + 1.4, .8);

    public static double FrictionMep(MechanicalLosses f, double peakPressure, double meanSpeed) =>
        f.ConstantPa + f.PeakPressureFactor * peakPressure + f.SpeedPaPerMPerS * meanSpeed +
        f.SpeedSquaredPaPerM2PerS2 * meanSpeed * meanSpeed;
}
