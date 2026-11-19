using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dino2.Desktop;
using Dino2.Storage;
using Dino2.Core.Thermochemistry;

namespace Dino2.Desktop.Tests;

internal static class FourStrokeSmoke
{
    public static async Task Run(string output)
    {
        var window = new FourStrokeWindow(() => true);
        window.Show();
        int count = 0;
        void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            count++; Console.WriteLine("PASS 4T UI " + message);
        }
        TextBox Field(string key) => (TextBox)window.FindName("Field_" + key);
        void Capture(string file)
        {
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
                dc.DrawRectangle(window.Background, null, bounds); dc.DrawRectangle(new VisualBrush(content), null, bounds);
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, file)); encoder.Save(stream);
        }
        try
        {
            Field("RpmStart").Text = "2000"; Field("RpmEnd").Text = "4000"; Field("RpmStep").Text = "1000";
            var pending = window.SimulateAsync();
            Assert(!((TabControl)window.FindName("ParameterTabs")).IsEnabled, "Inputs disabled during background calculation");
            await pending;
            Assert(window.CurrentResult?.Points.Count == 3, "3 RPM simulated off the UI thread");
            Assert(window.CurrentResult!.Model==ThermochemicalOptions.EquilibriumModelId&&window.CurrentResult.Points.All(p=>p.Chemistry is not null),"Default model includes species and chemical-energy diagnostics");
            Assert(Field("Lhv").IsReadOnly,"Reactive heating value is derived, not an independent heat input");
            var a = window.CurrentResult!;
            window.KeepOriginal();
            Field("IntakeLift").Text = "4,0";
            Assert(window.CurrentResult is null && ReferenceEquals(window.OriginalResult, a), "Editing invalidates B and preserves immutable A");
            Assert(!((Button)window.FindName("ExportButton")).IsEnabled, "Stale results cannot be exported");
            await window.SimulateAsync();
            Assert(window.CurrentResult!.Points[1].BrakePowerW < a.Points[1].BrakePowerW, "Valve lift changes physical simulation");
            Assert(((DataGrid)window.FindName("ComparisonGrid")).Items.Count == 3, "A/B differences visible at matching RPM");
            Capture("4t-curves-ab.png");
            var tabs = (TabControl)window.FindName("AnalysisTabs");
            tabs.SelectedIndex = 2; Capture("4t-comparison.png");
            tabs.SelectedIndex = 3; Capture("4t-cycle.png");
            tabs.SelectedIndex = 0;
            double expected = window.CurrentResult.Points[1].BrakePowerW;
            string file = Path.Combine(output, "4t-ui-project.json");
            window.SaveTo(file); window.LoadFrom(file);
            Assert(window.CurrentResult is null && window.OriginalResult is null, "Loaded project reconstructs both curves from saved inputs");
            await window.SimulateAsync();
            Assert(Math.Abs(window.CurrentResult!.Points[1].BrakePowerW - expected) < 1e-9 && window.OriginalResult!.Configuration == a.Configuration,
                "Project reload reproduces B and original A");
            Field("Rod").Text = "1"; await window.SimulateAsync();
            Assert(window.CurrentResult is null && ((TextBlock)window.FindName("Status")).Text.StartsWith("No se completó"), "Invalid mechanics rejected visibly");
            window.LoadFrom(file);
            Field("RpmEnd").Text = "6000"; Field("RpmStart").Text = "500"; Field("RpmStep").Text = "100";
            pending = window.SimulateAsync();
            ((Button)window.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending;
            Assert(window.CurrentResult is null && ((TextBlock)window.FindName("Status")).Text.StartsWith("Simulación cancelada"), "Cancellation preserves input and publishes no partial B");
            window.LoadFrom(file); await window.SimulateAsync();
            FourStrokeFiles.Export(Path.Combine(output, "4t-ui.csv"), window.CurrentResult!);
            FourStrokeFiles.ExportComparison(Path.Combine(output, "4t-ui.comparison.csv"), window.OriginalResult!, window.CurrentResult!);
            window.Width = 1080; window.Height = 750; Capture("4t-minimum.png");
            window.Width=1350;window.Height=900;
            var savedOriginal=window.OriginalResult;
            ((ComboBox)window.FindName("PhysicsModel")).SelectedIndex=0;
            await window.SimulateAsync();
            Assert(window.CurrentResult!.Model!=window.OriginalResult!.Model&&ReferenceEquals(window.OriginalResult,savedOriginal),
                "Reference v1 remains selectable while preserving saved original A");
            Assert(((TextBlock)window.FindName("ComparisonNotice")).Text.Contains("modelos diferentes"),"Cross-model A/B cannot masquerade as a mechanical gain");
            ((ComboBox)window.FindName("PhysicsModel")).SelectedIndex=1;
            await window.SimulateAsync();
            Assert(window.CurrentResult!.Model == ThermochemicalOptions.ModelId && window.CurrentResult.Configuration.Thermochemistry!.BurnLaw==BurnLaw.PrescribedWiebe,"Frozen species model supports prescribed reference combustion");
            window.LoadFrom(file);await window.SimulateAsync();
            var parameterTabs=(TabControl)window.FindName("ParameterTabs");parameterTabs.SelectedIndex=parameterTabs.Items.Count-1;
            Capture("stage2-evidence.png");parameterTabs.SelectedIndex=0;
            window.SaveTo(file);
            Console.WriteLine($"{count}/{count} 4T desktop checks passed.");
        }
        finally { window.Close(); }
    }
}
