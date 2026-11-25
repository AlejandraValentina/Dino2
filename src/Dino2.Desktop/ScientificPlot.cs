using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Dino2.Desktop;

public sealed record PlotLine(string Name, IReadOnlyList<Point> Points, Brush Color, bool Dashed = false, bool MarkersOnly = false);

public class ScientificPlot : FrameworkElement
{
    private static readonly Pen GridPen = MakePen(new SolidColorBrush(Color.FromRgb(226, 232, 237)), 1);
    private static readonly Pen FramePen = MakePen(new SolidColorBrush(Color.FromRgb(198, 208, 216)), 1);
    private static readonly Pen ZeroPen = MakePen(Brushes.SlateGray, 1);
    private static readonly Pen CursorPen = MakePen(new SolidColorBrush(Color.FromArgb(160, 30, 43, 53)), 1, true);
    private static readonly Brush CaptionBrush = Frozen(Color.FromRgb(30, 43, 53));
    private static readonly Brush TickBrush = Frozen(Color.FromRgb(86, 104, 122));
    private static readonly Brush ReadoutBackground = Frozen(Color.FromArgb(240, 255, 255, 255));
    private readonly Typeface font = new("Segoe UI");
    private IReadOnlyList<PlotLine> lines = [];
    private Rect full = Rect.Empty, view = Rect.Empty;
    private Point? dragStart;
    private Rect dragView;
    private Point? cursor;
    private (PlotLine Line, Point Sample)[] readout = [];
    private Geometry?[] paths = [];
    private Rect pathArea, pathView;
    public string Caption { get; set; } = "";
    public string XLabel { get; set; } = "RPM";
    internal Rect View => view;

