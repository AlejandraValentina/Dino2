using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.Controls;

namespace Dino2.Desktop;

internal static class DesktopLayout
{
    internal static void Fit(Window window)
    {
        var work = SystemParameters.WorkArea;
        window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, work.Width - 20));
        window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, work.Height - 20));
        if (window is MetroWindow metro) ApplyChrome(metro);
    }

    private static void ApplyChrome(MetroWindow window)
    {
        Brush Resource(string key, Color fallback) => window.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
        window.TitleCharacterCasing = CharacterCasing.Normal;
        window.TitleBarHeight = 32;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.WindowTitleBrush = Resource("Dino2.Brushes.TitleBar", Color.FromRgb(0x1C, 0x2A, 0x35));
        window.NonActiveWindowTitleBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x48, 0x53));
        window.GlowBrush = Resource("Dino2.Brushes.Accent", Color.FromRgb(0x17, 0x6B, 0x8C));
        window.NonActiveGlowBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xA6));
    }

    /// <summary>Atajo de teclado que equivale a pulsar el botón, solo cuando el botón está habilitado.</summary>
    internal static void Shortcut(Window window, Key key, ModifierKeys modifiers, Button target)
    {
        var command = new RoutedCommand();
        window.CommandBindings.Add(new CommandBinding(command,
            (_, _) => target.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, target)),
            (_, e) => e.CanExecute = target.IsEnabled));
        window.InputBindings.Add(new KeyBinding(command, key, modifiers));
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
