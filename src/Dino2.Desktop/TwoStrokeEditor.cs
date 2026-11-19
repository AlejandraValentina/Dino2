using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Dino2.Core.GasDynamics;
using Dino2.Core.TwoStroke;

namespace Dino2.Desktop;

public partial class TwoStrokeWindow
{
    private sealed record Field(string Label, Func<TwoStrokeConfiguration, double> Read,
        Func<TwoStrokeConfiguration, double, TwoStrokeConfiguration> Set);

    private void BuildEditor()
    {
        var tabs = new Dictionary<string, StackPanel>();
        StackPanel Group(string name)
        {
            if (tabs.TryGetValue(name, out var panel)) return panel;
            panel = new StackPanel { Margin = new Thickness(10, 1, 10, 10) };
            tabs.Add(name, panel);
            return panel;
        }

        void Add(string name, string label, Func<TwoStrokeConfiguration, double> read,
            Func<TwoStrokeConfiguration, double, TwoStrokeConfiguration> set)
        {
            var field = new Field(label, read, set);
            var input = new TextBox();
            AutomationProperties.SetName(input, label);
            var panel = Group(name);
            panel.Children.Add(new Label { Content = label, Target = input, Padding = new Thickness(0, 6, 0, 3) });
            panel.Children.Add(input);
            inputs.Add(field, input);
            input.TextChanged += (_, _) => Edited();
        }

        const string engine = "Motor y regímenes", combustion = "Combustión y pérdidas", reed = "Cárter, láminas y admisión",
            ports = "Lumbreras y transfers", exhaust = "Escape de expansión", numerics = "Resolución numérica";
        static int Integer(double value) => Math.Truncate(value) == value && value >= 0 && value <= int.MaxValue
            ? (int)value : throw new ArgumentException("Usá un número entero positivo.");
        Add(engine, "Diámetro cilindro [mm]", c => c.Geometry.BoreM * 1000, (c,v) => c with { Geometry = c.Geometry with { BoreM = v / 1000 } });
        Add(engine, "Carrera [mm]", c => c.Geometry.StrokeM * 1000, (c,v) => c with { Geometry = c.Geometry with { StrokeM = v / 1000 } });
        Add(engine, "Biela [mm]", c => c.Geometry.RodLengthM * 1000, (c,v) => c with { Geometry = c.Geometry with { RodLengthM = v / 1000 } });
        Add(engine, "RPM inicio", c => c.Speeds.Start, (c,v) => c with { Speeds = c.Speeds with { Start = Integer(v) } });
        Add(engine, "RPM fin", c => c.Speeds.End, (c,v) => c with { Speeds = c.Speeds with { End = Integer(v) } });
        Add(engine, "RPM paso", c => c.Speeds.Step, (c,v) => c with { Speeds = c.Speeds with { Step = Integer(v) } });

        Group(combustion);
        Group(reed);
        Group(ports);
        Group(exhaust);
        foreach (var spec in TwoStrokeParameters.All)
        {
            string group = spec.Id switch
            {
                TwoStrokeParameter.Compression => engine,
                TwoStrokeParameter.SparkAdvance => combustion,
                >= TwoStrokeParameter.TransferRoof and <= TwoStrokeParameter.ExhaustThroat => ports,
                >= TwoStrokeParameter.HeaderLength => exhaust,
                _ => reed
            };
            Add(group, spec.ToString(), spec.Read, spec.Set);
        }

        Add(reed, "Presión admisión [kPa abs]", c => c.Intake.PressurePa / 1000, (c,v) => c with { Intake = c.Intake with { PressurePa = v * 1000 } });
        Add(reed, "Temperatura admisión [K]", c => c.Intake.TemperatureK, (c,v) => c with { Intake = c.Intake with { TemperatureK = v } });
        Add(reed, "Diámetro admisión lado depósito [mm]", c => c.IntakeDuct.ReservoirDiameterM * 1000, (c,v) => c with { IntakeDuct = c.IntakeDuct with { ReservoirDiameterM = v / 1000 } });
        Add(reed, "Garganta de láminas [mm²]", c => c.Reed.ThroatM2 * 1e6, (c,v) => c with { Reed = c.Reed with { ThroatM2 = v / 1e6 } });
        Add(reed, "Perímetro de láminas [mm]", c => c.Reed.PerimeterM * 1000, (c,v) => c with { Reed = c.Reed with { PerimeterM = v / 1000 } });
        Add(reed, "Cd de láminas", c => c.Reed.Cd, (c,v) => c with { Reed = c.Reed with { Cd = v } });
        Add(ports, "Cd de transfers", c => c.Transfer.Cd, (c,v) => c with { Transfer = c.Transfer with { Cd = v } });
        Add(ports, "Cd de escape", c => c.Exhaust.Cd, (c,v) => c with { Exhaust = c.Exhaust with { Cd = v } });
        Add(ports, "Extracción relativa de fresca [1 = mezcla]", c => c.FreshExtractionRatio, (c,v) => c with { FreshExtractionRatio = v });
        Add(exhaust, "Diámetro del colector [mm]", c => c.Expansion.InletDiameterM * 1000, (c,v) => c with { Expansion = c.Expansion with { InletDiameterM = v / 1000 } });
        Add(exhaust, "Presión salida [kPa abs]", c => c.Tail.PressurePa / 1000, (c,v) => c with { Tail = c.Tail with { PressurePa = v * 1000 } });
        Add(exhaust, "Temperatura salida [K]", c => c.Tail.TemperatureK, (c,v) => c with { Tail = c.Tail with { TemperatureK = v } });
        Add(combustion, "Relación aire/combustible [kg/kg]", c => c.Combustion.AirFuelRatio, (c,v) => c with { Combustion = c.Combustion with { AirFuelRatio = v } });
        Add(combustion, "Demora de encendido [ms]", c => c.Combustion.DelayS * 1000, (c,v) => c with { Combustion = c.Combustion with { DelayS = v / 1000 } });
        Add(combustion, "Duración Wiebe [ms]", c => c.Combustion.DurationS * 1000, (c,v) => c with { Combustion = c.Combustion with { DurationS = v / 1000 } });
        Add(combustion, "Fracción de mezcla reactiva quemada", c => c.Combustion.BurnFraction, (c,v) => c with { Combustion = c.Combustion with { BurnFraction = v } });
        Add(combustion, "Exponente Wiebe", c => c.Combustion.WiebeExponent, (c,v) => c with { Combustion = c.Combustion with { WiebeExponent = v } });
        equilibrium = new CheckBox { Content = "Equilibrio UV en tramo cerrado", Margin = new Thickness(0, 8, 0, 8) };
        equilibrium.Checked += (_, _) => Edited();
        equilibrium.Unchecked += (_, _) => Edited();
        Group(combustion).Children.Add(equilibrium);
        Add(combustion, "Temperatura de pared [K]", c => c.Thermal.WallTemperatureK, (c,v) => c with { Thermal = c.Thermal with { WallTemperatureK = v } });
        Add(combustion, "Multiplicador Hohenberg", c => c.Thermal.HohenbergMultiplier, (c,v) => c with { Thermal = c.Thermal with { HohenbergMultiplier = v } });
        Add(combustion, "FMEP constante [kPa]", c => c.Friction.ConstantPa / 1000, (c,v) => c with { Friction = c.Friction with { ConstantPa = v * 1000 } });
        Add(combustion, "FMEP factor presión máxima", c => c.Friction.PeakPressureFactor, (c,v) => c with { Friction = c.Friction with { PeakPressureFactor = v } });
        Add(combustion, "FMEP lineal [Pa/(m/s)]", c => c.Friction.SpeedPaPerMPerS, (c,v) => c with { Friction = c.Friction with { SpeedPaPerMPerS = v } });
        Add(combustion, "FMEP cuadrático [Pa/(m/s)²]", c => c.Friction.SpeedSquaredPaPerM2PerS2, (c,v) => c with { Friction = c.Friction with { SpeedSquaredPaPerM2PerS2 = v } });
        Add(numerics, "Celdas admisión", c => c.IntakeDuct.Cells, (c,v) => c with { IntakeDuct = c.IntakeDuct with { Cells = Integer(v) } });
        Add(numerics, "Celdas escape", c => c.Expansion.Cells, (c,v) => c with { Expansion = c.Expansion with { Cells = Integer(v) } });
        Add(numerics, "Paso máximo [°]", c => c.Numerics.MaximumStepDeg, (c,v) => c with { Numerics = c.Numerics with { MaximumStepDeg = v } });
        Add(numerics, "CFL", c => c.Numerics.Courant, (c,v) => c with { Numerics = c.Numerics with { Courant = v } });
        Add(numerics, "Tolerancia periódica", c => c.Numerics.PeriodicTolerance, (c,v) => c with { Numerics = c.Numerics with { PeriodicTolerance = v } });
        Add(numerics, "Ciclos máximos", c => c.Numerics.MaximumCycles, (c,v) => c with { Numerics = c.Numerics with { MaximumCycles = Integer(v) } });
        scheme = new ComboBox { ItemsSource = Enum.GetValues<DuctScheme>() };
        AutomationProperties.SetName(scheme, "Esquema de conductos");
        Group(numerics).Children.Add(new Label { Content = "Esquema de conductos", Target = scheme });
        Group(numerics).Children.Add(scheme);
        scheme.SelectionChanged += (_, _) => Edited();

        var views = tabs.ToDictionary(item => item.Key, item => new ScrollViewer
        {
            Content = item.Value, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        ParametersGroup.ItemsSource = views.Keys;
        ParametersGroup.SelectionChanged += (_, _) =>
        {
            if (ParametersGroup.SelectedItem is string name)
                ParametersContent.Content = views[name];
        };
        ParametersGroup.SelectedIndex = 0;
    }
}
