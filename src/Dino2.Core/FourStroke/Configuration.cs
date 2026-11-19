namespace Dino2.Core.FourStroke;

public sealed record CylinderGeometry(double BoreM, double StrokeM, double RodLengthM, double CompressionRatio)
{
    public double DisplacementM3 => Math.PI / 4 * BoreM * BoreM * StrokeM;
}
public sealed record Reservoir(double PressurePa, double TemperatureK);
public sealed record PoppetValve(double DiameterM, double MaximumLiftM, int Count,
    double DischargeCoefficient, double OpensDeg, double ClosesDeg);
public sealed record Combustion(double SparkAdvanceDeg, double IgnitionDelayS, double DurationS,
    double AirFuelRatio, double LowerHeatingValueJPerKg, double HeatReleaseEfficiency, double WiebeExponent);
public sealed record ThermalBoundary(double WallTemperatureK, double HohenbergMultiplier);
public sealed record MechanicalLosses(double ConstantPa, double PeakPressureFactor,
    double SpeedPaPerMPerS, double SpeedSquaredPaPerM2PerS2);
public sealed record RpmRange(int Start, int End, int Step)
{
    public void Validate()
    {
        if (Start < 500 || End > 10000 || End < Start || Step < 1 || Step > 10000 || (End - Start) / Step + 1 > 100)
            throw new ArgumentException("Rango 4T: 500–10000 RPM, fin ≥ inicio, paso positivo y hasta 100 puntos.");
    }
    public IEnumerable<int> Values()
    {
        Validate();
        for (int rpm = Start; rpm <= End; rpm += Step) yield return rpm;
    }
}
public sealed record SolverSettings(double MaximumStepDeg, double RelativeTolerance,
    double PeriodicTolerance, int MaximumCycles)
{
    public static SolverSettings Default => new(.5, 2e-7, 1e-6, 60);
}

// The SI snapshot is specific to the open, pseudo-air, premixed 4T model.
// It does not change the independent Otto reference or its file schema.
public sealed record FourStrokeConfiguration(CylinderGeometry Geometry,
    Reservoir Intake, Reservoir Exhaust, PoppetValve IntakeValve, PoppetValve ExhaustValve,
    Combustion Combustion, ThermalBoundary Thermal, MechanicalLosses Friction,
    RpmRange Speeds, SolverSettings Solver)
{
    public static FourStrokeConfiguration Example => new(
        new(.08, .08, .135, 10), new(100000, 320), new(105000, 800),
        new(.032, .008, 1, .7, -10, 220), new(.028, .007, 1, .7, 500, 730),
        new(20, .0003, .002, 14.7, 43e6, .95, 2), new(450, 1),
        // Literature transfer, NOT calibrated to this synthetic cylinder. See model documentation.
        new(50000, .006, 9000, 45), new(1000, 6000, 500), SolverSettings.Default);

    public void Validate()
    {
        if (Geometry is null || Intake is null || Exhaust is null || IntakeValve is null || ExhaustValve is null ||
            Combustion is null || Thermal is null || Friction is null || Speeds is null || Solver is null)
            throw new ArgumentException("La configuración 4T contiene una sección nula.");
        Range(Geometry.BoreM, .02, .2, "Diámetro [m]");
        Range(Geometry.StrokeM, .02, .2, "Carrera [m]");
        Range(Geometry.RodLengthM, Geometry.StrokeM * .75, .5, "Longitud de biela [m]");
        Range(Geometry.CompressionRatio, 4, 16, "Compresión");
        foreach (var reservoir in new[] { Intake, Exhaust })
        {
            Range(reservoir.PressurePa, 20000, 300000, "Presión absoluta de depósito [Pa]");
            Range(reservoir.TemperatureK, 250, 1500, "Temperatura de depósito [K]");
        }
        foreach (var valve in new[] { IntakeValve, ExhaustValve })
        {
            Range(valve.DiameterM, .005, Geometry.BoreM * .7, "Diámetro de válvula [m]");
            Range(valve.MaximumLiftM, .0001, .025, "Alzada [m]");
            Range(valve.DischargeCoefficient, .1, 1, "Coeficiente de descarga");
            if (valve.Count < 1 || valve.Count > 3) throw new ArgumentException("Entre 1 y 3 válvulas por conducto.");
        }
        Range(IntakeValve.OpensDeg, -60, 30, "Apertura de admisión [°]");
        Range(IntakeValve.ClosesDeg, 180, 300, "Cierre de admisión [°]");
        Range(ExhaustValve.OpensDeg, 420, 570, "Apertura de escape [°]");
        Range(ExhaustValve.ClosesDeg, 700, 780, "Cierre de escape [°]");
        Range(Combustion.SparkAdvanceDeg, -10, 60, "Avance [° antes del PMS de combustión]");
        Range(Combustion.IgnitionDelayS, 0, .003, "Retraso de ignición [s]");
        Range(Combustion.DurationS, .0003, .006, "Duración de combustión [s]");
        Range(Combustion.AirFuelRatio, 12, 25, "Relación aire/combustible");
        Range(Combustion.LowerHeatingValueJPerKg, 0, 50e6, "PCI [J/kg]");
        Range(Combustion.HeatReleaseEfficiency, 0, 1, "Fracción de energía liberada");
        Range(Combustion.WiebeExponent, 1, 5, "Exponente Wiebe");
        Range(Thermal.WallTemperatureK, 250, 700, "Temperatura de pared [K]");
        Range(Thermal.HohenbergMultiplier, 0, 3, "Multiplicador Hohenberg");
        Range(Friction.ConstantPa, 0, 300000, "Fricción constante [Pa]");
        Range(Friction.PeakPressureFactor, 0, .1, "Fricción por presión pico");
        Range(Friction.SpeedPaPerMPerS, 0, 30000, "Fricción lineal con velocidad");
        Range(Friction.SpeedSquaredPaPerM2PerS2, 0, 1000, "Fricción cuadrática con velocidad");
        Range(Solver.MaximumStepDeg, .025, 4, "Paso angular máximo [°]");
        Range(Solver.RelativeTolerance, 1e-10, 1e-3, "Tolerancia local relativa");
        Range(Solver.PeriodicTolerance, 1e-9, 1e-3, "Tolerancia periódica relativa");
        if (Solver.MaximumCycles < 3 || Solver.MaximumCycles > 200) throw new ArgumentException("Máximo de ciclos entre 3 y 200.");
        Speeds.Validate();
        foreach (int rpm in Speeds.Values())
        {
            var start = 360 - Combustion.SparkAdvanceDeg + 6 * rpm * Combustion.IgnitionDelayS;
            if (start <= IntakeValve.ClosesDeg || start + 6 * rpm * Combustion.DurationS >= ExhaustValve.OpensDeg)
                throw new ArgumentException($"A {rpm} RPM la combustión sale del intervalo con válvulas cerradas. Revise avance, duración y distribución.");
        }
    }

    private static void Range(double value, double minimum, double maximum, string label)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException($"{label}: debe estar entre {minimum:G6} y {maximum:G6}.");
    }
}
