using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;
using System.Text.Json.Serialization;

namespace Dino2.Core.TwoStroke;

public sealed record PistonPort(double RoofM, double HeightM, double WidthM, double ThroatM2, double Cd)
{
    public double Area(double pistonTravelM) => Cd * Math.Min(ThroatM2,
        WidthM * Math.Clamp(pistonTravelM - RoofM, 0, HeightM));

    public double Opening(CylinderGeometry geometry)
    {
        double low = 0, high = 180;
        for (int i = 0; i < 50; i++)
        {
            double mid = (low + high) / 2;
            if (TwoStrokeGeometry.Travel(geometry, mid) < RoofM) low = mid;
            else high = mid;
        }
        return (low + high) / 2;
    }
}

public sealed record Reed(double ThroatM2, double PerimeterM, double LimitM, double StiffnessNPerM,
    double OpeningPressurePa, double Cd)
{
    public double EffectiveArea(double pressureDifference) => Cd * Math.Min(ThroatM2,
        PerimeterM * Math.Clamp((pressureDifference - OpeningPressurePa) * ThroatM2 / StiffnessNPerM, 0, LimitM));
}

// Listed outward, from the cylinder to the tail. Diameter is linear within each conical section.
public sealed record ExpansionChamber(double InletDiameterM, double HeaderM, double DiffuserM, double BellyDiameterM,
    double BellyM, double ConeM, double StingerDiameterM, double StingerM, int Cells)
{
    public DuctWall? Wall { get; init; }
    public double LengthM => HeaderM + DiffuserM + BellyM + ConeM + StingerM;
    internal double[] MeshKnots() => [0, StingerM, StingerM + ConeM, StingerM + ConeM + BellyM, LengthM - HeaderM, LengthM];
    public double DiameterFromTail(double x)
    {
        if (x <= StingerM) return StingerDiameterM;
        x -= StingerM;
        if (x <= ConeM) return StingerDiameterM + (BellyDiameterM - StingerDiameterM) * x / ConeM;
        x -= ConeM + BellyM;
        if (x <= 0) return BellyDiameterM;
        if (x <= DiffuserM) return BellyDiameterM + (InletDiameterM - BellyDiameterM) * x / DiffuserM;
        return InletDiameterM;
    }
}

public static class TwoStrokeGeometry
{
    public static double Travel(CylinderGeometry geometry, double angle) =>
        (SliderCrank.At(geometry, angle).VolumeM3 - Clearance(geometry)) * geometry.StrokeM / geometry.DisplacementM3;
    public static double Clearance(CylinderGeometry geometry) => geometry.DisplacementM3 / (geometry.CompressionRatio - 1);
    public static double CrankcaseVolume(CylinderGeometry geometry, double minimum, double angle) =>
        minimum + geometry.DisplacementM3 + Clearance(geometry) - SliderCrank.At(geometry, angle).VolumeM3;
}

public sealed record TwoStrokeCombustion(double AdvanceDeg, double DelayS, double DurationS,
    double AirFuelRatio, double BurnFraction, double WiebeExponent, bool ProductEquilibrium)
{
    public double? DurationDeg { get; init; }
    internal double CrankDuration(int rpm) => DurationDeg ?? 6 * rpm * DurationS;
}
public sealed record TwoStrokeNumerics(double MaximumStepDeg, double Courant, double PeriodicTolerance,
    int MaximumCycles, DuctScheme Scheme) : IJsonOnDeserializing
{
    private ExteriorBoundary exterior = ExteriorBoundary.PressureReservoir;
    public ExteriorBoundary Exterior { get => exterior; init => exterior = value; }
    void IJsonOnDeserializing.OnDeserializing() => exterior = ExteriorBoundary.UnspecifiedLegacy;
    public string? BoundaryNotice() => Exterior switch
    {
        ExteriorBoundary.PressureReservoir => null,
        ExteriorBoundary.UnspecifiedLegacy => "LEGACY / NO PREDICTIVO · contorno ausente en el archivo histórico.",
        _ => "LEGACY / NO PREDICTIVO · extremo adaptado."
    };
}

