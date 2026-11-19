using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Dino2.Desktop;

public sealed record PlotLine(string Name, IReadOnlyList<Point> Points, Brush Color, bool Dashed = false);

public sealed class ScientificPlot : FrameworkElement
{
    private IReadOnlyList<PlotLine> lines = [];
    public string Caption { get; set; } = "";
    public string XLabel { get; set; } = "RPM";
    private double xMin, xMax, yMin, yMax;
    public ScientificPlot()
    {
        MinHeight = 160;
        MouseMove += Inspect;
        MouseLeave += (_, _) => ToolTip = null;
    }
    public void SetLines(IReadOnlyList<PlotLine> values) { lines = values; ToolTip = null; InvalidateVisual(); }
    private void Inspect(object sender, MouseEventArgs e)
    {
        if (lines.Count == 0) return;
        double x = xMin + Math.Clamp((e.GetPosition(this).X - 76) / Math.Max(1, ActualWidth - 106), 0, 1) * (xMax - xMin);
        ToolTip = string.Join("\n", lines.Where(l => l.Points.Count > 0).Select(l =>
        {
            var nearest = l.Points.MinBy(p => Math.Abs(p.X - x));
            return FormattableString.Invariant($"{l.Name}: x={nearest.X:G6}, y={nearest.Y:G7}");
        }));
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        Text(dc, Caption, 10, 3, 14);
        var points = lines.SelectMany(l => l.Points).Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToArray();
        if (points.Length == 0) { Text(dc, "Sin resultados", 76, 45); return; }
        xMin = points.Min(p => p.X); xMax = points.Max(p => p.X);
        yMin = Math.Min(0, points.Min(p => p.Y)); yMax = Math.Max(0, points.Max(p => p.Y));
        if (xMin == xMax) { xMin -= .5; xMax += .5; }
        if (yMin == yMax) { yMin -= .5; yMax += .5; }
        double range = yMax - yMin;
        if (yMin < 0) yMin -= range * .07;
        yMax += range * .1;
        double left = 76, top = 32, width = Math.Max(1, ActualWidth - 106), height = Math.Max(1, ActualHeight - 76);
        Point Map(Point p) => new(left + (p.X - xMin) / (xMax - xMin) * width, top + height - (p.Y - yMin) / (yMax - yMin) * height);
        var grid = new Pen(Brushes.LightGray, 1);
        for (int i = 0; i <= 4; i++)
        {
            double y = yMin + (yMax - yMin) * i / 4, x = xMin + (xMax - xMin) * i / 4;
            double gy = Map(new(xMin, y)).Y;
            dc.DrawLine(grid, new(left, gy), new(left + width, gy));
            Text(dc, y.ToString("G4", CultureInfo.InvariantCulture), 2, gy - 8);
            Text(dc, x.ToString("G5", CultureInfo.InvariantCulture), Map(new(x, yMin)).X - 16, top + height + 4);
        }
        Text(dc, XLabel, left + width / 2 - 20, top + height + 22);
        dc.PushClip(new RectangleGeometry(new Rect(left - 4, top - 4, width + 8, height + 8)));
        foreach (var line in lines)
        {
            if (line.Points.Count == 0) continue;
            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                context.BeginFigure(Map(line.Points[0]), false, false);
                foreach (var p in line.Points.Skip(1)) context.LineTo(Map(p), true, false);
            }
            path.Freeze();
            var pen = new Pen(line.Color, 2) { DashStyle = line.Dashed ? DashStyles.Dash : DashStyles.Solid };
            dc.DrawGeometry(null, pen, path);
            if (line.Points.Count <= 100)
                foreach (var p in line.Points) dc.DrawEllipse(line.Color, null, Map(p), 2.5, 2.5);
        }
        dc.Pop();
    }
    private void Text(DrawingContext dc, string text, double x, double y, double size = 12) =>
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, Brushes.DarkSlateGray, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
}
