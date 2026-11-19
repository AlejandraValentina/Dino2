using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Dino2.Core;

namespace Dino2.Desktop;

// Two small independent linear plots, zero baseline and actual RPM coordinates.
public sealed class CurvePlot : FrameworkElement
{
    private IReadOnlyList<OperatingPoint>? points;
    private int? selected;
    public bool Power { get; set; }
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(35, 57, 72));
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(23, 107, 140));

    public CurvePlot()
    {
        MinHeight = 130;
        MouseMove += Inspect;
        MouseLeave += (_, _) => { selected = null; ToolTip = null; InvalidateVisual(); };
    }

    public void SetPoints(IReadOnlyList<OperatingPoint>? data)
    {
        points = data;
        selected = null;
        ToolTip = null;
        InvalidateVisual();
    }

    private double Value(OperatingPoint point) => Power ? point.IndicatedPowerW / 1000 : point.IndicatedTorqueNm;

    private void Inspect(object sender, MouseEventArgs e)
    {
        if (points is null || points.Count == 0) return;
        double fraction = Math.Clamp((e.GetPosition(this).X - 68) / Math.Max(1, ActualWidth - 96), 0, 1);
        selected = (int)Math.Round(fraction * (points.Count - 1));
        var p = points[selected.Value];
        ToolTip = FormattableString.Invariant($"{p.Rpm} RPM · {Value(p):F4} {(Power ? "kW" : "N·m")} indicados ideales");
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        double left = 68, top = 36, width = Math.Max(1, ActualWidth - 96), height = Math.Max(1, ActualHeight - 80);
        Text(dc, Power ? "Potencia indicada ideal [kW]" : "Par indicado ideal [N·m]", 12, 4, 14);
        if (points is null || points.Count == 0)
        {
            Text(dc, "Sin cálculo", left, top + 20);
            return;
        }
        double maximum = Math.Max(1e-9, points.Max(Value) * 1.12);
        if (points.All(p => Value(p) == 0)) maximum = 1;
        int minRpm = points[0].Rpm, maxRpm = points[^1].Rpm;
        double X(OperatingPoint p) => maxRpm == minRpm ? left + width / 2 : left + width * (p.Rpm - minRpm) / (maxRpm - minRpm);
        double Y(OperatingPoint p) => top + height * (1 - Value(p) / maximum);
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(222, 230, 237)), 1);
        for (int i = 0; i <= 4; i++)
        {
            double y = top + height * (1 - i / 4.0);
            dc.DrawLine(grid, new Point(left, y), new Point(left + width, y));
            Text(dc, (maximum * i / 4).ToString("G4", CultureInfo.InvariantCulture), 3, y - 8);
        }
        dc.DrawLine(new Pen(Ink, 1), new Point(left, top), new Point(left, top + height));
        dc.DrawLine(new Pen(Ink, 1), new Point(left, top + height), new Point(left + width, top + height));
        if (maxRpm == minRpm) Text(dc, minRpm.ToString(), left + width / 2 - 14, top + height + 6);
        else
        {
            for (int i = 0; i <= 4; i++)
            {
                double rpm = minRpm + (maxRpm - minRpm) * i / 4.0;
                Text(dc, rpm.ToString("0.#", CultureInfo.InvariantCulture), left + width * i / 4 - 14, top + height + 6);
            }
        }
        Text(dc, "RPM", left + width / 2 - 12, top + height + 23);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(X(points[0]), Y(points[0])), false, false);
            foreach (var p in points.Skip(1)) context.LineTo(new Point(X(p), Y(p)), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(Line, 2), geometry);
        if (points.Count <= 60)
            foreach (var p in points) dc.DrawEllipse(Line, null, new Point(X(p), Y(p)), 3, 3);
        if (selected is int index)
        {
            var p = points[index];
            dc.DrawLine(new Pen(Ink, 1), new Point(X(p), top), new Point(X(p), top + height));
            dc.DrawEllipse(Brushes.White, new Pen(Line, 2), new Point(X(p), Y(p)), 5, 5);
        }
    }

    private void Text(DrawingContext dc, string value, double x, double y, double size = 12) =>
        dc.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
}
