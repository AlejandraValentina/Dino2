using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dino2.Core.Studies;

namespace Dino2.Desktop;

public partial class StudyPanel
{
    private void DrawSensitivity(IReadOnlyList<RankEntry> ranking)
    {
        var plan = resultPlan!;
        var axes = plan.Definition.Axes;
        var objective = plan.Definition.Objective;
        var evaluated = ranking.ToDictionary(r => r.Id, r => r.Objective.Score!.Value);
        SensitivityPlot.Visibility = Visibility.Collapsed;
        MapScroll.Visibility = Visibility.Collapsed;
        if (evaluated.Count == 0)
        {
            SensitivityNote.Text = "No hay muestras elegibles para visualizar.";
            return;
        }
        var variants = plan.Variants.Skip(1).ToArray();
        if (axes.Count == 1)
        {
            var spec = StudyParameters.Get(axes[0].Parameter);
            SensitivityPlot.XLabel = spec.Display;
            SensitivityPlot.Caption = $"Objetivo muestreado [{objective.Unit}]";
            SensitivityPlot.SetLines([new("Muestras convergidas", variants.Where(v => evaluated.ContainsKey(v.Id))
                .Select(v => new Point(v.Parameters[0].Value, evaluated[v.Id])).ToArray(), Brushes.SteelBlue, MarkersOnly: true)]);
            SensitivityPlot.Visibility = Visibility.Visible;
            SensitivityNote.Text = "Cada punto es una variante evaluada. Incluye solo configuraciones elegibles para el objetivo.";
            return;
        }
        if (axes.Count != 2)
        {
            SensitivityNote.Text = "Para tres parámetros, usá la tabla ordenable y la curva de cada variante.";
            return;
        }

        MapGrid.Children.Clear();
        MapGrid.RowDefinitions.Clear();
        MapGrid.ColumnDefinitions.Clear();
        var x = variants.Select(v => v.Parameters[0].Value).Distinct().ToArray();
        var y = variants.Select(v => v.Parameters[1].Value).Distinct().ToArray();
        double lowest = evaluated.Values.Min(objective.Preference);
        double highest = evaluated.Values.Max(objective.Preference);
        for (int col = 0; col <= x.Length; col++) MapGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        for (int row = 0; row <= y.Length; row++) MapGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        void Add(UIElement content, int row, int column)
        {
            Grid.SetRow(content, row);
            Grid.SetColumn(content, column);
            MapGrid.Children.Add(content);
        }
        for (int col = 0; col < x.Length; col++)
            Add(new TextBlock { Text = Format(x[col]), Margin = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Center }, 0, col + 1);
        for (int row = 0; row < y.Length; row++)
            Add(new TextBlock { Text = Format(y[row]), Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center }, row + 1, 0);
        foreach (var variant in variants)
        {
            bool available = evaluated.TryGetValue(variant.Id, out double value);
            var weight = available && highest > lowest ? (objective.Preference(value) - lowest) / (highest - lowest) : 0;
            var cell = new Button
            {
                Content = available ? value.ToString("G5", System.Globalization.CultureInfo.InvariantCulture) : "–",
                ToolTip = Label(variant.Id) + " · " + Parameters(variant),
                MinWidth = 72, Padding = new Thickness(5), Margin = new Thickness(1),
                Background = available ? new SolidColorBrush(Color.FromRgb(
                    (byte)(236 - 112 * weight), (byte)(245 - 57 * weight), (byte)(250 - 22 * weight))) : Brushes.Gainsboro
            };
            cell.Click += (_, _) =>
            {
                StudyRows.SelectedItem = StudyRows.Items.OfType<Row>().Single(r => r.Id == variant.Id);
                Pages.SelectedIndex = 1;
            };
            Add(cell, 1 + Array.IndexOf(y, variant.Parameters[1].Value), 1 + Array.IndexOf(x, variant.Parameters[0].Value));
        }
        var horizontal = StudyParameters.Get(axes[0].Parameter).Display;
        var vertical = StudyParameters.Get(axes[1].Parameter).Display;
        SensitivityNote.Text = $"Columnas · {horizontal}. Filas · {vertical}. Celdas [{objective.Unit}], azul más intenso = objetivo más favorable. Pulsá una celda para ver la variante.";
        MapScroll.Visibility = Visibility.Visible;
    }
}
