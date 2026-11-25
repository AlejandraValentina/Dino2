using System.Text.Json.Serialization;
using Dino2.Core.GasDynamics;

namespace Dino2.Core.FourStroke;

public sealed record CylinderGeometry(double BoreM, double StrokeM, double RodLengthM, double CompressionRatio)
{
    public double DisplacementM3 => Math.PI / 4 * BoreM * BoreM * StrokeM;
}
public sealed record Reservoir(double PressurePa, double TemperatureK);
public sealed record ValveLiftPoint(double CrankAngleDeg, double LiftM);

public sealed record PoppetValve(double DiameterM, double MaximumLiftM, int Count,
    double DischargeCoefficient, double OpensDeg, double ClosesDeg)
{
    /// <summary>Optional measured lift trace over one 720° crank cycle; null selects the historical sinusoid.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ValveLiftPoint>? LiftProfile { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DischargeMap? DischargeMap { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DischargeMap? ReverseDischargeMap { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LiftProfileSource { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LiftProfileSha256 { get; init; }

    public bool Equals(PoppetValve? other) => other is not null && DiameterM == other.DiameterM &&
        MaximumLiftM == other.MaximumLiftM && Count == other.Count && DischargeCoefficient == other.DischargeCoefficient &&
        OpensDeg == other.OpensDeg && ClosesDeg == other.ClosesDeg &&
        (LiftProfile is null ? other.LiftProfile is null : other.LiftProfile is not null && LiftProfile.SequenceEqual(other.LiftProfile)) &&
        LiftProfileSource == other.LiftProfileSource && LiftProfileSha256 == other.LiftProfileSha256 &&
        (DischargeMap is null ? other.DischargeMap is null : other.DischargeMap is not null &&
            DischargeMap.Metadata == other.DischargeMap.Metadata && DischargeMap.Points.SequenceEqual(other.DischargeMap.Points)) &&
        (ReverseDischargeMap is null ? other.ReverseDischargeMap is null : other.ReverseDischargeMap is not null &&
            ReverseDischargeMap.Metadata == other.ReverseDischargeMap.Metadata && ReverseDischargeMap.Points.SequenceEqual(other.ReverseDischargeMap.Points));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DiameterM); hash.Add(MaximumLiftM); hash.Add(Count); hash.Add(DischargeCoefficient);
        hash.Add(OpensDeg); hash.Add(ClosesDeg); hash.Add(LiftProfileSource); hash.Add(LiftProfileSha256);
        if (DischargeMap is not null) { hash.Add(DischargeMap.Metadata); foreach (var point in DischargeMap.Points) hash.Add(point); }
        if (ReverseDischargeMap is not null) { hash.Add(ReverseDischargeMap.Metadata); foreach (var point in ReverseDischargeMap.Points) hash.Add(point); }
        if (LiftProfile is not null) foreach (var point in LiftProfile) hash.Add(point);
        return hash.ToHashCode();
    }
}
public sealed record Combustion(double SparkAdvanceDeg, double IgnitionDelayS, double DurationS,
    double AirFuelRatio, double LowerHeatingValueJPerKg, double HeatReleaseEfficiency, double WiebeExponent)
{
    public double? DurationDeg { get; init; }
    internal double CrankDuration(int rpm) => DurationDeg ?? 6 * rpm * DurationS;
}
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
    RpmRange Speeds, SolverSettings Solver, Thermochemistry.ThermochemicalOptions? Thermochemistry = null,
    GasDynamics.GasPath? Ducts = null)
{
    public string ModelIdentifier => Ducts?.Identifier ??
        Thermochemistry?.Identifier ?? FourStrokeResult.ModelId;

    public static FourStrokeConfiguration Example => new(
        new(.08, .08, .135, 10), new(100000, 320), new(105000, 800),
        new(.032, .008, 1, .7, -10, 220), new(.028, .007, 1, .7, 500, 730),
        new(20, .0003, .002, 14.7, 43e6, .95, 2), new(450, 1),
        // Literature transfer, NOT calibrated to this synthetic cylinder. See model documentation.
        new(50000, .006, 9000, 45), new(1000, 6000, 500), SolverSettings.Default);

    public void Validate(bool operatingPoints = true)
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
            ValidateLiftProfile(valve);
        }
        Range(IntakeValve.OpensDeg, -60, 30, "Apertura de admisión [°]");
        Range(IntakeValve.ClosesDeg, 180, 300, "Cierre de admisión [°]");
        Range(ExhaustValve.OpensDeg, 420, 570, "Apertura de escape [°]");
        Range(ExhaustValve.ClosesDeg, 700, 780, "Cierre de escape [°]");
        Range(Combustion.SparkAdvanceDeg, -10, 60, "Avance [° antes del PMS de combustión]");
        Range(Combustion.IgnitionDelayS, 0, .003, "Retraso de ignición [s]");
        Range(Combustion.DurationS, .0003, .006, "Duración de combustión [s]");
        if (Combustion.DurationDeg is double duration)
        {
            Range(duration, 5, 180, "Duración de combustión [°]");
            if (Thermochemistry?.BurnLaw == Core.Thermochemistry.BurnLaw.ConditionScaledWiebe)
                throw new ArgumentException("La duración en grados requiere la ley Wiebe prescrita.");
        }
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
        Thermochemistry?.Validate(this);
        if (Ducts is not null)
        {
            Ducts.Validate();
            if (Thermochemistry?.UsesEquilibrium != true)
                throw new ArgumentException("Los conductos 1D se acoplan al cilindro con equilibrio UV.");
        }
        if (operatingPoints)
            foreach (int rpm in Speeds.Values())
                if (OperatingPointError(rpm) is { } error) throw new ArgumentException(error);
    }

    private static void ValidateLiftProfile(PoppetValve valve)
    {
        if (valve.LiftProfile is not { } profile) return;
        if (string.IsNullOrWhiteSpace(valve.LiftProfileSource) || valve.LiftProfileSha256 is not { Length: 64 } hash ||
            hash.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("El perfil de alzada requiere nombre de fuente y SHA-256 para su trazabilidad.");
        if (profile.Count < 3 || profile[0].CrankAngleDeg != 0 || profile[^1].CrankAngleDeg != 720 ||
            profile[0].LiftM != profile[^1].LiftM)
            throw new ArgumentException("El perfil de alzada debe cubrir 0–720° de cigüeñal y cerrar periódicamente.");
        double previous = -1;
        foreach (var point in profile)
        {
            if (!double.IsFinite(point.CrankAngleDeg) || !double.IsFinite(point.LiftM) ||
                point.CrankAngleDeg <= previous || point.CrankAngleDeg < 0 || point.CrankAngleDeg > 720 ||
                point.LiftM < 0 || point.LiftM > valve.MaximumLiftM)
                throw new ArgumentException("El perfil debe tener ángulos estrictos y alzadas entre cero y la alzada máxima [SI].");
            previous = point.CrankAngleDeg;
        }
        double tolerance = Math.Max(1e-12, valve.MaximumLiftM * 1e-8);
        if (ValveFlow.Lift(valve, valve.OpensDeg) > tolerance || ValveFlow.Lift(valve, valve.ClosesDeg) > tolerance ||
            profile.Max(point => point.LiftM) <= 0)
            throw new ArgumentException("Los eventos de apertura/cierre deben coincidir con alzada cero y el perfil debe tener alzada positiva.");
    }

    internal string? OperatingPointError(int rpm)
    {
        var start = 360 - Combustion.SparkAdvanceDeg + 6 * rpm * Combustion.IgnitionDelayS;
        bool prescribed = Thermochemistry?.BurnLaw != Core.Thermochemistry.BurnLaw.ConditionScaledWiebe;
        return start <= IntakeValve.ClosesDeg || start >= ExhaustValve.OpensDeg ||
            (prescribed && start + Combustion.CrankDuration(rpm) >= ExhaustValve.OpensDeg)
            ? $"A {rpm} RPM la combustión sale del intervalo con válvulas cerradas. Revisá avance, duración y distribución."
            : null;
    }

    private static void Range(double value, double minimum, double maximum, string label)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException($"{label}: debe estar entre {minimum:G6} y {maximum:G6}.");
    }
}
