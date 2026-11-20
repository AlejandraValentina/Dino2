using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;

namespace Dino2.Desktop;

internal sealed record EngineParameter(string Key, string Group, string Label,
    Func<FourStrokeConfiguration, double> Get, Func<FourStrokeConfiguration, double, FourStrokeConfiguration> Set,
    double Scale = 1, bool Integer = false)
{
    public static IReadOnlyList<EngineParameter> All { get; } = Standard.Concat(DuctParameters()).ToArray();

    private static EngineParameter[] Standard =>
    [
        new("Bore", "Geometría", "Diámetro [mm]", c => c.Geometry.BoreM, (c,v) => c with { Geometry = c.Geometry with { BoreM = v } }, 1000),
        new("Stroke", "Geometría", "Carrera [mm]", c => c.Geometry.StrokeM, (c,v) => c with { Geometry = c.Geometry with { StrokeM = v } }, 1000),
        new("Rod", "Geometría", "Biela entre centros [mm]", c => c.Geometry.RodLengthM, (c,v) => c with { Geometry = c.Geometry with { RodLengthM = v } }, 1000),
        new("Compression", "Geometría", "Relación de compresión", c => c.Geometry.CompressionRatio, (c,v) => c with { Geometry = c.Geometry with { CompressionRatio = v } }),
        new("IntakeDiameter", "Válvulas", "Admisión · diámetro [mm]", c => c.IntakeValve.DiameterM, (c,v) => c with { IntakeValve = c.IntakeValve with { DiameterM = v } }, 1000),
        new("IntakeLift", "Válvulas", "Admisión · alzada máxima [mm]", c => c.IntakeValve.MaximumLiftM, (c,v) => c with { IntakeValve = c.IntakeValve with { MaximumLiftM = v } }, 1000),
        new("IntakeCount", "Válvulas", "Admisión · cantidad", c => c.IntakeValve.Count, (c,v) => c with { IntakeValve = c.IntakeValve with { Count = (int)v } }, Integer:true),
        new("IntakeCd", "Válvulas", "Admisión · coef. descarga", c => c.IntakeValve.DischargeCoefficient, (c,v) => c with { IntakeValve = c.IntakeValve with { DischargeCoefficient = v } }),
        new("Ivo", "Válvulas", "Admisión · abre [° desde PMS admisión]", c => c.IntakeValve.OpensDeg, (c,v) => c with { IntakeValve = c.IntakeValve with { OpensDeg = v } }),
        new("Ivc", "Válvulas", "Admisión · cierra [°]", c => c.IntakeValve.ClosesDeg, (c,v) => c with { IntakeValve = c.IntakeValve with { ClosesDeg = v } }),
        new("ExhaustDiameter", "Válvulas", "Escape · diámetro [mm]", c => c.ExhaustValve.DiameterM, (c,v) => c with { ExhaustValve = c.ExhaustValve with { DiameterM = v } }, 1000),
        new("ExhaustLift", "Válvulas", "Escape · alzada máxima [mm]", c => c.ExhaustValve.MaximumLiftM, (c,v) => c with { ExhaustValve = c.ExhaustValve with { MaximumLiftM = v } }, 1000),
        new("ExhaustCount", "Válvulas", "Escape · cantidad", c => c.ExhaustValve.Count, (c,v) => c with { ExhaustValve = c.ExhaustValve with { Count = (int)v } }, Integer:true),
        new("ExhaustCd", "Válvulas", "Escape · coef. descarga", c => c.ExhaustValve.DischargeCoefficient, (c,v) => c with { ExhaustValve = c.ExhaustValve with { DischargeCoefficient = v } }),
        new("Evo", "Válvulas", "Escape · abre [°]", c => c.ExhaustValve.OpensDeg, (c,v) => c with { ExhaustValve = c.ExhaustValve with { OpensDeg = v } }),
        new("Evc", "Válvulas", "Escape · cierra [°]", c => c.ExhaustValve.ClosesDeg, (c,v) => c with { ExhaustValve = c.ExhaustValve with { ClosesDeg = v } }),
        new("Spark", "Combustión", "Avance [° antes PMS combustión]", c => c.Combustion.SparkAdvanceDeg, (c,v) => c with { Combustion = c.Combustion with { SparkAdvanceDeg = v } }),
        new("Delay", "Combustión", "Retraso ignición [ms]", c => c.Combustion.IgnitionDelayS, (c,v) => c with { Combustion = c.Combustion with { IgnitionDelayS = v } }, 1000),
        new("Duration", "Combustión", "Duración prescrita [ms]", c => c.Combustion.DurationS, (c,v) => c with { Combustion = c.Combustion with { DurationS = v } }, 1000),
        new("DurationDeg", "Combustión", "Duración prescrita [°] · 0 usa ms", c => c.Combustion.DurationDeg ?? 0,
            (c,v) => c with { Combustion = c.Combustion with { DurationDeg = v == 0 ? null : v } }),
        new("Afr", "Combustión", "Relación aire/combustible [kg/kg]", c => c.Combustion.AirFuelRatio, (c,v) => c with { Combustion = c.Combustion with { AirFuelRatio = v } }),
        new("Lhv", "Combustión", "PCI [MJ/kg combustible]", c => c.Combustion.LowerHeatingValueJPerKg, (c,v) => c with { Combustion = c.Combustion with { LowerHeatingValueJPerKg = v } }, 1e-6),
        new("BurnEfficiency", "Combustión", "Fracción de energía liberada [0–1]", c => c.Combustion.HeatReleaseEfficiency, (c,v) => c with { Combustion = c.Combustion with { HeatReleaseEfficiency = v } }),
        new("Wiebe", "Combustión", "Exponente Wiebe", c => c.Combustion.WiebeExponent, (c,v) => c with { Combustion = c.Combustion with { WiebeExponent = v } }),
        new("ReferenceT", "Comb. v2", "T de referencia [K]", c => (c.Thermochemistry??ThermochemicalOptions.Default).ReferenceTemperatureK, (c,v) => c.Thermochemistry is null?c:c with{Thermochemistry=c.Thermochemistry with{ReferenceTemperatureK=v}}),
        new("ReferenceP", "Comb. v2", "p de referencia [kPa]", c => (c.Thermochemistry??ThermochemicalOptions.Default).ReferencePressurePa, (c,v) => c.Thermochemistry is null?c:c with{Thermochemistry=c.Thermochemistry with{ReferencePressurePa=v}},.001),
        new("ReferenceResidual", "Comb. v2", "Residuo de referencia [0–0.2]", c => (c.Thermochemistry??ThermochemicalOptions.Default).ReferenceResidualFraction, (c,v) => c.Thermochemistry is null?c:c with{Thermochemistry=c.Thermochemistry with{ReferenceResidualFraction=v}}),
        new("ReferencePhi", "Comb. v2", "Equivalencia de referencia [0.8–1]", c => (c.Thermochemistry??ThermochemicalOptions.Default).ReferenceEquivalenceRatio, (c,v) => c.Thermochemistry is null?c:c with{Thermochemistry=c.Thermochemistry with{ReferenceEquivalenceRatio=v}}),
        new("ReferenceSpeed", "Comb. v2", "Up de referencia [m/s]", c => (c.Thermochemistry??ThermochemicalOptions.Default).ReferenceMeanPistonSpeedMPerS, (c,v) => c.Thermochemistry is null?c:c with{Thermochemistry=c.Thermochemistry with{ReferenceMeanPistonSpeedMPerS=v}}),
        new("ReferenceBore", "Comb. v2", "Diámetro de referencia [mm]", c => (c.Thermochemistry??ThermochemicalOptions.Default).ReferenceBoreM, (c,v) => c.Thermochemistry is null?c:c with{Thermochemistry=c.Thermochemistry with{ReferenceBoreM=v}},1000),
        new("Turbulence", "Comb. v2", "u turbulenta / Up (estimado)", c => (c.Thermochemistry??ThermochemicalOptions.Default).TurbulenceToPistonSpeed, (c,v) => c.Thermochemistry is null?c:c with{Thermochemistry=c.Thermochemistry with{TurbulenceToPistonSpeed=v}}),
        new("IntakePressure", "Contornos", "Depósito admisión [kPa absolutos]", c => c.Intake.PressurePa, (c,v) => c with { Intake = c.Intake with { PressurePa = v } }, .001),
        new("IntakeTemperature", "Contornos", "Depósito admisión [K]", c => c.Intake.TemperatureK, (c,v) => c with { Intake = c.Intake with { TemperatureK = v } }),
        new("ExhaustPressure", "Contornos", "Depósito escape [kPa absolutos]", c => c.Exhaust.PressurePa, (c,v) => c with { Exhaust = c.Exhaust with { PressurePa = v } }, .001),
        new("ExhaustTemperature", "Contornos", "Depósito escape [K]", c => c.Exhaust.TemperatureK, (c,v) => c with { Exhaust = c.Exhaust with { TemperatureK = v } }),
        new("WallTemperature", "Contornos", "Pared uniforme [K]", c => c.Thermal.WallTemperatureK, (c,v) => c with { Thermal = c.Thermal with { WallTemperatureK = v } }),
        new("HeatMultiplier", "Contornos", "Multiplicador Hohenberg", c => c.Thermal.HohenbergMultiplier, (c,v) => c with { Thermal = c.Thermal with { HohenbergMultiplier = v } }),
        new("FrictionA", "Fricción", "A constante [kPa]", c => c.Friction.ConstantPa, (c,v) => c with { Friction = c.Friction with { ConstantPa = v } }, .001),
        new("FrictionB", "Fricción", "B presión pico [–]", c => c.Friction.PeakPressureFactor, (c,v) => c with { Friction = c.Friction with { PeakPressureFactor = v } }),
        new("FrictionC", "Fricción", "C velocidad [Pa/(m/s)]", c => c.Friction.SpeedPaPerMPerS, (c,v) => c with { Friction = c.Friction with { SpeedPaPerMPerS = v } }),
        new("FrictionD", "Fricción", "D velocidad² [Pa/(m/s)²]", c => c.Friction.SpeedSquaredPaPerM2PerS2, (c,v) => c with { Friction = c.Friction with { SpeedSquaredPaPerM2PerS2 = v } }),
        new("RpmStart", "RPM / cálculo", "RPM inicial", c => c.Speeds.Start, (c,v) => c with { Speeds = c.Speeds with { Start = (int)v } }, Integer:true),
        new("RpmEnd", "RPM / cálculo", "RPM final", c => c.Speeds.End, (c,v) => c with { Speeds = c.Speeds with { End = (int)v } }, Integer:true),
        new("RpmStep", "RPM / cálculo", "Paso RPM", c => c.Speeds.Step, (c,v) => c with { Speeds = c.Speeds with { Step = (int)v } }, Integer:true),
        new("AngularStep", "RPM / cálculo", "Paso angular máximo [°]", c => c.Solver.MaximumStepDeg, (c,v) => c with { Solver = c.Solver with { MaximumStepDeg = v } }),
        new("LocalTolerance", "RPM / cálculo", "Tolerancia local relativa", c => c.Solver.RelativeTolerance, (c,v) => c with { Solver = c.Solver with { RelativeTolerance = v } }),
        new("PeriodicTolerance", "RPM / cálculo", "Tolerancia entre ciclos", c => c.Solver.PeriodicTolerance, (c,v) => c with { Solver = c.Solver with { PeriodicTolerance = v } }),
        new("Cycles", "RPM / cálculo", "Máximo de ciclos", c => c.Solver.MaximumCycles, (c,v) => c with { Solver = c.Solver with { MaximumCycles = (int)v } }, Integer:true)
    ];

    private static IEnumerable<EngineParameter> DuctParameters()
    {
        foreach (bool inlet in new[] { true, false })
        {
            string name = inlet ? "Admisión" : "Escape", prefix = inlet ? "Intake" : "Exhaust";
            DuctGeometry Get(GasPath path) => inlet ? path.Intake : path.Exhaust;
            FourStrokeConfiguration Set(FourStrokeConfiguration config, Func<DuctGeometry, DuctGeometry> update)
            {
                var path = config.Ducts;
                if (path is null) return config;
                var geometry = update(Get(path));
                return config with { Ducts = inlet ? path with { Intake = geometry } : path with { Exhaust = geometry } };
            }

            EngineParameter Field(string key, string label, Func<DuctGeometry, double> get,
                Func<DuctGeometry, double, DuctGeometry> set, double scale = 1000, bool integer = false) =>
                new(prefix + key, "Conductos", name + " · " + label,
                    c => get(Get(c.Ducts ?? GasPath.Example)), (c, v) => Set(c, d => set(d, v)), scale, integer);
            yield return Field("DuctLength", "longitud [mm]", d => d.LengthM, (d, v) => d with { LengthM = v });
            yield return Field("ReservoirBore", "diámetro en depósito [mm]", d => d.ReservoirDiameterM, (d, v) => d with { ReservoirDiameterM = v });
            yield return Field("ValveBore", "diámetro en válvula [mm]", d => d.ValveDiameterM, (d, v) => d with { ValveDiameterM = v });
            yield return Field("Cells", "celdas", d => d.Cells, (d, v) => d with { Cells = (int)v }, 1, true);
            yield return Field("Roughness", "rugosidad [µm] · −1 omite pared",
                d => d.Wall?.RoughnessM ?? -.000001, (d, v) => d with
                { Wall = v == -.000001 ? null : new(v, d.Wall?.TemperatureK) }, 1e6);
            yield return Field("WallT", "pared [K] · 0 adiabática",
                d => d.Wall?.TemperatureK ?? 0, (d, v) => d with
                { Wall = v == 0 && d.Wall is null ? null : new(d.Wall?.RoughnessM ?? 0, v == 0 ? null : v) }, 1);
        }
        yield return new("IntakeBoundary", "Conductos", "Boca de admisión · 1 presión física · 0 adaptada histórica",
            c => (int)(c.Ducts ?? GasPath.Example).IntakeBoundary,
            (c, v) => c.Ducts is null ? c : c with { Ducts = c.Ducts with { IntakeExterior = (ExteriorBoundary)(int)v } }, Integer: true);
        yield return new("ExhaustBoundary", "Conductos", "Boca de escape · 1 presión física · 0 adaptada histórica",
            c => (int)(c.Ducts ?? GasPath.Example).ExhaustBoundary,
            (c, v) => c.Ducts is null ? c : c with { Ducts = c.Ducts with { ExhaustExterior = (ExteriorBoundary)(int)v } }, Integer: true);
        yield return new("Courant", "Conductos", "CFL", c => (c.Ducts ?? GasPath.Example).Courant,
            (c, v) => c.Ducts is null ? c : c with { Ducts = c.Ducts with { Courant = v } });
    }
}
