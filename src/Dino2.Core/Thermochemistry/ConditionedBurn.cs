using Dino2.Core.FourStroke;

namespace Dino2.Core.Thermochemistry;

public static class ConditionedBurn
{
    // Metghalchi & Keck 1982, equations 16-18, Table 3A, iso-octane.
    // Experimental range: phi 0.8-1.5, 298-700 K, 0.4-50 atm; dilution data f<=0.2 at phi=1.
    public static double LaminarSpeed(double temperature, double pressure, double phi, double residual)
    {
        if (!double.IsFinite(temperature + pressure + phi + residual) || temperature <= 0 || pressure <= 0 || phi < .8 || phi > 1.5 || residual < 0 || residual > .2)
            throw new ArgumentException("Velocidad laminar: phi 0.8–1.5, residuo 0–0.2 y estado finito positivo.");
        double reference = .2632 - .8472 * (phi - 1.13) * (phi - 1.13);
        return reference * Math.Pow(temperature / 298, 2.18 - .8 * (phi - 1)) *
            Math.Pow(pressure / 101325, -.16 + .22 * (phi - 1)) * (1 - 2.1 * residual);
    }
    public static double DurationSeconds(FourStrokeConfiguration c, int rpm, double temperature, double pressure, double residual)
    {
        var settings = c.Thermochemistry ?? throw new ArgumentException("Faltan opciones termoquímicas.");
        if (settings.BurnLaw == BurnLaw.PrescribedWiebe) return c.Combustion.DurationS;
        double phi = Math.Clamp(SpeciesThermodynamics.StoichiometricAfr / c.Combustion.AirFuelRatio, .8, 1);
        double reference = LaminarSpeed(settings.ReferenceTemperatureK, settings.ReferencePressurePa, settings.ReferenceEquivalenceRatio, settings.ReferenceResidualFraction) +
            settings.TurbulenceToPistonSpeed * settings.ReferenceMeanPistonSpeedMPerS;
        double speed = LaminarSpeed(temperature, pressure, phi, residual) +
            settings.TurbulenceToPistonSpeed * 2 * c.Geometry.StrokeM * rpm / 60;
        return c.Combustion.DurationS * c.Geometry.BoreM / settings.ReferenceBoreM * reference / speed;
    }
    public static double PhaseAtFraction(double fraction, double exponent) =>
        Math.Pow(-Math.Log(1 - fraction * (1 - Math.Exp(-6.908))) / 6.908, 1 / (exponent + 1));
}
