namespace Dino2.Core;

// Public boundaries use SI exclusively; UI conversions belong to the desktop project.
public sealed record OttoConfiguration(
    double BoreM,
    double StrokeM,
    double CompressionRatio,
    double InitialPressurePa,
    double InitialTemperatureK,
    double HeatInputJPerKg,
    double Gamma,
    double GasConstantJPerKgK,
    int RpmStart,
    int RpmEnd,
    int RpmStep)
{
    public static OttoConfiguration Example => new(0.08, 0.08, 10, 100_000, 300,
        800_000, 1.4, 287, 1000, 6000, 500);

    public void Validate()
    {
        Range(BoreM, 0.001, 1, "Diámetro [m]");
        Range(StrokeM, 0.001, 1, "Carrera [m]");
        Range(CompressionRatio, 1.01, 30, "Relación de compresión");
        Range(InitialPressurePa, 1000, 1_000_000, "Presión inicial absoluta [Pa]");
        Range(InitialTemperatureK, 150, 1500, "Temperatura inicial [K]");
        Range(HeatInputJPerKg, 0, 3_000_000, "Calor aportado [J/kg]");
        Range(Gamma, 1.05, 1.67, "Gamma");
        Range(GasConstantJPerKgK, 200, 400, "Constante del gas [J/(kg K)]");
        if (RpmStart < 1 || RpmEnd > 30_000 || RpmEnd < RpmStart || RpmStep < 1 || RpmStep > 30_000)
            throw new ArgumentException("RPM: inicio ≥ 1, fin ≤ 30000, fin ≥ inicio y paso entre 1 y 30000.");
        if ((RpmEnd - RpmStart) / RpmStep + 1 > 2000)
            throw new ArgumentException("El barrido admite hasta 2000 puntos; aumente el paso RPM.");
    }

    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{name}: debe estar entre {min} y {max}.");
    }
}
