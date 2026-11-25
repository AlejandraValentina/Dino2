using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dino2.Desktop;
using Dino2.Storage;
using Dino2.Core.GasDynamics;
using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Desktop.Tests;

internal static class FourStrokeSmoke
{
    internal static async Task PhysicalDefaults(string output)
    {
        var window = new FourStrokeWindow(() => true);
        try
        {
            window.Show();
            window.PhysicsModelIndex = 7;
            await window.SimulateAsync();
            var result = window.CurrentResult ?? throw new Exception(((TextBlock)window.FindName("Status")).Text);
            int[] rpm = Enumerable.Range(2, 11).Select(i => 500 * i).ToArray();
            if (!result.Configuration.Speeds.Values().SequenceEqual(rpm) || !result.Points.Select(p => p.Rpm).SequenceEqual(rpm) ||
                result.Failures.Count != 0 || result.Configuration.Ducts?.Courant != .20 || result.Configuration.Solver.MaximumStepDeg != .15 ||
                result.Configuration.Solver.PeriodicTolerance != 1e-6 || result.Configuration.Combustion.DurationDeg is not null)
                throw new Exception("Barrido físico predeterminado incompleto: " + string.Join(" | ", result.Failures));
            foreach (var point in result.Points)
                if (!(point.PeriodicError < 1e-6 && Math.Abs(point.MassResidualKg) < 1e-12 &&
                    Math.Abs(point.EnergyResidualJ) < 1e-7 && point.GasDynamics is { } diagnostics &&
                    Math.Abs(diagnostics.EnergyResidualJ) < 1e-7))
                    throw new Exception($"Periodicidad o balance incompatible a {point.Rpm} RPM.");
            FourStrokeFiles.Save(Path.Combine(output, "stage14b-4t-default.json"), new(result.Configuration, null));
            File.WriteAllText(Path.Combine(output, "stage14b-4t-default-result.json"),
                System.Text.Json.JsonSerializer.Serialize(result));
            Console.WriteLine($"PASS 4T Desktop · 11/11 RPM, CFL .20, paso .15°, periódico 1e-6 · {result.ElapsedMilliseconds:F0} ms");
        }
        finally { window.Close(); }
    }

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
            window.PhysicsModelIndex=0;
            await window.SimulateAsync();
            Assert(window.CurrentResult!.Model!=window.OriginalResult!.Model&&ReferenceEquals(window.OriginalResult,savedOriginal),
                "Reference v1 remains selectable while preserving saved original A");
            Assert(((TextBlock)window.FindName("ComparisonNotice")).Text.Contains("modelos diferentes"),"Cross-model A/B cannot masquerade as a mechanical gain");
            window.PhysicsModelIndex=1;
            await window.SimulateAsync();
            Assert(window.CurrentResult!.Model == ThermochemicalOptions.ModelId && window.CurrentResult.Configuration.Thermochemistry!.BurnLaw==BurnLaw.PrescribedWiebe,"Frozen species model supports prescribed reference combustion");
            window.LoadFrom(file);await window.SimulateAsync();
            var parameterTabs=(TabControl)window.FindName("ParameterTabs");parameterTabs.SelectedIndex=parameterTabs.Items.Count-1;
            Capture("stage2-evidence.png");parameterTabs.SelectedIndex=0;
            window.SaveTo(file);
            var ducts = FourStrokeFiles.Load("examples/4t-ducts.json").Current;
            string ductFile = Path.Combine(output, "4t-ui-ducts.json");
            FourStrokeFiles.Save(ductFile, new(ducts with { Speeds = new(4000, 4000, 1) }, null));
            window.LoadFrom(ductFile);
            Assert(window.PhysicsModelIndex == 5 && Field("IntakeDuctLength").IsEnabled, "1D project restores duct model and editable geometry");
            await window.SimulateAsync();
            Assert(window.CurrentResult?.Model == GasPath.ModelId && window.CurrentResult.Points.Single().GasDynamics is not null,
                "1D Windows calculation publishes diagnostics");
            var originalDuct = window.CurrentResult;
            window.KeepOriginal();
            Field("IntakeDuctLength").Text = "550";
            await window.SimulateAsync();
            Assert(ReferenceEquals(window.OriginalResult, originalDuct) &&
                window.CurrentResult!.Points.Single().VolumetricEfficiency != originalDuct!.Points.Single().VolumetricEfficiency,
                "Duct length changes B filling and preserves immutable A");
            tabs.SelectedIndex = 4; Capture("stage4-port-pressure.png");
            tabs.SelectedIndex = 2; Capture("stage4-comparison.png");
            window.SaveTo(ductFile);
            var ductProject = FourStrokeFiles.Load(ductFile);
            Assert(ductProject.Current.Ducts!.Intake.LengthM == .55 && ductProject.Original!.Ducts!.Intake.LengthM == .35,
                "Desktop saves both duct geometries");
            var example = FourStrokeFiles.Load("examples/4t-ducts-muscl-ab.json");
            FourStrokeConfiguration Reduced(FourStrokeConfiguration c, int intakeCells) => c with
            {
                Speeds = new(4000, 4000, 1),
                Ducts = c.Ducts! with { Intake = c.Ducts.Intake with { Cells = intakeCells }, Exhaust = c.Ducts.Exhaust with { Cells = 8 } }
            };
            string musclFile = Path.Combine(output, "4t-ui-muscl.json");
            FourStrokeFiles.Save(musclFile, new(Reduced(example.Current, 12), Reduced(example.Original!, 8)));
            window.LoadFrom(musclFile);
            await window.SimulateAsync();
            Assert(window.PhysicsModelIndex == 7 && window.CurrentResult?.Model == GasPath.MusclModelId, "MUSCL example selects the correct model");
            Assert(((Button)window.FindName("ResolveMeshButton")).IsEnabled, "Equivalent A/B enables resolution check");
            var originalMuscl = window.OriginalResult;
            await window.CheckResolutionAsync(4000);
            Assert(window.Resolution?.Rpm == 4000 && ReferenceEquals(window.OriginalResult, originalMuscl), "Refined A/B preserves original result");
            tabs.SelectedIndex = 2; Capture("stage4-closure-resolution.png");
            tabs.SelectedIndex = 4; Capture("stage4-closure-ports.png");
            string export = Path.Combine(output, "muscl.csv");
            window.ExportTo(export);
            Assert(File.Exists(Path.ChangeExtension(export, ".mesh.json")) &&
                File.ReadAllText(Path.ChangeExtension(export, ".trace.csv")).Contains("intake_flow_kg_s"), "Export includes mesh evidence and valve flows");
            Field("IntakeDuctLength").Text = "540";
            Assert(window.Resolution is null && window.CurrentResult is null, "Editing invalidates resolution and B only");
            Console.WriteLine($"{count}/{count} 4T desktop checks passed.");
        }
        finally { window.Close(); }
    }
}
