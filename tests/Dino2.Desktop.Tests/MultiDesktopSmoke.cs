using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dino2.Core.Multicylinder;
using Dino2.Desktop;
using Dino2.Storage;

namespace Dino2.Desktop.Tests;

internal static class MultiDesktopSmoke
{
    internal static async Task Run(string output)
    {
        var config = MultiConfiguration.Example();
        config = config with { Cylinder = config.Cylinder with { Speeds = new(4000, 4000, 1) } };
        var window = new MulticylinderWindow(() => true);
        T Find<T>(string name) where T : class => (T)window.FindName(name);
        void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent, Find<Button>(name)));
        static void Check(bool pass, string label)
        {
            if (!pass) throw new Exception(label);
            Console.WriteLine("PASS multi Desktop " + label);
        }
        try
        {
            window.Restore(new(config));
            window.Show();
            var task = window.RunEditor();
            Check(Find<Button>("MultiCancel").IsEnabled && !Find<TabControl>("Editor").IsEnabled,
                "la simulación ejecuta en segundo plano");
            await task;
            var run = window.Snapshot().CurrentResult;
            Check(run?.Points.Single().Status == MultiStatus.Converged, "muestra una simulación acoplada");
            Click("MultiKeep");
            var volume = Find<TextBox>("MultiField_IntakeVolume");
            volume.Text = "3.6";
            Check(window.Snapshot() is { CurrentResult: null, OriginalResult: not null },
                "la edición invalida B y conserva A");
            await window.RunEditor();
            Check(Find<DataGrid>("DifferenceRows").Items.Count == 1 && Find<DataGrid>("CylinderRows").Items.Count == 2,
                "A/B y diagnóstico individual en la misma aplicación");
            var curve = Find<ScientificPlot>("MultiPower");
            curve.ExportPng(Path.Combine(output, "multi-curvas.png"));
            var snapshot = window.Snapshot();
            string project = Path.Combine(output, "multi-ui.json");
            MultiFiles.Save(project, snapshot);
            await window.LoadProjectAsync(project);
            Check(window.Snapshot().Current == snapshot.Current && window.Snapshot().Original == config &&
                window.Snapshot().CurrentResult?.Points[0].Metrics == snapshot.CurrentResult?.Points[0].Metrics,
                "el proyecto recupera configuraciones y curvas");
            string csv = Path.Combine(output, "multi-ui-cycle.csv");
            MultiFiles.ExportTrace(csv, window.Snapshot().CurrentResult!, 4000);
            Check(File.ReadLines(csv).Count() > 1400, "exporta el ciclo completo por cilindro");
            var take = ((Panel)Find<StackPanel>("StudyCommands").Children[0]).Children.OfType<Button>().First();
            take.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, take));
            Check(Find<Button>("MultiStudyStart").IsEnabled, "previsualiza el plan de estudio");
            await window.RunStudyEditor();
            var study = window.Snapshot().Study;
            Check(study?.Run is { Cancelled: false, ExecutedCurves: 1 } && Find<DataGrid>("StudyRows").Items.Count == 2,
                "estudio real, deduplicación y clasificación");
            MultiFiles.Save(project, window.Snapshot());
            await window.LoadProjectAsync(project);
            Check(window.Snapshot().Study?.Run.Evaluations.Count == 2, "recupera estudio y curva desde el archivo");
            Click("MultiStudyApply");
            Check(window.Snapshot().CurrentResult is null && window.Snapshot().Original == config,
                "la variante del estudio se aplica a B");
            var cancelled = window.RunEditor();
            Click("MultiCancel");
            await cancelled;
            Check(window.Snapshot().CurrentResult is null && window.Snapshot().OriginalResult is not null,
                "cancelación conserva A y deja B sin resultados");
        }
        finally { window.Close(); }
    }
}
