using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dino2.Desktop;
using Dino2.Storage;

namespace Dino2.Desktop.Tests;

internal static class HistoricalStage13
{
    internal static int Run(string root)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Dino2.Desktop;component/Styles.xaml", UriKind.Relative)
        });
        int exit = 1;
        string Fixture(string current, string older)
        {
            var path = Path.Combine(root, "windows", current);
            return File.Exists(path) ? path : Path.Combine(root, "windows", older);
        }
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                var two = new TwoStrokeWindow();
                var savedTwo = TwoStrokeFiles.Load(Fixture("2t-full/engine-project.json", "ui-mono2/engine-project.json"));
                two.RestoreProject(savedTwo);
                Assert(savedTwo.CurrentResult is not null && savedTwo.OriginalResult is not null &&
                    ((DataGrid)two.FindName("ComparisonGrid")).Items.Count == 0, "2T abre curvas anteriores y no cruza revisiones");
                two.Close();

                var four = new MulticylinderWindow(() => true);
                var savedFour = MultiFiles.Load(Fixture("multi4/motor.json", "ui-multi4/motor.json"));
                four.Restore(savedFour);
                Assert(four.Snapshot().CurrentResult is not null && four.Snapshot().OriginalResult is not null &&
                    !((Button)four.FindName("MultiCheck")).IsEnabled && !((Button)four.FindName("MultiABExport")).IsEnabled,
                    "4T multicilíndrico conserva ambos resultados históricos y bloquea el contraste");
                await four.RunEditor();
                var solved = four.Snapshot();
                Assert(solved.CurrentResult?.Provenance?.NumericalRevision.Contains("ports-v3") == true &&
                    solved.OriginalResult?.Provenance?.NumericalRevision.Contains("ports-v3") == true &&
                    ((DataGrid)four.FindName("DifferenceRows")).Items.Count > 0,
                    "4T vuelve a calcular A y B con la revisión actual");
                four.Close();

                var sharedTwo = new MultiTwoStrokeWindow(() => true);
                var savedShared = TwoStrokeFiles.LoadMulti(Fixture("multi2/motor.json", "ui-multi2/motor.json"));
                sharedTwo.Restore(savedShared);
                Assert(sharedTwo.Snapshot().Curve is not null && sharedTwo.Snapshot().Baseline is not null &&
                    ((DataGrid)sharedTwo.FindName("DiffRows")).Items.Count == 0 &&
                    !((Button)sharedTwo.FindName("M2Compare")).IsEnabled,
                    "2T multicilíndrico restaura ambos resultados históricos con el contraste inhabilitado");
                sharedTwo.Close();
                exit = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Shutdown(); }
        }));
        app.Run();
        return exit;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
