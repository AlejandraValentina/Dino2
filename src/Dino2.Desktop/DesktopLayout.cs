using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Dino2.Desktop;

internal static class DesktopLayout
{
    internal static void Fit(Window window)
    {
        var work = SystemParameters.WorkArea;
        window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, work.Width - 20));
        window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, work.Height - 20));
    }

    internal static bool ConfirmDiscard(Window owner, string document) =>
        MessageBox.Show(!owner.IsVisible && owner.Owner is not null ? owner.Owner : owner, $"Hay resultados sin guardar en {document}. ¿Descartarlos?",
            "Dino2", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    internal static void FormatGrid(DataGrid grid)
    {
        grid.FrozenColumnCount = 1;
        grid.AutoGeneratingColumn += (_, e) =>
        {
            string[] words = e.PropertyName.Split('_');
            string unit = words[^1] switch { "W" => "W", "kW" => "kW", "Nm" => "N·m", "K" => "K", "mg" => "mg", "bar" => "bar", "J" => "J", _ => "" };
            e.Column.Header = e.PropertyName == "BSFC_g_kWh" ? "Consumo específico [g/kWh]" : unit.Length == 0
                ? string.Join(" ", words) : $"{string.Join(" ", words[..^1])} [{unit}]";
            if ((Nullable.GetUnderlyingType(e.PropertyType) ?? e.PropertyType) == typeof(double) &&
                e.Column is DataGridTextColumn column)
                column.Binding = new Binding(e.PropertyName) { StringFormat = "G6", TargetNullValue = "—" };
        };
    }
}
