using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;

namespace Dino2.Core.Studies;

public enum StudyParameter
{
    Spark, Compression, IntakeLift, ExhaustLift, IntakeOpen, IntakeClose, ExhaustOpen, ExhaustClose,
    IntakeLength, ExhaustLength, IntakeOuterDiameter, IntakeValveDiameter, ExhaustOuterDiameter, ExhaustValveDiameter,
    IntakePressure, ExhaustPressure, IntakeTemperature, ExhaustTemperature
}

public sealed record ParameterRange(StudyParameter Parameter, double Start, double End, double Increment);
public sealed record ParameterSample(StudyParameter Parameter, double Value);
public sealed record ParameterSpec(StudyParameter Id, string Label, string Unit, double Minimum, double Maximum,
    bool RequiresDucts, Func<FourStrokeConfiguration, double> Read,
    Func<FourStrokeConfiguration, double, FourStrokeConfiguration> Write)
{
    public string Display => $"{Label} [{Unit}]";
}

public static class StudyParameters
{
    public static IReadOnlyList<ParameterSpec> All { get; } = Array.AsReadOnly<ParameterSpec>(
    [
        new(StudyParameter.Spark, "Avance de encendido", "° APMS", -10, 60, false,
            c => c.Combustion.SparkAdvanceDeg, (c, v) => c with { Combustion = c.Combustion with { SparkAdvanceDeg = v } }),
        new(StudyParameter.Compression, "Relación de compresión", "–", 4, 16, false,
            c => c.Geometry.CompressionRatio, (c, v) => c with { Geometry = c.Geometry with { CompressionRatio = v } }),
        new(StudyParameter.IntakeLift, "Alzada de admisión", "mm", .1, 25, false,
            c => c.IntakeValve.MaximumLiftM * 1000, (c, v) => c with { IntakeValve = c.IntakeValve with { MaximumLiftM = v / 1000 } }),
        new(StudyParameter.ExhaustLift, "Alzada de escape", "mm", .1, 25, false,
            c => c.ExhaustValve.MaximumLiftM * 1000, (c, v) => c with { ExhaustValve = c.ExhaustValve with { MaximumLiftM = v / 1000 } }),
        new(StudyParameter.IntakeOpen, "Apertura de admisión", "° ciclo", -60, 30, false,
            c => c.IntakeValve.OpensDeg, (c, v) => c with { IntakeValve = c.IntakeValve with { OpensDeg = v } }),
        new(StudyParameter.IntakeClose, "Cierre de admisión", "° ciclo", 180, 300, false,
            c => c.IntakeValve.ClosesDeg, (c, v) => c with { IntakeValve = c.IntakeValve with { ClosesDeg = v } }),
        new(StudyParameter.ExhaustOpen, "Apertura de escape", "° ciclo", 420, 570, false,
            c => c.ExhaustValve.OpensDeg, (c, v) => c with { ExhaustValve = c.ExhaustValve with { OpensDeg = v } }),
        new(StudyParameter.ExhaustClose, "Cierre de escape", "° ciclo", 700, 780, false,
            c => c.ExhaustValve.ClosesDeg, (c, v) => c with { ExhaustValve = c.ExhaustValve with { ClosesDeg = v } }),
        Duct(StudyParameter.IntakeLength, "Longitud de admisión", true, 80, 3000, d => d.LengthM, (d, v) => d with { LengthM = v }),
        Duct(StudyParameter.ExhaustLength, "Longitud de escape", false, 80, 3000, d => d.LengthM, (d, v) => d with { LengthM = v }),
        Duct(StudyParameter.IntakeOuterDiameter, "Diámetro de admisión en depósito", true, 15, 150, d => d.ReservoirDiameterM, (d, v) => d with { ReservoirDiameterM = v }),
        Duct(StudyParameter.IntakeValveDiameter, "Diámetro del conducto de admisión en válvula", true, 15, 150, d => d.ValveDiameterM, (d, v) => d with { ValveDiameterM = v }),
        Duct(StudyParameter.ExhaustOuterDiameter, "Diámetro de escape en depósito", false, 15, 150, d => d.ReservoirDiameterM, (d, v) => d with { ReservoirDiameterM = v }),
        Duct(StudyParameter.ExhaustValveDiameter, "Diámetro del conducto de escape en válvula", false, 15, 150, d => d.ValveDiameterM, (d, v) => d with { ValveDiameterM = v }),
        new(StudyParameter.IntakePressure, "Presión absoluta de admisión", "kPa", 20, 300, false,
            c => c.Intake.PressurePa / 1000, (c, v) => c with { Intake = c.Intake with { PressurePa = v * 1000 } }),
        new(StudyParameter.ExhaustPressure, "Presión absoluta de escape", "kPa", 20, 300, false,
            c => c.Exhaust.PressurePa / 1000, (c, v) => c with { Exhaust = c.Exhaust with { PressurePa = v * 1000 } }),
        new(StudyParameter.IntakeTemperature, "Temperatura de admisión", "K", 250, 1500, false,
            c => c.Intake.TemperatureK, (c, v) => c with { Intake = c.Intake with { TemperatureK = v } }),
        new(StudyParameter.ExhaustTemperature, "Temperatura de escape", "K", 250, 1500, false,
            c => c.Exhaust.TemperatureK, (c, v) => c with { Exhaust = c.Exhaust with { TemperatureK = v } })
    ]);

    public static ParameterSpec Get(StudyParameter id) => All.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException("Parámetro de estudio desconocido.");

    private static ParameterSpec Duct(StudyParameter id, string label, bool intake, double min, double max,
        Func<DuctGeometry, double> read, Func<DuctGeometry, double, DuctGeometry> write) =>
        new(id, label, "mm", min, max, true,
            c => read(intake ? c.Ducts!.Intake : c.Ducts!.Exhaust) * 1000, (c, v) =>
            {
                var ducts = c.Ducts!;
                return c with { Ducts = intake ? ducts with { Intake = write(ducts.Intake, v / 1000) }
                    : ducts with { Exhaust = write(ducts.Exhaust, v / 1000) } };
            });
}
