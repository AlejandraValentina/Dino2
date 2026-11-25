using System.Windows;
using System.Windows.Input;
using MahApps.Metro.Controls;

namespace Dino2.Desktop;

/// <summary>
/// Simulador de un tipo de motor. Puede mostrarse como ventana independiente (lo usan las pruebas)
/// o cederle su contenido a <see cref="ShellWindow"/>, que lo aloja junto a los demás motores.
/// </summary>
public class EngineDocument : MetroWindow
{
    private Window? host;

    /// <summary>Ventana dueña de diálogos y mensajes: el shell cuando el documento está alojado.</summary>
    internal Window DialogOwner => host ?? this;

    /// <summary>Raíz visual cedida al shell; nula mientras el documento sea una ventana independiente.</summary>
    internal FrameworkElement? HostedRoot { get; private set; }

    /// <summary>
    /// Entrega el contenido al shell. Conserva el ámbito de nombres para que los enlaces ElementName y
    /// <c>FindName</c> sigan resolviendo, y traslada los comandos de los atajos de teclado a la raíz.
    /// El documento queda como ventana oculta que nunca se muestra.
    /// </summary>
    internal FrameworkElement Detach(Window shell)
    {
        if (HostedRoot is not null) return HostedRoot;
        var root = (FrameworkElement)Content;
        NameScope.SetNameScope(root, NameScope.GetNameScope(this));
        Content = null;
        foreach (CommandBinding binding in CommandBindings.Cast<CommandBinding>().ToArray())
        {
            CommandBindings.Remove(binding);
            root.CommandBindings.Add(binding);
        }
        host = shell;
        return HostedRoot = root;
    }

    /// <summary>Atajos registrados por el documento, para que el shell los reenvíe a la raíz alojada.</summary>
    internal IEnumerable<KeyBinding> Shortcuts => InputBindings.OfType<KeyBinding>();

    /// <summary>
    /// Indica si el documento puede cerrarse. Si hay un cálculo en curso lo cancela y devuelve falso;
    /// si hay cambios sin guardar pide confirmación.
    /// </summary>
    internal virtual bool CanCloseDocument() => true;
}