public sealed record TwoStrokeConfiguration(CylinderGeometry Geometry, double CrankcaseMinimumM3,
    Reservoir Intake, Reservoir Tail, Reed Reed, PistonPort Transfer, PistonPort Exhaust,
    DuctGeometry IntakeDuct, ExpansionChamber Expansion, double FreshExtractionRatio,
    TwoStrokeCombustion Combustion, ThermalBoundary Thermal, MechanicalLosses Friction,
    RpmRange Speeds, TwoStrokeNumerics Numerics)
{
    public const string ModelId = "two-stroke-reed-expansion-isooctane-v1";
    public const string NumericalRevision = "two-stroke-ssprk2-passive-limiter-v2";
    public string Revision => Stage11Revision + (Numerics.Exterior == ExteriorBoundary.PressureReservoir ? "-ports-v2" : "");
    [JsonIgnore]
    public string Stage12Revision => Stage11Revision + (Numerics.Exterior == ExteriorBoundary.PressureReservoir ? "-ports-v1" : "");
    [JsonIgnore]
    public string Stage11Revision => NumericalRevision +
        (Numerics.Exterior == ExteriorBoundary.PressureReservoir ? "-pressure-boundary" : "") +
        (IntakeDuct.Wall is not null || Expansion.Wall is not null ? "-wall" : "") +
        (Combustion.DurationDeg.HasValue ? "-wiebe-angle" : "");
    public double TrappedCompressionRatio =>
        SliderCrank.At(Geometry, 360 - Exhaust.Opening(Geometry)).VolumeM3 / TwoStrokeGeometry.Clearance(Geometry);
    public double PrimaryCompressionRatio => 1 + Geometry.DisplacementM3 / CrankcaseMinimumM3;

    public static TwoStrokeConfiguration Example => new(
        new(.054, .0545, .11, 11), .000180, new(100000, 320), new(100000, 300),
        new(.00049, .11, .004, 850, 40, .7), new(.043, .011, .052, .0008, .73),
        new(.031, .022, .036, .0008, .74), new(.14, .031, .031, 12),
        new(.035, .17, .27, .085, .13, .20, .018, .13, 32), 1,
        new(19, .00016, .0011, SpeciesThermodynamics.StoichiometricAfr, .96, 2, true),
        new(440, 1), new(42000, .004, 4500, 45), new(4000, 6000, 1000),
        new(.65, .44, 5e-5, 160, DuctScheme.Muscl) { Exterior = ExteriorBoundary.HistoricalRiemann });

    public static TwoStrokeConfiguration RecoveryExample
    {
        get
        {
            var example = Example;
            return example with { Numerics = example.Numerics with { Exterior = ExteriorBoundary.PressureReservoir } };
        }
    }

    public void Validate()
    {
        if (Geometry is null || Intake is null || Tail is null || Reed is null || Transfer is null ||
            Exhaust is null || IntakeDuct is null || Expansion is null || Combustion is null || Thermal is null ||
            Friction is null || Speeds is null || Numerics is null)
            throw new ArgumentException("La configuración 2T contiene una sección nula.");
        Check(Geometry.BoreM, .02, .12, "Diámetro");
        Check(Geometry.StrokeM, .02, .12, "Carrera");
        Check(Geometry.RodLengthM, Geometry.StrokeM * .75, .3, "Biela");
        Check(Geometry.CompressionRatio, 4, 16, "Compresión geométrica");
        Check(CrankcaseMinimumM3 / Geometry.DisplacementM3, .7, 5, "Volumen mínimo del cárter / cilindrada");
        foreach (var reservoir in new[] { Intake, Tail })
        {
            Check(reservoir.PressurePa, 50000, 130000, "Presión absoluta");
            Check(reservoir.TemperatureK, 260, 550, "Temperatura de depósito");
        }
        foreach (var port in new[] { Transfer, Exhaust })
        {
            Check(port.RoofM, .15 * Geometry.StrokeM, .97 * Geometry.StrokeM, "Techo de lumbrera");
            Check(port.HeightM, .0005, Geometry.StrokeM - port.RoofM, "Altura de lumbrera");
            Check(port.WidthM, .001, Math.PI * Geometry.BoreM, "Ancho agregado de lumbrera");
            Check(port.ThroatM2, 1e-6, Geometry.DisplacementM3 / Geometry.StrokeM, "Garganta de lumbrera");
            Check(port.Cd, .1, 1, "Cd de lumbrera");
        }
        if (Exhaust.RoofM >= Transfer.RoofM)
            throw new ArgumentException("El escape debe abrir antes y cerrar después que los transfers.");
        Check(Reed.ThroatM2, 1e-5, .002, "Garganta de láminas");
        Check(Reed.PerimeterM, .01, .4, "Perímetro de láminas");
        Check(Reed.LimitM, .0002, .012, "Tope de láminas");
        Check(Reed.StiffnessNPerM, 50, 10000, "Rigidez de láminas");
        Check(Reed.OpeningPressurePa, 0, 3000, "Umbral de láminas");
        Check(Reed.Cd, .1, 1, "Cd de láminas");
        IntakeDuct.Validate();
        foreach (double length in new[] { Expansion.HeaderM, Expansion.DiffuserM, Expansion.BellyM, Expansion.ConeM, Expansion.StingerM })
            Check(length, .035, .8, "Longitud de sección del escape");
        Check(Expansion.InletDiameterM, .015, .12, "Diámetro de colector");
        Check(Expansion.StingerDiameterM, .012, Expansion.InletDiameterM, "Diámetro de salida");
        Check(Expansion.BellyDiameterM, Expansion.InletDiameterM, .18, "Diámetro central");
        if (Expansion.Cells is < 12 or > 256) throw new ArgumentException("Escape: 12–256 celdas.");
        Expansion.Wall?.Validate(Expansion.StingerDiameterM);
        Check(FreshExtractionRatio, .2, 5, "Preferencia de extracción de mezcla fresca");
        Check(Combustion.AdvanceDeg, -10, 50, "Avance");
        Check(Combustion.DelayS, 0, .002, "Demora de encendido");
        Check(Combustion.DurationS, .0003, .004, "Duración de combustión");
        if (Combustion.DurationDeg is double duration) Check(duration, 5, 120, "Duración de combustión [°]");
        Check(Combustion.AirFuelRatio, 11, 22, "Relación aire/combustible");
        Check(Combustion.BurnFraction, 0, 1, "Fracción quemada");
        Check(Combustion.WiebeExponent, 1, 5, "Exponente Wiebe");
        Check(Thermal.WallTemperatureK, 260, 700, "Temperatura de pared");
        Check(Thermal.HohenbergMultiplier, 0, 3, "Factor Hohenberg");
        Check(Friction.ConstantPa, 0, 300000, "Fricción constante");
        Check(Friction.PeakPressureFactor, 0, .1, "Fricción por presión");
        Check(Friction.SpeedPaPerMPerS, 0, 30000, "Fricción lineal");
        Check(Friction.SpeedSquaredPaPerM2PerS2, 0, 1000, "Fricción cuadrática");
        Check(Numerics.MaximumStepDeg, .02, 3, "Paso angular");
        Check(Numerics.Courant, .05, .5, "CFL");
        Check(Numerics.PeriodicTolerance, 1e-8, .001, "Tolerancia periódica");
        if (Numerics.MaximumCycles is < 3 or > 200 || !Enum.IsDefined(Numerics.Scheme) || !Enum.IsDefined(Numerics.Exterior))
            throw new ArgumentException("Se requieren de 3 a 200 ciclos y un esquema 1D válido.");
        if (Speeds.Start < 500 || Speeds.End > 10000 || Speeds.End < Speeds.Start || Speeds.Step is < 1 or > 10000 ||
            (Speeds.End - Speeds.Start) / Speeds.Step + 1 > 100)
            throw new ArgumentException("Rango 2T: 500–10000 RPM, fin ≥ inicio, paso positivo y hasta 100 puntos.");
    }

    internal void ValidateOperatingPoint(int rpm)
    {
        if (Combustion.AirFuelRatio < SpeciesThermodynamics.StoichiometricAfr * (1 - 1e-10))
            throw new ModelDomainException($"{rpm} RPM: la oxidación parcial de mezclas ricas está fuera de dominio del modelo 2T.");
        double opening = Exhaust.Opening(Geometry);
        double start = 360 - Combustion.AdvanceDeg + 6 * rpm * Combustion.DelayS;
        double end = start + Combustion.CrankDuration(rpm);
        if (start <= 360 - opening || end >= 360 + opening)
            throw new ModelDomainException(
                $"A {rpm} RPM, la combustión prevista ({start:F2}–{end:F2}°) sale del tramo con lumbreras cerradas ({360 - opening:F2}–{360 + opening:F2}°).");
    }

    private static void Check(double value, double min, double max, string label)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{label}: {min:G5}–{max:G5} en unidades SI.");
    }
}
