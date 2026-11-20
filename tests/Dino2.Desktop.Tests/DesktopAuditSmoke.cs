using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

        foreach (string example in Directory.EnumerateFiles("examples", "*.json"))
        {
            string name = Path.GetFileName(example);
            if (name.StartsWith("4t-", StringComparison.Ordinal)) FourStrokeFiles.Load(example);
            else if (name.StartsWith("2t-", StringComparison.Ordinal)) TwoStrokeFiles.Load(example);
            else if (name.StartsWith("otto-", StringComparison.Ordinal)) ConfigurationFile.Load(example);
            else if (name.StartsWith("study-", StringComparison.Ordinal)) StudyFiles.Load(example);
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
        }
        finally { four.Close(); }

        // A display-only stress fixture. Repeated traces exercise serialization and layout,
        // independent of claims about engine output at the repeated RPMs.
        var input = TwoStrokeConfiguration.Example with { Speeds = new(5000, 5000, 1) };
        var point = await Task.Run(() => TwoStrokeSimulator.Simulate(input, 5000));
        var basis = input with { Speeds = new(1000, 8900, 100) };
        var runs = basis.Speeds.Values().Select(rpm =>
            new TwoStrokeRun(rpm, point with { Rpm = rpm }, null, TwoStrokeRunStatus.Periodic)).ToArray();
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
        }
        finally { two.Close(); }
    }
}
