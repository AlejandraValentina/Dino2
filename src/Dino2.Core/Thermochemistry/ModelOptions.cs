using Dino2.Core.FourStroke;

namespace Dino2.Core.Thermochemistry;

public enum BurnLaw { PrescribedWiebe, ConditionScaledWiebe }
public enum EvidenceStatus { Estimated, Calibrated }
public sealed record ParameterEvidence(EvidenceStatus Status, string Source, string CalibrationDataset, string ApplicableRange)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Status) || CalibrationDataset is null || string.IsNullOrWhiteSpace(Source) || string.IsNullOrWhiteSpace(ApplicableRange) ||
            (Status == EvidenceStatus.Calibrated && string.IsNullOrWhiteSpace(CalibrationDataset)))
            throw new ArgumentException("Identifique fuente, rango y, si declara calibración, conjunto de datos.");
    }
}

public sealed record ThermochemicalOptions(BurnLaw BurnLaw, double ReferenceTemperatureK, double ReferencePressurePa,
    double ReferenceResidualFraction, double ReferenceMeanPistonSpeedMPerS, double ReferenceBoreM,
    double TurbulenceToPistonSpeed, ParameterEvidence CombustionEvidence,
    ParameterEvidence HeatTransferEvidence, ParameterEvidence FrictionEvidence)
{
    public const string ModelId = "four-stroke-isooctane-frozen-v2";
    public static ThermochemicalOptions Default => new(BurnLaw.ConditionScaledWiebe, 650, 2e6, .05, 8, .08, .3,
        new(EvidenceStatus.Estimated, "Metghalchi-Keck 1982 + duración de referencia y turbulencia asumidas", "", "Iso-octano; phi 0.8–1; sin validación de motor"),
        new(EvidenceStatus.Estimated, "Hohenberg 1979; multiplicador transferido, no calibrado", "", "Correlación de otro tipo de motor; sin rango validado propio"),
        new(EvidenceStatus.Estimated, "Chen-Flynn; coeficientes transferidos MVM2012-026; D con incertidumbre dimensional de origen", "", "Sin ensayos de fricción del motor objetivo"));

    public void Validate(FourStrokeConfiguration c)
    {
        if (!Enum.IsDefined(BurnLaw) || CombustionEvidence is null || HeatTransferEvidence is null || FrictionEvidence is null)
            throw new ArgumentException("Opciones termoquímicas incompletas.");
        CombustionEvidence.Validate(); HeatTransferEvidence.Validate(); FrictionEvidence.Validate();
        double phi = SpeciesThermodynamics.StoichiometricAfr / c.Combustion.AirFuelRatio;
        if (phi < .8 - 1e-10 || phi > 1 + 1e-10)
            throw new ArgumentException($"El modelo de productos completos admite phi 0.8–1 (AFR {SpeciesThermodynamics.StoichiometricAfr:G6} a {SpeciesThermodynamics.StoichiometricAfr / .8:G6}). No calcula mezclas ricas.");
        if (Math.Abs(c.Combustion.LowerHeatingValueJPerKg / SpeciesThermodynamics.LowerHeatingValue - 1) > 1e-8)
            throw new ArgumentException("El PCI reactivo se deriva de las entalpías NASA del iso-octano gaseoso; no admite un PCI independiente.");
        if (!FiniteRange(ReferenceTemperatureK, 298, 700) || !FiniteRange(ReferencePressurePa, .4 * 101325, 50 * 101325) ||
            !FiniteRange(ReferenceResidualFraction, 0, .2) || !FiniteRange(ReferenceMeanPistonSpeedMPerS, 0, 40) ||
            !FiniteRange(ReferenceBoreM, .02, .2) || !FiniteRange(TurbulenceToPistonSpeed, 0, 2))
            throw new ArgumentException("Referencias de combustión fuera de rango: T 298–700 K, p 0.4–50 atm, residuo 0–0.2, turbulencia/Up 0–2.");
    }
    private static bool FiniteRange(double value, double lo, double hi) => double.IsFinite(value) && value >= lo && value <= hi;

    public static FourStrokeConfiguration Example => FourStrokeConfiguration.Example with
    {
        Combustion = FourStrokeConfiguration.Example.Combustion with
        {
            AirFuelRatio = SpeciesThermodynamics.StoichiometricAfr,
            LowerHeatingValueJPerKg = SpeciesThermodynamics.LowerHeatingValue,
            HeatReleaseEfficiency = .98
        },
        Thermochemistry = Default
    };
}

public readonly record struct SpeciesMasses(double FuelKg, double OxygenKg, double NitrogenKg, double CarbonDioxideKg, double WaterKg)
{
    public double Sum => FuelKg + OxygenKg + NitrogenKg + CarbonDioxideKg + WaterKg;
    public double[] ToArray() => [FuelKg, OxygenKg, NitrogenKg, CarbonDioxideKg, WaterKg];
    public static SpeciesMasses From(ReadOnlySpan<double> values) => new(values[0], values[1], values[2], values[3], values[4]);
}

public sealed record ReactiveDiagnostics(double MaximumSpeciesResidualKg, double MaximumElementResidualKg,
    double FuelConsumedKg, double BurnDurationDeg, double SocTemperatureK, double SocPressurePa,
    double SocResidualFraction, double Ca10Deg, double Ca50Deg, double Ca90Deg,
    bool FlameCorrelationExtrapolated, string ModelLimitations);
