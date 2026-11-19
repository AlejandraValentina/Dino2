using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dino2.Core.FourStroke;
using Dino2.Core.Studies;
using Dino2.Core.Thermochemistry;
using Dino2.Desktop;
using Dino2.Storage;

namespace Dino2.Desktop.Tests;

internal static class StudySmoke
{
    internal static async Task Run(string output)
    {
        var window = new FourStrokeWindow(() => true) { Width = 1320, Height = 880 };
        window.Show();
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
            Console.WriteLine("PASS study UI " + message);
        }

        try
        {
            var speeds = new RpmRange(2500, 3500, 500);
            var engine = ThermochemicalOptions.EquilibriumExample with { Speeds = speeds };
            var path = Path.Combine(output, "study-engine.json");
            FourStrokeFiles.Save(path, new(engine, engine));
            window.LoadFrom(path);
            await window.SimulateAsync();
            var original = window.OriginalResult;
            Check(original?.Configuration == engine, "Opens existing 4T and preserves A");

            var panel = (StudyPanel)window.FindName("Studies");
            ((TabItem)window.FindName("StudyTab")).IsSelected = true;
            ((Button)panel.FindName("StudyUseCurrent")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var plan = new StudyDefinition(engine,
                [new(StudyParameter.Spark, 15, 25, 5), new(StudyParameter.Compression, 9, 10, 1)],
                speeds, new(ObjectiveKind.MeanPower, new(2500, 3500)));
            panel.PopulatePlan(plan);
            Check(((TextBlock)panel.FindName("Preview")).Text.Contains("6 variantes") &&
                ((TextBlock)panel.FindName("Preview")).Text.Contains("18 simulaciones"), "Previews unique cost before running");
            Capture("stage5-plan.png");

            var running = panel.RunAsync();
            Check(panel.IsBusy && ((Button)panel.FindName("StudyCancel")).IsEnabled, "Runs outside the UI thread with cancellation");
            await running;
            var run = panel.LastRun ?? throw new Exception(((TextBlock)panel.FindName("StudyMessage")).Text);
            var ranked = StudyRunner.Rank(run.Definition.Objective, run.Evaluations);
            Check(run.Evaluations.Count == 7 && ranked.Count == 6, "Shows complete factorial results and objective rank");
            var chosen = ranked[0].Id;
            var data = (DataGrid)panel.FindName("StudyRows");
            data.SelectedIndex = 1;
            await panel.VerifyAsync(chosen, 0);
            Check(panel.Checks.Single().Levels.Count == 3 && ((TextBlock)panel.FindName("CheckText")).Text.Contains("Banda empírica"),
                "Separates sampled rank from comparison at three resolutions");
            Capture("stage5-ranked.png");
            var pages = (TabControl)panel.FindName("Pages");
            pages.SelectedIndex = 2; Capture("stage5-curves.png");
            pages.SelectedIndex = 4; Capture("stage5-map.png");

            string file = Path.Combine(output, "stage5-ui.dino-study.json");
            string csv = Path.Combine(output, "stage5-ui.csv");
            panel.SaveTo(file);
            panel.LoadFrom(file);
            panel.ExportTo(csv);
            Check(panel.LastRun!.Evaluations.Count == 7 && panel.Checks.Count == 1 &&
                File.ReadAllText(csv).Contains("# resolution="), "Reloads evaluated curves and exports resolution evidence");
            panel.ApplyVariant(chosen);
            Check(ReferenceEquals(window.OriginalResult, original) && window.CurrentResult is null, "Applying B keeps original A and invalidates stale manual B");
            await window.SimulateAsync();
            Check(window.CurrentResult!.Configuration == StudyPlan.Create(plan).Variants[chosen].Configuration,
                "Manually simulates the selected configuration from the study");

            ((TabItem)window.FindName("StudyTab")).IsSelected = true;
            panel.PopulatePlan(plan with { Speeds = new(1000, 5000, 1000), Objective = new(ObjectiveKind.MeanPower) });
            running = panel.RunAsync();
            panel.Cancel();
            await running;
            Check(panel.LastRun is { Cancelled: true } && !panel.IsBusy, "Cancellation leaves a safe inspectable state");

            void Capture(string fileName)
            {
                window.UpdateLayout();
                var content = (FrameworkElement)window.Content;
                var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
                    dc.DrawRectangle(window.Background, null, bounds);
                    dc.DrawRectangle(new VisualBrush(content), null, bounds);
                }
                image.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = File.Create(Path.Combine(output, fileName));
                encoder.Save(stream);
            }

            Console.WriteLine($"{checks}/{checks} study desktop checks passed.");
        }
        finally { window.Close(); }
    }
}
