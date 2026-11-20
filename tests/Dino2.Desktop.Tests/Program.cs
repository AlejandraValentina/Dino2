using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dino2.Desktop;
using Dino2.Storage;

namespace Dino2.Desktop.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/verification");
        Directory.CreateDirectory(output);
        int exit = 1;
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Dino2.Desktop;component/Styles.xaml", UriKind.Relative)
        });
        var window = new MainWindow();
        void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }
        TextBox Input(int index) => (TextBox)window.FindName("Input" + index);
        Button calculate = (Button)window.FindName("CalculateButton");
        void Calculate() => calculate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Screenshot(string name)
        {
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
                dc.DrawRectangle(window.Background, null, bounds);
                dc.DrawRectangle(new VisualBrush(content), null, bounds);
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name));
            encoder.Save(stream);
        }
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                Calculate();
                Assert(window.Result?.Points.Count == 11, "Window calculates default RPM sweep");
                Assert(((DataGrid)window.FindName("ResultsTable")).Items.Count == 11, "RPM grid receives results");
                Screenshot("desktop-default.png");
                var tabs = (TabControl)window.FindName("ResultTabs");
                tabs.SelectedIndex = 1;
                Screenshot("desktop-rpm-table.png");
                tabs.SelectedIndex = 2;
                Screenshot("desktop-states.png");
                tabs.SelectedIndex = 0;
                double original = window.Result!.WorkJ;
                Input(0).Text = "90,0";
                Assert(window.Result is null && !((Button)window.FindName("ExportButton")).IsEnabled,
                    "Editing removes previous results and disables export");
                Calculate();
                Assert(window.Result!.WorkJ > original, "Decimal comma and geometry affect the physical calculation");
                string path = Path.Combine(output, "ui-roundtrip.json");
                window.SaveTo(path);
                double expected = window.Result.WorkJ;
                Input(0).Text = "70";
                window.LoadFrom(path);
                Assert(window.Result is null, "Opening configuration requests recalculation");
                Calculate();
                Assert(Math.Abs(window.Result!.WorkJ - expected) < 1e-10, "Save/open reproduces result");
                Input(8).Text = "1000.5";
                Calculate();
                Assert(window.Result is null && ((TextBlock)window.FindName("Status")).Text.StartsWith("No se completó"),
                    "Fractional RPM rejected visibly without crashing");
                window.LoadFrom(path);
                Input(0).Text = "NaN";
                Calculate();
                Assert(window.Result is null, "Nonfinite editor input rejected");
                window.LoadFrom(path);
                File.WriteAllText(Path.Combine(output, "bad.json"), "{}");
                try { window.LoadFrom(Path.Combine(output, "bad.json")); }
                catch (System.Text.Json.JsonException) { }
                Calculate();
                Assert(Math.Abs(window.Result!.WorkJ - expected) < 1e-10, "Failed load preserves current configuration");
                window.Width = 960;
                window.Height = 680;
                Screenshot("desktop-minimum.png");
                window.Width = 1200;
                window.Height = 850;
                Input(5).Text = "0";
                Input(9).Text = "1000";
                Calculate();
                Assert(window.Result!.Points.Count == 1 && window.Result.WorkJ == 0, "Zero heat and single RPM render safely");
                Screenshot("desktop-zero-single.png");
                window.LoadFrom(path);
                Calculate();
                ResultCsv.Save(Path.Combine(output, "ui-results.csv"), window.Result!);
                window.SaveTo(path); // clean state avoids a close prompt
                await FourStrokeSmoke.Run(output);
                await StudySmoke.Run(output);
                await DesktopAuditSmoke.Run(output);
                await MultiDesktopSmoke.Run(output);
                Console.WriteLine("Desktop smoke checks passed; images in " + output);
                exit = 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); }
            finally { app.Shutdown(); }
        }));
        app.Run(window);
        return exit;
    }
}
