using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.TwoStroke;
using Dino2.Desktop;
using Dino2.Storage;

namespace Dino2.Desktop.Tests;

internal static class DesktopAuditSmoke
{
    internal static async Task Run(string output)
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Console.WriteLine("PASS audit " + message);
        }

        string MapCsv(string path, string model, string direction = "both", string referenceArea = "curtain_area")
        {
            File.WriteAllText(path, $"#reference_area={referenceArea}\n#pressure_convention=upstream_stagnation_to_downstream_static\n#direction={direction}\n#units=opening_fraction,pressure_ratio,dimensionless\n#source=synthetic-desktop-smoke\n#compatible_model={model}\n#data_kind=synthetic\nopening_fraction,pressure_ratio,cd\n0,0.2,0.65\n0,1,0.65\n1,0.2,0.7\n1,1,0.7\n");
            return path;
        }

        foreach (string example in Directory.EnumerateFiles("examples", "*.json"))
        {
            string name = Path.GetFileName(example);
            if (name.StartsWith("4t-", StringComparison.Ordinal)) FourStrokeFiles.Load(example);
            else if (name.StartsWith("2t-", StringComparison.Ordinal)) TwoStrokeFiles.Load(example);
            else if (name.StartsWith("otto-", StringComparison.Ordinal)) ConfigurationFile.Load(example);
            else if (name.StartsWith("study-", StringComparison.Ordinal)) StudyFiles.Load(example);
            else if (name.StartsWith("multi-2t-", StringComparison.Ordinal)) TwoStrokeFiles.LoadMulti(example);
            else if (name.StartsWith("multi-", StringComparison.Ordinal)) MultiFiles.Load(example);
        }
        Console.WriteLine("PASS audit compatible versioned desktop examples");

        var plot = new ScientificPlot { Caption = "Par [N·m]" };
        Point[] points = [new(2000, 2), new(2500, 11), new(double.NaN, 3), new(5000, 4)];
        plot.SetLines([new("A", points, Brushes.SteelBlue)]);
        plot.Measure(new Size(520, 250));
        plot.Arrange(new Rect(0, 0, 520, 250));
        var extent = plot.View;
        plot.Zoom(.5, new Point(.5, .5));
        Check(plot.View.Width == extent.Width / 2 && plot.View.Height == extent.Height / 2, "plot zoom reduces both axes");
        var nearest = ScientificPlot.Nearest(points, new Point(2800, 10), extent);
        Check(nearest == points[1], "plot cursor reports a calculated sample, ignoring gaps");
        plot.ResetView();
        Check(plot.View == extent && extent.Y == 0, "plot resets to the original zero-based scale");
        string png = Path.Combine(output, "stage8-plot.png");
        plot.ExportPng(png);
        using (var image = File.OpenRead(png))
            Check(new PngBitmapDecoder(image, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0].PixelWidth >= 520,
                "graph exports a readable PNG");

        var four = new FourStrokeWindow(() => true) { Width = 824, Height = 531 };
        try
        {
            four.Show();
            var speeds = new[] { ("RpmStart", "2000"), ("RpmEnd", "4000"), ("RpmStep", "1000") };
            foreach (var (key, value) in speeds) ((TextBox)four.FindName($"Field_{key}")).Text = value;
            var inactive = EngineParameter.All.First(parameter => parameter.Group == "Conductos");
            ((TextBox)four.FindName($"Field_{inactive.Key}")).Text = "inactivo";
            await four.SimulateAsync();
            Check(four.CurrentResult?.Points.Count == 3, "inactive duct parameters cannot block 0D runs");
            var selected = (ComboBox)four.FindName("TraceRpm");
            selected.SelectedItem = 4000;
            four.KeepOriginal();
            Check(Equals(selected.SelectedItem, 4000), "4T preserves inspected RPM when retaining A");
            string export = Path.Combine(output, "audit-export.csv");
            four.ExportTo(export);
            string trace = Path.ChangeExtension(export, ".trace.csv");
            Check(File.ReadLines(trace).Any(line => line.StartsWith("# rpm=4000", StringComparison.Ordinal)), "4T exports trace for the inspected RPM");
            four.UpdateLayout();
            Check(((Button)four.FindName("CancelButton")).ActualHeight > 0, "compact window retains the cancellation control");
            Check(four.FindName("ImportIntakeMapButton") is Button && four.FindName("ImportExhaustMapButton") is Button &&
                four.FindName("ClearDischargeMapsButton") is Button && four.FindName("ViewDischargeMapsButton") is Button,
                "4T Desktop exposes map import, domain viewer and constant-Cd controls");
            var fourMap = DischargeMapFile.Load(MapCsv(Path.Combine(output, "audit-4t-map.csv"), "four-stroke-0d-isentropic-nozzle"));
            four.ApplyDischargeMap(true, fourMap);
            string fourMapProject = Path.Combine(output, "audit-4t-map-project.json");
            four.SaveTo(fourMapProject);
            var savedFourMap = FourStrokeFiles.Load(fourMapProject).Current.IntakeValve.DischargeMap;
            Check(savedFourMap is not null && savedFourMap.Metadata.DataSha256 == fourMap.Sha256 &&
                savedFourMap.Points.SequenceEqual(fourMap.Map.Points), "4T Desktop saves and reopens Cd map points and provenance");
            var fourViewer = new DischargeMapWindow([("4T · admisión", savedFourMap!)]);
            fourViewer.Show();
            Check(((DataGrid)fourViewer.FindName("MapPoints")).Items.Count == 4 &&
                ((TextBlock)fourViewer.FindName("MapMetadata")).Text.Contains(fourMap.Sha256, StringComparison.Ordinal),
                "4T Cd viewer displays every sample, provenance and coverage");
            fourViewer.Close();
            four.ClearDischargeMaps();
            string constantProject = Path.Combine(output, "audit-4t-constant-cd.json");
            four.SaveTo(constantProject);
            Check(FourStrokeFiles.Load(constantProject).Current.IntakeValve.DischargeMap is null &&
                four.FindName("ValveProfileInfo") is TextBlock, "4T Desktop can return to constant Cd and displays map coverage");
        }
        finally { four.Close(); }

        // A display-only stress fixture. Repeated traces exercise serialization and layout,
        // independent of claims about engine output at the repeated RPMs.
        var input = TwoStrokeConfiguration.Example with { Speeds = new(5000, 5000, 1) };
        var point = await Task.Run(() => TwoStrokeSimulator.Simulate(input, 5000));
        var basis = input with { Speeds = new(1000, 8900, 100) };
        var runs = basis.Speeds.Values().Select(rpm =>
            new TwoStrokeRun(rpm, point with { Rpm = rpm, IndicatedPowerW = point.IndicatedPowerW * rpm / 5000,
                ShaftPowerW = point.ShaftPowerW * rpm / 5000 }, null, TwoStrokeRunStatus.Periodic)).ToArray();
        var project = new TwoStrokeProject(basis, null, new(basis, runs, TwoStrokeConfiguration.NumericalRevision));
        string largePath = Path.Combine(output, "audit-large-project.json");
        TwoStrokeFiles.Save(largePath, project);
        string badPath = Path.Combine(output, "audit-malformed-project.json");
        var invalid = JsonNode.Parse(File.ReadAllText(largePath))!;
        invalid["project"]!["currentResult"]!["runs"]![0] = null;
        File.WriteAllText(badPath, invalid.ToJsonString());

        var two = new TwoStrokeWindow { Width = 824, Height = 531 };
        try
        {
            two.Show();
            bool dispatcherRan = false;
            using var process = Process.GetCurrentProcess();
            var elapsed = Stopwatch.StartNew();
            var pending = two.LoadProjectAsync(largePath);
            await two.Dispatcher.InvokeAsync(() => dispatcherRan = !pending.IsCompleted, DispatcherPriority.Send);
            await pending;
            Console.WriteLine($"AUDIT large_project_bytes={new FileInfo(largePath).Length}; rpm={runs.Length}; " +
                $"open_ms={elapsed.ElapsedMilliseconds}; working_set_bytes={process.WorkingSet64}");
            Check(dispatcherRan, "reading a large 2T project yields to the WPF dispatcher");
            Check(two.CurrentConfiguration() == basis, "large project restores all settings");
            var resultGrid = (DataGrid)two.FindName("ResultGrid");
            Check(resultGrid.Items.Count == runs.Length, "the large 2T result displays all RPMs");
            var status = (TextBlock)two.FindName("Status");
            await two.Guard(() => two.LoadProjectAsync(badPath));
            Check(resultGrid.Items.Count == runs.Length && status.Text.Contains("deben corresponder"),
                "malformed nested project leaves the previous work intact");
            await two.Guard(() => two.LoadProjectAsync(Path.Combine(output, "audit-absent.json")));
            Check(resultGrid.Items.Count == runs.Length, "missing file leaves the previous work intact");
            Check(two.FindName("ImportTransferMapButton") is Button && two.FindName("ImportExhaustMapButton") is Button &&
                two.FindName("ImportReedMapButton") is Button && two.FindName("ClearCdMapsButton") is Button &&
                two.FindName("ViewCdMapsButton") is Button,
                "2T Desktop exposes distinct transfer, exhaust and reed map controls");
            var pressureConfig = basis with { Numerics = basis.Numerics with { Exterior = ExteriorBoundary.PressureReservoir } };
            string pressureProject = Path.Combine(output, "audit-2t-pressure-project.json");
            TwoStrokeFiles.Save(pressureProject, new(pressureConfig, null));
            await two.LoadProjectAsync(pressureProject);
            var transferMap = DischargeMapFile.Load(MapCsv(Path.Combine(output, "audit-2t-transfer-map.csv"),
                "two-stroke-0d-isentropic-nozzle", "reverse", "port_open_area"));
            two.ApplyPortMap(true, transferMap);
            var exhaustMap = DischargeMapFile.Load(MapCsv(Path.Combine(output, "audit-2t-exhaust-map.csv"),
                "ports-v4-equivalent-capacity-v1", "forward", "port_open_area"));
            two.ApplyPortMap(false, exhaustMap);
            var reedMap = DischargeMapFile.Load(MapCsv(Path.Combine(output, "audit-2t-reed-map.csv"),
                "ports-v4-equivalent-capacity-v1", "both", "reed_open_area"));
            two.ApplyReedMap(reedMap);
            string mappedProject = Path.Combine(output, "audit-2t-mapped-project.json");
            TwoStrokeFiles.Save(mappedProject, new(two.CurrentConfiguration(), null));
            var reopened = TwoStrokeFiles.Load(mappedProject).Current;
            Check(reopened.Transfer.ReverseDischargeMap?.Metadata.DataSha256 == transferMap.Sha256 &&
                reopened.Exhaust.DischargeMap?.Metadata.DataSha256 == exhaustMap.Sha256 &&
                reopened.Reed.DischargeMap?.Metadata.DataSha256 == reedMap.Sha256,
                "2T Desktop stores transfer, ports-v4 exhaust and reed maps with provenance");
            var twoViewer = new DischargeMapWindow(new[]
            {
                ("2T · transfer", reopened.Transfer.ReverseDischargeMap!),
                ("2T · escape", reopened.Exhaust.DischargeMap!),
                ("2T · láminas", reopened.Reed.DischargeMap!)
            });
            twoViewer.Show();
            Check(((DataGrid)twoViewer.FindName("MapPoints")).Items.Count == 12 &&
                ((TextBlock)twoViewer.FindName("MapMetadata")).Text.Contains("reed_open_area", StringComparison.Ordinal),
                "2T Cd viewer displays transfer, exhaust and reed samples with conventions");
            twoViewer.Close();
            two.ClearPortMaps();
            Check(two.CurrentConfiguration().Transfer.DischargeMap is null && two.CurrentConfiguration().Exhaust.DischargeMap is null &&
                two.CurrentConfiguration().Reed.DischargeMap is null, "2T Desktop returns ports and reed to constant Cd");
            await two.LoadProjectAsync(pressureProject);
        }
        finally { two.Close(); }
    }
}