    public ScientificPlot()
    {
        MinHeight = 124;
        Focusable = true;
        var reset = new MenuItem { Header = "Ajustar vista" };
        reset.Click += (_, _) => ResetView();
        var save = new MenuItem { Header = "Guardar gráfico PNG…" };
        save.Click += (_, _) => SaveImage();
        ContextMenu = new ContextMenu { Items = { reset, save } };
        ContextMenu.Opened += (_, _) => reset.IsEnabled = save.IsEnabled = !full.IsEmpty;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PlotPeer(this);

    private sealed class PlotPeer(ScientificPlot plot) : FrameworkElementAutomationPeer(plot)
    {
        protected override string GetNameCore() => plot.Caption;
        protected override string GetClassNameCore() => nameof(ScientificPlot);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetHelpTextCore() => plot.Inspection is { Length: > 0 } reading
            ? reading : "Rueda o teclas + y − para zoom, flechas para desplazar e Inicio para ajustar.";
    }

    public void SetLines(IReadOnlyList<PlotLine> values)
    {
        var oldFull = full;
        bool hadZoom = view != oldFull;
        lines = values.Where(line => line.Points.Any(Finite)).ToArray();
        full = Bounds(lines);
        view = hadZoom && oldFull == full ? view : full;
        paths = [];
        ClearInspection();
        InvalidateVisual();
    }

    private static bool Finite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static Rect Bounds(IReadOnlyList<PlotLine> values)
    {
        double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y0 = 0, y1 = 0;
        foreach (var line in values)
            foreach (Point point in line.Points)
            {
                if (!Finite(point)) continue;
                x0 = Math.Min(x0, point.X); x1 = Math.Max(x1, point.X);
                y0 = Math.Min(y0, point.Y); y1 = Math.Max(y1, point.Y);
            }
        if (!double.IsFinite(x0)) return Rect.Empty;
        if (x0 == x1) { x0 -= .5; x1 += .5; }
        double span = y0 == y1 ? 10 : y1 - y0;
        y0 -= y0 < 0 ? span * .07 : 0;
        y1 += span * .1;
        // Extremos verticales en múltiplos del paso de marcas: el eje termina en un valor redondo.
        double step = NiceStep(y1 - y0, 5);
        y1 = Math.Ceiling(y1 / step) * step;
        if (y0 < 0) y0 = Math.Floor(y0 / step) * step;
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Paso de marcas 1–2–5 × 10ⁿ que divide <paramref name="span"/> en unas <paramref name="target"/> partes.</summary>
    internal static double NiceStep(double span, double target)
    {
        if (!(span > 0) || !double.IsFinite(span)) return 1;
        double raw = span / Math.Max(1, target);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double normalized = raw / magnitude;
        return (normalized < 1.5 ? 1 : normalized < 3 ? 2 : normalized < 7 ? 5 : 10) * magnitude;
    }

    /// <summary>Marcas múltiplo de <paramref name="step"/> dentro de [lo, hi].</summary>
    internal static IEnumerable<double> Ticks(double lo, double hi, double step)
    {
        double tolerance = step * 1e-9;
        for (double k = Math.Ceiling((lo - tolerance) / step); k * step <= hi + tolerance; k++)
        {
            double value = k * step;
            yield return Math.Abs(value) < tolerance ? 0 : value;
        }
    }

    /// <summary>Texto de una marca con los decimales que exige el paso.</summary>
    internal static string TickLabel(double value, double step) =>
        value.ToString("F" + Math.Clamp((int)-Math.Floor(Math.Log10(step) + 1e-9), 0, 6), CultureInfo.CurrentCulture);

    private Rect Area => new(66, 26, Math.Max(1, ActualWidth - 91),
        Math.Max(1, ActualHeight - 66 - LegendRows * 16));
    private int LegendColumns => Math.Max(1, (int)(Math.Max(0, ActualWidth - 20) / 150));
    private int LegendRows => Math.Clamp((lines.Count + LegendColumns - 1) / LegendColumns, 1, 3);

    internal void ResetView() => SetView(full);
    internal void Zoom(double factor, Point anchor)
    {
        if (full.IsEmpty) return;
        double width = Math.Clamp(view.Width * factor, full.Width / 100, full.Width);
        double height = Math.Clamp(view.Height * factor, full.Height / 100, full.Height);
        SetView(new Rect(view.X + (view.Width - width) * anchor.X, view.Y + (view.Height - height) * anchor.Y, width, height));
    }

    private void SetView(Rect candidate)
    {
        if (full.IsEmpty) return;
        view = new Rect(Math.Clamp(candidate.X, full.Left, Math.Max(full.Left, full.Right - candidate.Width)),
            Math.Clamp(candidate.Y, full.Top, Math.Max(full.Top, full.Bottom - candidate.Height)), candidate.Width, candidate.Height);
        ClearInspection();
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var pointer = e.GetPosition(this);
        if (full.IsEmpty || !Area.Contains(pointer)) return;
        var area = Area;
        Zoom(Math.Pow(.8, e.Delta / 120.0), new Point((pointer.X - area.Left) / area.Width, (area.Bottom - pointer.Y) / area.Height));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (full.IsEmpty || !Area.Contains(e.GetPosition(this))) return;
        Focus();
        if (e.ClickCount == 2) { ResetView(); e.Handled = true; return; }
        if (view == full || !CaptureMouse()) return;
        dragStart = e.GetPosition(this);
        dragView = view;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (dragStart is null) return;
        dragStart = null;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { dragStart = null; base.OnLostMouseCapture(e); }
    protected override void OnMouseLeave(MouseEventArgs e) { ClearInspection(); base.OnMouseLeave(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (full.IsEmpty) return;
        var moved = view;
        switch (e.Key)
        {
            case Key.Add or Key.OemPlus: Zoom(.8, new(.5, .5)); break;
            case Key.Subtract or Key.OemMinus: Zoom(1.25, new(.5, .5)); break;
            case Key.Home or Key.Escape: ResetView(); break;
            case Key.Left: moved.Offset(-view.Width / 8, 0); SetView(moved); break;
            case Key.Right: moved.Offset(view.Width / 8, 0); SetView(moved); break;
            case Key.Up: moved.Offset(0, view.Height / 8); SetView(moved); break;
            case Key.Down: moved.Offset(0, -view.Height / 8); SetView(moved); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        var area = Area;
        if (dragStart is Point start)
        {
            var moved = dragView;
            moved.Offset((start.X - position.X) / area.Width * dragView.Width, (position.Y - start.Y) / area.Height * dragView.Height);
            SetView(moved);
            return;
        }
        if (full.IsEmpty || !area.Contains(position)) { ClearInspection(); return; }
        var location = new Point(view.X + (position.X - area.Left) / area.Width * view.Width,
            view.Y + (area.Bottom - position.Y) / area.Height * view.Height);
        cursor = position;
        readout = lines.Select(line => (Line: line, Sample: Nearest(line.Points, location, view)))
            .Where(item => item.Sample is not null).Select(item => (item.Line, item.Sample!.Value)).ToArray();
        InvalidateVisual();
    }

    /// <summary>Lectura del cursor: muestras calculadas más próximas, una por curva.</summary>
    internal string Inspection => string.Join("\n", readout.Select(item =>
        FormattableString.CurrentCulture($"{item.Line.Name} · {XLabel} {item.Sample.X:G7} · {Caption} {item.Sample.Y:G7}")));

    internal static Point? Nearest(IReadOnlyList<Point> points, Point location, Rect viewport)
    {
        double minimum = double.PositiveInfinity;
        Point? nearest = null;
        foreach (var point in points)
        {
            if (!Finite(point) || !viewport.Contains(point)) continue;
            double dx = (point.X - location.X) / viewport.Width, dy = (point.Y - location.Y) / viewport.Height;
            // Prioritize the x coordinate. Use y to distinguish overlapping positions in a p–V loop.
            double distance = dx * dx + 1e-6 * dy * dy;
            if (distance < minimum) { minimum = distance; nearest = point; }
        }
        return nearest;
    }

    private void ClearInspection()
    {
        if (cursor is null && readout.Length == 0) return;
        cursor = null; readout = [];
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        Text(dc, Caption, 10, 3, 13, Math.Max(1, ActualWidth - 100), CaptionBrush, FontWeights.SemiBold);
        if (full.IsEmpty) { Text(dc, "Sin resultados", 70, 37, 12, 300, TickBrush); return; }
        if (view != full) Text(dc, "Vista ampliada · Inicio para ajustar", Math.Max(10, ActualWidth - 230), 5, 11, 220, TickBrush,
            alignment: TextAlignment.Right);
        Rect area = Area;
        Point Map(Point p) => new(area.Left + (p.X - view.X) / view.Width * area.Width,
            area.Bottom - (p.Y - view.Y) / view.Height * area.Height);
        // Marcas redondas: aproximadamente una cada 32 px en vertical (al menos tres) y cada 90 px en horizontal.
        double yStep = NiceStep(view.Height, Math.Clamp(area.Height / 32, 3, 8));
        double xStep = NiceStep(view.Width, Math.Clamp(area.Width / 90, 2, 10));
        foreach (double y in Ticks(view.Top, view.Bottom, yStep))
        {
            double py = Map(new Point(view.Left, y)).Y;
            dc.DrawLine(GridPen, new Point(area.Left, py), new Point(area.Right, py));
            Text(dc, TickLabel(y, yStep), 2, py - 7, 11, area.Left - 8, TickBrush, alignment: TextAlignment.Right);
        }
        foreach (double x in Ticks(view.Left, view.Right, xStep))
        {
            double px = Map(new Point(x, view.Top)).X;
            dc.DrawLine(GridPen, new Point(px, area.Top), new Point(px, area.Bottom));
            Text(dc, TickLabel(x, xStep), px - 40, area.Bottom + 3, 11, 80, TickBrush, alignment: TextAlignment.Center);
        }
        dc.DrawRectangle(null, FramePen, area);
        if (view.Top < 0 && view.Bottom > 0)
        {
            double zero = Map(new Point(view.Left, 0)).Y;
            dc.DrawLine(ZeroPen, new Point(area.Left, zero), new Point(area.Right, zero));
        }
        Text(dc, XLabel, area.Left, area.Bottom + 17, 11, area.Width, CaptionBrush, alignment: TextAlignment.Center);
        if (paths.Length != lines.Count || pathArea != area || pathView != view)
        {
            paths = lines.Select(line => BuildPath(line, Map)).ToArray();
            pathArea = area; pathView = view;
        }
        dc.PushClip(new RectangleGeometry(area));
        for (int i = 0; i < lines.Count; i++)
            if (paths[i] is { } geometry)
            {
                var line = lines[i];
                if (!line.MarkersOnly) dc.DrawGeometry(null, MakePen(line.Color, 2, line.Dashed), geometry);
                if (line.MarkersOnly || line.Points.Count <= 100)
                    foreach (var point in line.Points.Where(p => Finite(p) && view.Contains(p)))
                        dc.DrawEllipse(line.Color, null, Map(point), 2.6, 2.6);
            }
        if (cursor is Point mouse && readout.Length > 0)
        {
            dc.DrawLine(CursorPen, new Point(mouse.X, area.Top), new Point(mouse.X, area.Bottom));
            foreach (var (line, sample) in readout)
                dc.DrawEllipse(Brushes.White, MakePen(line.Color, 2), Map(sample), 4, 4);
        }
        dc.Pop();
        if (cursor is Point at && readout.Length > 0) DrawReadout(dc, at, area);
        DrawLegend(dc, area.Bottom + 32);
        if (IsKeyboardFocused)
            dc.DrawRectangle(null, MakePen(Brushes.SteelBlue, 1), new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)));
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    private void DrawReadout(DrawingContext dc, Point at, Rect area)
    {
        const double row = 16, pad = 7, swatch = 14;
        var header = Format($"{XLabel} = {readout[0].Sample.X.ToString("G6", CultureInfo.CurrentCulture)}", 11, CaptionBrush, FontWeights.SemiBold);
        var rows = readout.Select(item => Format($"{item.Line.Name}: {item.Sample.Y.ToString("G5", CultureInfo.CurrentCulture)}",
            11, CaptionBrush, FontWeights.Normal)).ToArray();
        double width = Math.Max(header.Width, rows.Max(r => r.Width) + swatch) + 2 * pad;
        double height = row * (rows.Length + 1) + 2 * pad - 2;
        double x = at.X + 14 + width > area.Right ? at.X - 14 - width : at.X + 14;
        double y = Math.Clamp(at.Y - height / 2, area.Top + 2, Math.Max(area.Top + 2, area.Bottom - height - 2));
        dc.DrawRoundedRectangle(ReadoutBackground, FramePen, new Rect(x, y, width, height), 3, 3);
        dc.DrawText(header, new Point(x + pad, y + pad));
        for (int i = 0; i < rows.Length; i++)
        {
            double top = y + pad + row * (i + 1);
            var line = readout[i].Line;
            dc.DrawLine(MakePen(line.Color, 2, line.Dashed), new Point(x + pad, top + 7), new Point(x + pad + 10, top + 7));
            dc.DrawText(rows[i], new Point(x + pad + swatch, top));
        }
    }

    private void DrawLegend(DrawingContext dc, double top)
    {
        int columns = LegendColumns;
        double width = (ActualWidth - 20) / columns;
        int visible = Math.Min(lines.Count, LegendRows * columns);
        for (int i = 0; i < visible; i++)
        {
            var line = lines[i];
            double x = 10 + i % columns * width, y = top + i / columns * 16;
            if (line.MarkersOnly) dc.DrawEllipse(line.Color, null, new Point(x + 8, y + 7), 2.6, 2.6);
            else dc.DrawLine(MakePen(line.Color, 2, line.Dashed), new Point(x, y + 7), new Point(x + 19, y + 7));
            Text(dc, line.Name, x + 24, y, 11, Math.Max(1, width - 25), CaptionBrush);
        }
    }

    private static Geometry BuildPath(PlotLine line, Func<Point, Point> map)
    {
        var result = new StreamGeometry();
        using (var context = result.Open())
        {
            bool continueLine = false;
            foreach (var point in line.Points)
            {
                if (!Finite(point)) { continueLine = false; continue; }
                if (continueLine) context.LineTo(map(point), true, false);
                else context.BeginFigure(map(point), false, false);
                continueLine = true;
            }
        }
        result.Freeze();
        return result;
    }

    private static Pen MakePen(Brush color, double width, bool dashed = false)
    {
        var pen = new Pen(color, width);
        if (dashed) pen.DashStyle = DashStyles.Dash;
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    private void Text(DrawingContext dc, string text, double x, double y, double size = 12,
        double width = 300, Brush? brush = null, FontWeight? weight = null, TextAlignment alignment = TextAlignment.Left)
    {
        var label = Format(text, size, brush ?? CaptionBrush, weight ?? FontWeights.Normal);
        label.MaxTextWidth = Math.Max(1, width);
        label.MaxLineCount = 1;
        label.Trimming = TextTrimming.CharacterEllipsis;
        label.TextAlignment = alignment;
        dc.DrawText(label, new Point(x, y));
    }

    private FormattedText Format(string text, double size, Brush brush, FontWeight weight) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(font.FontFamily, FontStyles.Normal, weight, FontStretches.Normal), size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void SaveImage()
    {
        if (full.IsEmpty || ActualWidth <= 0 || ActualHeight <= 0) return;
        var owner = Window.GetWindow(this);
        var dialog = new SaveFileDialog { Filter = "Imagen PNG (*.png)|*.png", FileName = "grafico-dino2.png" };
        if (dialog.ShowDialog(owner) != true) return;
        try { ExportPng(dialog.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show(owner, ex.Message, "No se pudo guardar el gráfico", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    internal void ExportPng(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var image = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        image.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
