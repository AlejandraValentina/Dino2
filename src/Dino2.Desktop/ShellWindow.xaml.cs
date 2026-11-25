using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MahApps.Metro.Controls;
using MahApps.Metro.IconPacks;

namespace Dino2.Desktop;

/// <summary>
/// Ventana principal. Aloja los simuladores de cada motor y alterna entre ellos desde el panel lateral.
/// Cada simulador se crea al elegirlo por primera vez y conserva su estado al cambiar de motor.
/// </summary>
public partial class ShellWindow : MetroWindow
{
    private static readonly DependencyPropertyDescriptor TitleDescriptor =
        DependencyPropertyDescriptor.FromProperty(TitleProperty, typeof(Window));

    internal sealed class EngineEntry(string label, string detail, PackIconMaterialKind icon, Func<EngineDocument> create)
        : INotifyPropertyChanged
    {
        public string Label { get; } = label;
        public string Detail { get; } = detail;
        public PackIconMaterialKind Icon { get; } = icon;
        /// <summary>Los documentos marcan los cambios sin guardar con un asterisco final en el título.</summary>
        public bool IsDirty => Document?.Title.EndsWith('*') == true;
        internal EngineDocument? Document { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;

        internal EngineDocument Open(ShellWindow shell)
        {
            if (Document is not null) return Document;
            Document = create();
            Document.Detach(shell);
            TitleDescriptor.AddValueChanged(Document, (_, _) =>
            {
                PropertyChanged?.Invoke(this, new(nameof(IsDirty)));
                if (shell.Active == Document) shell.Title = Document.Title;
            });
            return Document;
        }
    }

    internal IReadOnlyList<EngineEntry> Entries { get; }
    internal EngineDocument? Active => (EngineNav.SelectedItem as EngineEntry)?.Document;

    public ShellWindow() : this(null) { }

    /// <param name="confirmDiscard">Respuesta automática a las confirmaciones de descarte (pruebas).</param>
    internal ShellWindow(Func<bool>? confirmDiscard)
    {
        InitializeComponent();
        DesktopLayout.Fit(this);
        Entries =
        [
            new("Motor 4T", "Monocilíndrico", PackIconMaterialKind.Engine, () => new FourStrokeWindow(confirmDiscard)),
            new("Motor 2T", "Láminas · cárter 0D", PackIconMaterialKind.Numeric2BoxOutline, () => new TwoStrokeWindow()),
            new("Multicilíndrico 4T", "En línea · 2 a 4 cilindros", PackIconMaterialKind.ViewColumnOutline,
                () => new MulticylinderWindow(confirmDiscard)),
            new("Multicilíndrico 2T", "Cárteres separados", PackIconMaterialKind.ViewParallelOutline,
                () => new MultiTwoStrokeWindow(confirmDiscard)),
            new("Otto ideal", "Ciclo de referencia", PackIconMaterialKind.SineWave, () => new MainWindow()),
        ];
        EngineNav.Tag = true;
        EngineNav.ItemsSource = Entries;
        EngineNav.SelectedIndex = 0;
        SizeChanged += (_, _) => CompactNavigation = ActualWidth < CompactBelow;
        Closing += (_, e) =>
        {
            foreach (var entry in Entries)
                if (entry.Document is { } document && !document.CanCloseDocument())
                {
                    e.Cancel = true;
                    EngineNav.SelectedItem = entry;
                    return;
                }
        };
    }

    /// <summary>Ancho de ventana por debajo del cual el panel lateral muestra solo íconos.</summary>
    internal const double CompactBelow = 1200;

    /// <summary>Panel lateral reducido a íconos; el nombre de cada motor queda en su ayuda emergente.</summary>
    internal bool CompactNavigation
    {
        get => EngineNav.Tag is false;
        set
        {
            if (CompactNavigation == value) return;
            EngineNav.Tag = !value;
            NavPane.Width = value ? 52 : 176;
            NavHeader.Visibility = NavFooter.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Muestra el simulador indicado y lo devuelve.</summary>
    internal EngineDocument Select(int index)
    {
        EngineNav.SelectedIndex = index;
        return Active!;
    }

    private void EngineNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EngineNav.SelectedItem is not EngineEntry entry) return;
        var document = entry.Open(this);
        Workspace.Content = document.HostedRoot;
        Title = document.Title;
        // Los atajos del documento se reenvían a su raíz para que funcionen aunque el foco esté en el panel lateral.
        InputBindings.Clear();
        foreach (var shortcut in document.Shortcuts)
            InputBindings.Add(new KeyBinding(shortcut.Command, (KeyGesture)shortcut.Gesture) { CommandTarget = document.HostedRoot });
    }
}
