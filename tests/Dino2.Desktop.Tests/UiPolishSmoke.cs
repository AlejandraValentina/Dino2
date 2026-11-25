using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dino2.Desktop.Tests;

/// <summary>Escalas de gráficos, validación de campos en vivo y selector de modelo dividido.</summary>
internal static class UiPolishSmoke
{
    internal static void Run()
    {
        static void Check(bool pass, string label)
        {
            if (!pass) throw new Exception(label);
            Console.WriteLine("PASS ui " + label);
        }

        Check(ScientificPlot.NiceStep(19.228, 5) == 5 && ScientificPlot.NiceStep(4000, 5) == 1000 &&
              Math.Abs(ScientificPlot.NiceStep(0.0042, 4) - 0.001) < 1e-15 && ScientificPlot.NiceStep(720, 8) == 100,
            "los pasos de marca son 1, 2 o 5 por potencia de diez");
        Check(ScientificPlot.Ticks(-0.3, 1.05, 0.5).SequenceEqual([0, 0.5, 1]) &&
              ScientificPlot.Ticks(2000, 6000, 1000).SequenceEqual([2000, 3000, 4000, 5000, 6000]),
            "las marcas caen en múltiplos del paso dentro de la vista");
        Check(ScientificPlot.TickLabel(2.5, 0.5) == 2.5.ToString("F1", CultureInfo.CurrentCulture) &&
              ScientificPlot.TickLabel(3000, 1000) == "3000",
            "las etiquetas usan los decimales del paso y no agregan separadores de miles");

        var plot = new ScientificPlot { Caption = "Potencia [kW]" };
        plot.SetLines([new("A", [new(2000, 7.1), new(4000, 19.228)], Brushes.SteelBlue)]);
        double top = plot.View.Bottom, step = ScientificPlot.NiceStep(top, 5);
        Check(plot.View.Y == 0 && Math.Abs(top / step - Math.Round(top / step)) < 1e-9 && top >= 19.228,
            "el eje vertical empieza en cero y termina en un valor redondo");

        var window = new FourStrokeWindow(() => true) { Width = 1100, Height = 720 };
        try
        {
            window.Show();
            TextBox Field(string key) => (TextBox)window.FindName("Field_" + key);
            var bore = Field("Bore");
            bore.Text = "8o";
            Check(FieldValidation.GetIsInvalid(bore) && FieldValidation.GetMessage(bore) == FieldValidation.NotANumber,
                "un número mal escrito se marca al escribirlo");
            bore.Text = "80,5";
            Check(!FieldValidation.GetIsInvalid(bore), "la coma decimal se acepta y limpia la marca");
            Field("RpmStart").Text = "1000.5";
            Check(FieldValidation.GetMessage(Field("RpmStart")) == FieldValidation.NotAnInteger, "las RPM exigen enteros");
            Field("RpmStart").Text = "2000";
            var duct = Field("IntakeDuctLength");
            window.PhysicsModelIndex = 0;
            duct.Text = "x";
            Check(!duct.IsEnabled && !FieldValidation.GetIsInvalid(duct), "un campo deshabilitado no se marca");
            duct.Text = "450";
            Check(!FieldValidation.Invalid(window).Any(), "sin campos inválidos tras corregir");

            var burn = (ComboBox)window.FindName("BurnLawChoice");
            for (int model = 0; model <= 8; model++)
            {
                window.PhysicsModelIndex = model;
                Check(window.PhysicsModelIndex == model && burn.IsEnabled == (model != 0),
                    $"el selector dividido representa el modelo {model}");
            }
        }
        finally { window.Close(); }
    }
}
