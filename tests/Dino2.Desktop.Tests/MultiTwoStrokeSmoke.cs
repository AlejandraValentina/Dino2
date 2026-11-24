using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dino2.Core.Studies;
using Dino2.Core.TwoStroke;
using Dino2.Desktop;
using Dino2.Storage;

namespace Dino2.Desktop.Tests;

internal static class MultiTwoStrokeSmoke
{
    internal static async Task Run(string output)
    {
        var app = new MultiTwoStrokeWindow(() => true);
        var config = MultiTwoStrokeConfiguration.Example();
        config = config with { Cylinder = config.Cylinder with { Speeds = new(5000, 5000, 1) } };
        T Item<T>(string name) where T : class => (T)app.FindName(name);
        void Click(string name) => Item<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static void Check(bool pass, string label)
        {
            if (!pass) throw new Exception(label);
            Console.WriteLine("PASS multi 2T Desktop " + label);
        }
        try
        {
            app.Restore(new(config));
            app.Show();
            var calculation = app.RunEditor();
            Check(Item<Button>("M2Cancel").IsEnabled, "cálculo de fondo");
            await calculation;
            Check(app.Snapshot().Curve?.Complete == true && Item<DataGrid>("CylinderRows").Items.Count == 2, "motor acoplado, ciclo y barrido");
            Click("M2Keep");
            Item<TextBox>("M2Network_EVolume").Text = "0.75";
            Check(app.Snapshot() is { Curve: null, Baseline: not null }, "edición de escape preserva A");
            await app.RunEditor();
            Check(app.Snapshot().Curve?.Complete == true && Item<DataGrid>("DiffRows").Items.Count == 1, "comparación A/B");
            var project = app.Snapshot();
            var a = project.Baseline!;
            var b = project.Curve!;
            double reference = a.Runs.Single().Point!.ShaftTorqueNm, candidate = b.Runs.Single().Point!.ShaftTorqueNm;
            var levels = new[] { 1, 2, 4 }.Select(scale =>
                new StudyLevel(scale, candidate, reference, candidate - reference, null, null, null)).ToArray();
            var (verdict, band) = StudyVerification.Classify(levels);
            var evidence = new StudyCheck(1, 0, verdict, band, levels, 0,
                NumericalRevision: b.Provenance!.NumericalRevision);
            project = project with { Contrast = new(5000, evidence) };
            string file = Path.Combine(output, "multi2t-ui.json");
            TwoStrokeFiles.SaveMulti(file, project);
            await app.LoadProjectAsync(file);
            Check(app.Snapshot().Original == config && app.Snapshot().Curve?.Complete == true &&
                app.Snapshot().Contrast?.Check.NumericalRevision == evidence.NumericalRevision, "guardado y reapertura con contraste");
            var legacyEvidence = evidence with { NumericalRevision = MultiTwoStrokeConfiguration.ModelId };
            TwoStrokeFiles.SaveMulti(file, project with { Contrast = new(5000, legacyEvidence) });
            Check(TwoStrokeFiles.LoadMulti(file).Contrast?.Check.NumericalRevision == legacyEvidence.NumericalRevision,
                "conserva el contraste histórico del esquema sin presión prescrita");
            bool rejected = false;
            try
            {
                TwoStrokeFiles.SaveMulti(file, project with
                {
                    Contrast = new(5000, evidence with { NumericalRevision = "otra-revisión" })
                });
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "rechaza un contraste de revisión incompatible");
            string csv = Path.Combine(output, "multi2t-ui-cycle.csv");
            TwoStrokeFiles.ExportMultiCycle(csv, app.Snapshot().Curve!, 5000);
            Check(File.ReadLines(csv).Count() > 600, "CSV trazable de ambos cilindros");
            Click("M2TakeBase");
            Check(Item<Button>("M2StudyRun").IsEnabled, "plan con tamaño anticipado");
            await app.RunStudy();
            Check(app.Snapshot().Study?.Rows.Count == 2 && app.Snapshot().Study?.Rows.All(r => r.Curve?.Complete == true) == true, "estudio físico y clasificación");
            TwoStrokeFiles.SaveMulti(file, app.Snapshot());
            await app.LoadProjectAsync(file);
            Check(app.Snapshot().Study?.Rows.Count == 2, "recupera el estudio");
            Click("M2Apply");
            Check(app.Snapshot() is { Curve: null, Original: not null } && app.Snapshot().Original == config, "aplica B con A inmutable");
            var inFlight = app.RunEditor();
            Click("M2Cancel");
            await inFlight;
            Check(app.Snapshot() is { Curve: null, Baseline: not null }, "cancelación preserva A");
        }
        finally { app.Close(); }
    }
}
