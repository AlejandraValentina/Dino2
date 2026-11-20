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
    private static readonly Pen GridPen = MakePen(new SolidColorBrush(Color.FromRgb(221, 230, 235)), 1);
    private static readonly Pen ZeroPen = MakePen(Brushes.SlateGray, 1);
    private readonly Typeface font = new("Segoe UI");
    private IReadOnlyList<PlotLine> lines = [];
    private Rect full = Rect.Empty, view = Rect.Empty;
    private Point? dragStart;
    private Rect dragView;
    private string? inspection;
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
        protected override string GetHelpTextCore() => "Rueda o teclas + y − para zoom, flechas para desplazar e Inicio para ajustar.";
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
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

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
        var cursor = e.GetPosition(this);
        var area = Area;
        if (dragStart is Point start)
        {
            var moved = dragView;
            moved.Offset((start.X - cursor.X) / area.Width * dragView.Width, (cursor.Y - start.Y) / area.Height * dragView.Height);
            SetView(moved);
            return;
        }
        if (full.IsEmpty || !area.Contains(cursor)) { ClearInspection(); return; }
        var location = new Point(view.X + (cursor.X - area.Left) / area.Width * view.Width,
            view.Y + (area.Bottom - cursor.Y) / area.Height * view.Height);
        string message = string.Join("\n", lines.Select(line =>
        {
            var point = Nearest(line.Points, location, view);
            return point is Point p
                ? FormattableString.CurrentCulture($"{line.Name} · {XLabel} {p.X:G7} · {Caption} {p.Y:G7}") : null;
        }).OfType<string>());
        if (inspection != message) { inspection = message; ToolTip = message.Length > 0 ? message : null; }
    }

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

    private void ClearInspection() { inspection = null; ToolTip = null; }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        Text(dc, Caption, 10, 3, 14, Math.Max(1, ActualWidth - 100));
        if (full.IsEmpty) { Text(dc, "Sin resultados", 70, 37); return; }
        if (view != full) Text(dc, "Vista ampliada", Math.Max(10, ActualWidth - 94), 4, 11);
        Rect area = Area;
        Point Map(Point p) => new(area.Left + (p.X - view.X) / view.Width * area.Width,
            area.Bottom - (p.Y - view.Y) / view.Height * area.Height);
        for (int i = 0; i <= 4; i++)
        {
            double y = view.Y + view.Height * i / 4, x = view.X + view.Width * i / 4;
            double py = Map(new Point(view.Left, y)).Y;
            double px = Map(new Point(x, view.Top)).X;
            dc.DrawLine(GridPen, new Point(area.Left, py), new Point(area.Right, py));
            dc.DrawLine(GridPen, new Point(px, area.Top), new Point(px, area.Bottom));
            Text(dc, y.ToString("G5", CultureInfo.CurrentCulture), 3, py - 7, 11, 62);
            Text(dc, x.ToString("G6", CultureInfo.CurrentCulture), px - 17, area.Bottom + 2, 11, 54);
        }
        if (view.Top < 0 && view.Bottom > 0)
        {
            double zero = Map(new Point(view.Left, 0)).Y;
            dc.DrawLine(ZeroPen, new Point(area.Left, zero), new Point(area.Right, zero));
        }
        Text(dc, XLabel, area.Left + Math.Max(0, area.Width / 2 - 25), area.Bottom + 16, 11);
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
        dc.Pop();
        DrawLegend(dc, area.Bottom + 32);
        if (IsKeyboardFocused)
            dc.DrawRectangle(null, MakePen(Brushes.SteelBlue, 1), new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)));
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

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
            Text(dc, line.Name, x + 24, y, 11, Math.Max(1, width - 25));
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
        double width = 300)
    {
        var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            font, size, Brushes.DarkSlateGray, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(label, new Point(x, y));
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
