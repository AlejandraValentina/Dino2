using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MahApps.Metro.Controls;

namespace Dino2.Desktop.Tests;

/// <summary>La ventana única aloja los cinco simuladores y conserva el comportamiento de cada uno.</summary>
internal static class ShellSmoke
{
    internal static async Task Run()
    {
        static void Check(bool pass, string label)
        {
            if (!pass) throw new Exception(label);
            Console.WriteLine("PASS shell " + label);
        }
        static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        static IEnumerable<DependencyObject> Logical(DependencyObject root) =>
            new[] { root }.Concat(LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(Logical));

        var shell = new ShellWindow(() => true) { Width = 1100, Height = 720 };
        try
        {
            shell.Show();
            await Idle();
            var four = (FourStrokeWindow)shell.Active!;
            var workspace = (ContentControl)shell.FindName("Workspace");
            Check(shell.Entries.Count == 5 && workspace.Content == four.HostedRoot && !four.IsVisible,
                "inicia con el 4T alojado en la ventana única");
            var bore = (TextBox)four.FindName("Field_Bore");
            Check(Window.GetWindow(bore) == shell && four.DialogOwner == shell && bore.ActualHeight > 0,
                "los controles alojados se muestran y los diálogos pertenecen al shell");
            Check(shell.Title == four.Title, "el título del shell refleja el simulador activo");

            foreach (var (key, value) in new[] { ("RpmStart", "3000"), ("RpmEnd", "3000"), ("RpmStep", "1000") })
                ((TextBox)four.FindName("Field_" + key)).Text = value;
            Check(shell.Entries[0].IsDirty && shell.Title == four.Title && shell.Title.EndsWith('*'),
                "la edición marca el motor con cambios sin guardar");

            var nav = (ListBox)shell.FindName("EngineNav");
            nav.Focus();
            var simulate = shell.InputBindings.OfType<KeyBinding>().Single(b => b.Key == Key.F5);
            var command = (RoutedCommand)simulate.Command;
            Check(command.CanExecute(null, simulate.CommandTarget), "F5 disponible con el foco en el panel lateral");
            command.Execute(null, simulate.CommandTarget);
            await Idle();
            var cancel = (Button)four.FindName("CancelButton");
            var ring = Logical(four.HostedRoot!).OfType<MetroProgressBar>().Single();
            Check(cancel.IsEnabled && ring.Visibility == Visibility.Visible,
                "el atajo inicia el cálculo y el indicador enlazado por nombre lo muestra");
            for (int i = 0; i < 600 && cancel.IsEnabled; i++) await Task.Delay(50);
            Check(four.CurrentResult?.Points.Count == 1 && ring.Visibility == Visibility.Collapsed,
                "el cálculo alojado termina y publica resultados");

            var expected = new[] { typeof(FourStrokeWindow), typeof(TwoStrokeWindow), typeof(MulticylinderWindow),
                typeof(MultiTwoStrokeWindow), typeof(MainWindow) };
            for (int i = 1; i < expected.Length; i++)
            {
                var document = shell.Select(i);
                await Idle();
                Check(document.GetType() == expected[i] && workspace.Content == document.HostedRoot &&
                      document.DialogOwner == shell && !document.IsVisible && shell.Title == document.Title,
                    $"navega a {shell.Entries[i].Label}");
            }
            Check(shell.Select(0) == four && workspace.Content == four.HostedRoot && four.CurrentResult is not null &&
                  ((TextBox)four.FindName("Field_RpmStart")).Text == "3000", "volver al 4T conserva configuración y resultados");

            shell.Width = ShellWindow.CompactBelow - 100;
            await Idle();
            bool compact = shell.CompactNavigation;
            shell.Width = ShellWindow.CompactBelow + 100;
            await Idle();
            Check(compact && !shell.CompactNavigation, "el panel lateral se reduce a íconos en ventanas angostas");

            shell.Close();
            await Idle();
            Check(!shell.IsVisible, "el cierre consulta cada simulador y termina");
        }
        finally { if (shell.IsVisible) shell.Close(); }
    }
}
