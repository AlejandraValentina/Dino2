using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Dino2.Core.GasDynamics;
using Dino2.Core.Multicylinder;
using Dino2.Core.TwoStroke;
using Dino2.Storage;
using Microsoft.Win32;

namespace Dino2.Desktop;

public partial class MultiTwoStrokeWindow : Window
{
    private MultiTwoStrokeConfiguration template = MultiTwoStrokeConfiguration.Example();
    private MultiTwoStrokeConfiguration? original;
    private MultiTwoStrokeSweep? curve, baseline;
    private TwoStrokeComparisonEvidence? contrast;
    private CancellationTokenSource? operation;
    private readonly Func<bool>? confirm;
    private bool initializing = true, populating, dirty, changingRpm;
    private readonly Dictionary<TwoStrokeWindow.Field, TextBox> editor = [];
    private readonly Dictionary<string, TextBox> network = [];
    private readonly Dictionary<string, ScrollViewer> groups = [];
    private readonly ComboBox count = new() { Name = "M2Count", ItemsSource = new[] { 2, 3, 4 } };
    private readonly ComboBox scheme = new() { ItemsSource = Enum.GetValues<DuctScheme>() };
    private readonly CheckBox equilibrium = new() { Content = "Equilibrio UV en tramo cerrado" };
    private readonly CheckBox sharedIntake = new() { Content = "Admisión compartida", Name = "M2SharedIntake" };
    private readonly CheckBox sharedExhaust = new() { Content = "Escape compartido", Name = "M2SharedExhaust" };
    private readonly TextBox[] phases = [new(), new(), new()];
    private readonly TextBlock diagram = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public MultiTwoStrokeWindow() : this(null) { }
    internal MultiTwoStrokeWindow(Func<bool>? confirmDiscard)
    {
        confirm = confirmDiscard;
        InitializeComponent();
        DesktopLayout.Fit(this);
        BuildEditor();
        BuildStudy();
        foreach (var grid in new[] { RunRows, CylinderRows, DiffRows, StudyRows, StudyRunRows }) DesktopLayout.FormatGrid(grid);
        CycleSource.ItemsSource = new[] { "Actual / B", "Original A" };
        CycleSource.SelectedIndex = 0;
        initializing = false;
        Populate(template);
        Render();
        Closing += (_, e) => e.Cancel = !CanCloseFromOwner();
    }

    internal bool CanCloseFromOwner()
    {
        if (operation is null) return CanDiscard();
        operation.Cancel();
        M2Status.Text = "Cancelando el trabajo antes de cerrar.";
        return false;
    }

    private bool CanDiscard() => !dirty || (confirm?.Invoke() ?? DesktopLayout.ConfirmDiscard(this, "Multicilíndrico 2T"));

    private void BuildEditor()
    {
        StackPanel Group(string name)
        {
            if (groups.TryGetValue(name, out var old)) return (StackPanel)old.Content;
            var panel = new StackPanel { Margin = new Thickness(9) };
            groups.Add(name, new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            return panel;
        }
        var general = Group("Motor y fases");
        RegisterName(count.Name, count);
        Label(general, "Cilindros en línea", count);
        count.SelectionChanged += (_, _) =>
        {
            if (populating || count.SelectedItem is not int n) return;
            populating = true;
            SetPhases(FiringPhases.Even(n, 360));
            populating = false;
            Edited();
        };
        for (int i = 0; i < phases.Length; i++)
        {
            phases[i].Name = $"M2Phase{i + 2}";
            RegisterName(phases[i].Name, phases[i]);
            Label(general, $"Cilindro {i + 2} · desfase [°]", phases[i]);
            phases[i].TextChanged += (_, _) => Edited();
        }
        foreach (var toggle in new[] { sharedIntake, sharedExhaust })
        {
            RegisterName(toggle.Name, toggle);
            toggle.Margin = new Thickness(0, 7, 0, 3);
            general.Children.Add(toggle);
            toggle.Checked += (_, _) => Edited();
            toggle.Unchecked += (_, _) => Edited();
        }
        general.Children.Add(diagram);
        foreach (var (key, caption) in new (string, string)[]
        {
            ("AVolume", "Unión de admisión · volumen [L]"),
            ("ALength", "Tronco de admisión · longitud [mm]"), ("AOuter", "Tronco · Ø en depósito [mm]"),
            ("AInner", "Tronco · Ø en unión [mm]"), ("ACells", "Tronco de admisión · celdas"),
            ("EVolume", "Unión de escape · volumen [L]"),
            ("ELength", "Primario de escape · longitud [mm]"), ("EOuter", "Primario · Ø en unión [mm]"),
            ("EInner", "Primario · Ø en lumbrera [mm]"), ("ECells", "Primario de escape · celdas")
        })
        {
            var box = new TextBox { Name = "M2Network_" + key };
            AutomationProperties.SetAutomationId(box, box.Name);
            RegisterName(box.Name, box);
            network.Add(key, box);
            Label(Group(key[0] == 'A' ? "Colector de admisión" : "Colector de escape"), caption, box);
            box.TextChanged += (_, _) => Edited();
        }

        foreach (var field in TwoStrokeWindow.DefineFields())
        {
            var box = new TextBox();
            editor.Add(field, box);
            Label(Group(field.Group), field.Label, box);
            box.TextChanged += (_, _) => Edited();
        }
        Group("Combustión y pérdidas").Children.Add(equilibrium);
        equilibrium.Checked += (_, _) => Edited();
        equilibrium.Unchecked += (_, _) => Edited();
        Label(Group("Resolución numérica"), "Esquema 1D", scheme);
        scheme.SelectionChanged += (_, _) => Edited();
        EditorGroups.ItemsSource = groups.Keys;
        EditorGroups.SelectionChanged += (_, _) =>
        {
            if (EditorGroups.SelectedItem is string group) EditorContent.Content = groups[group];
        };
        EditorGroups.SelectedIndex = 0;
    }

    private static void Label(Panel panel, string name, Control input)
    {
        AutomationProperties.SetName(input, name);
        panel.Children.Add(new Label { Content = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap },
            Target = input, Padding = new Thickness(0, 7, 0, 3) });
        panel.Children.Add(input);
    }

    private static string Text(double value) => value.ToString("R", CultureInfo.CurrentCulture);
    private static double Number(TextBox field) => TwoStrokeWindow.Parse(field.Text, AutomationProperties.GetName(field));
    private static int Integer(TextBox field)
    {
        double value = Number(field);
        if (value != Math.Truncate(value) || value is < 0 or > int.MaxValue)
            throw new ArgumentException("Ingresá valores enteros para celdas o RPM.");
        return (int)value;
    }

    private void SetPhases(FiringPhases layout)
    {
        var values = layout.Values();
        for (int i = 0; i < phases.Length; i++)
        {
            bool inLayout = i + 1 < values.Length;
            phases[i].IsEnabled = inLayout;
            phases[i].Text = inLayout ? Text(values[i + 1]) : "";
        }
    }

    private FiringPhases ReadPhases()
    {
        int cylinders = (int)count.SelectedItem;
        return new(Number(phases[0]), cylinders >= 3 ? Number(phases[1]) : null,
            cylinders >= 4 ? Number(phases[2]) : null);
    }

    private MultiTwoStrokeConfiguration ReadEditor()
    {
        var c = template.Cylinder;
        foreach (var (field, box) in editor)
        {
            double value = Number(box);
            if (value != field.Read(c)) c = field.Set(c, value);
        }
        c = c with { Combustion = c.Combustion with { ProductEquilibrium = equilibrium.IsChecked == true },
            Numerics = c.Numerics with { Scheme = (DuctScheme)scheme.SelectedItem } };
        var sample = MultiTwoStrokeConfiguration.Example();
        var a = template.SharedIntake ?? sample.SharedIntake!;
        var e = template.SharedExhaust ?? sample.SharedExhaust!;
        double Si(string key, double old)
        {
            double value = Number(network[key]);
            return value == old * 1000 ? old : value / 1000;
        }
        DuctGeometry Geometry(string suffix, DuctGeometry old) => new(Si(suffix + "Length", old.LengthM),
            Si(suffix + "Outer", old.ReservoirDiameterM), Si(suffix + "Inner", old.ValveDiameterM), Integer(network[suffix + "Cells"]));
        var config = new MultiTwoStrokeConfiguration(c, ReadPhases(),
            sharedIntake.IsChecked == true ? new(Geometry("A", a.Trunk), Si("AVolume", a.VolumeM3)) : null,
            sharedExhaust.IsChecked == true ? new(Geometry("E", e.Primary), Si("EVolume", e.JunctionVolumeM3)) : null);
        config.Validate();
        return config;
    }

    private void Populate(MultiTwoStrokeConfiguration config)
    {
        populating = true;
        template = config;
        count.SelectedItem = config.Phases.Count;
        SetPhases(config.Phases);
        equilibrium.IsChecked = config.Cylinder.Combustion.ProductEquilibrium;
        scheme.SelectedItem = config.Cylinder.Numerics.Scheme;
        sharedIntake.IsChecked = config.SharedIntake is not null;
        sharedExhaust.IsChecked = config.SharedExhaust is not null;
        foreach (var (field, box) in editor) box.Text = Text(field.Read(config.Cylinder));
        var sample = MultiTwoStrokeConfiguration.Example();
        var a = config.SharedIntake ?? sample.SharedIntake!;
        var e = config.SharedExhaust ?? sample.SharedExhaust!;
        Write("A", a.Trunk, a.VolumeM3);
        Write("E", e.Primary, e.JunctionVolumeM3);
        populating = false;
        RenderTopology();
        void Write(string key, DuctGeometry d, double volume)
        {
            network[key + "Length"].Text = Text(d.LengthM * 1000);
            network[key + "Outer"].Text = Text(d.ReservoirDiameterM * 1000);
            network[key + "Inner"].Text = Text(d.ValveDiameterM * 1000);
            network[key + "Cells"].Text = Text(d.Cells);
            network[key + "Volume"].Text = Text(volume * 1000);
        }
    }

    private void RenderTopology()
    {
        foreach (var (key, box) in network) box.IsEnabled = (key[0] == 'A' ? sharedIntake : sharedExhaust).IsChecked == true;
        string order;
        try { order = string.Join(" → ", ReadPhases().FiringOrder()); }
        catch (ArgumentException) { order = "revisá los desfases"; }
        string row = string.Join(" · ", Enumerable.Range(1, (int)count.SelectedItem).Select(i => $"C{i}"));
        diagram.Text = (sharedIntake.IsChecked == true ? "Depósito ↔ tronco ↔ unión de admisión\n" : "Depósitos por cilindro\n") +
            $"↕ ramales y láminas\nCárteres separados · {row}\n↕ transfers\nCilindros · {row}\n↕ lumbreras\n" +
            (sharedExhaust.IsChecked == true ? "Primarios ↔ unión ↔ expansión común ↔ salida" : "Expansión propia y salida por cilindro") +
            $"\n\nOrden de encendido\n{order}";
    }

    private void Edited()
    {
        if (initializing || populating) return;
        dirty = true;
        curve = null;
        contrast = null;
        RenderTopology();
        Render();
        M2Status.Text = "Configuración editada. A se conserva.";
    }

    private async Task Execute<T>(Func<CancellationToken, IProgress<string>, T> work, Action<T> show)
    {
        if (operation is not null) return;
        using var token = new CancellationTokenSource();
        operation = token;
        SetBusy(true);
        var progress = new Progress<string>(s => { if (operation == token) M2Status.Text = s; });
        try { show(await Task.Run(() => work(token.Token, progress), token.Token)); }
        catch (OperationCanceledException) { M2Status.Text = "Cancelado. Se conservan los resultados anteriores."; }
        catch (Exception ex) when (Expected(ex)) { Message(ex); }
        finally { operation = null; SetBusy(false); Render(); }
    }

    private void SetBusy(bool active)
    {
        Commands.IsEnabled = Editor.IsEnabled = StudyCommands.IsEnabled = StudyEditor.IsEnabled = !active;
        M2Cancel.IsEnabled = active;
        Buttons();
    }

    private async void Run_Click(object sender, RoutedEventArgs e) => await RunEditor();
    internal async Task RunEditor()
    {
        MultiTwoStrokeConfiguration c;
        try { c = ReadEditor(); }
        catch (ArgumentException ex) { Message(ex); return; }
        var a = original;
        var old = baseline;
        await Execute((token, progress) =>
        {
            var update = new Relay<MultiTwoStrokeRun>(r => progress.Report($"{r.Rpm} RPM · {State(r.Status)}"));
            var b = TwoStrokeSimulator.Sweep(c, progress: update, cancellation: token, activity: progress);
            var solved = a is null ? null : a == c ? b : old ?? TwoStrokeSimulator.Sweep(a, cancellation: token, activity: progress);
            return (b, solved);
        }, result =>
        {
            curve = result.b;
            baseline = result.solved;
            contrast = null;
            dirty = true;
            M2Status.Text = $"Cálculo terminado · {curve.Runs.Count(r => r.Point is not null)}/{curve.Runs.Count} RPM periódicas.";
        });
    }

    private sealed class Relay<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }

    private void Cancel_Click(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        if (curve is null) return;
        original = curve.Configuration; baseline = curve; contrast = null; dirty = true;
        M2Status.Text = "Configuración conservada como A.";
        Render();
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        original = null; baseline = null; contrast = null; dirty = true;
        Render();
    }

    internal MultiTwoStrokeProject Snapshot() => new(ReadEditor(), original, curve, baseline, contrast, study);
    internal void Restore(MultiTwoStrokeProject project)
    {
        Populate(project.Current);
        original = project.Original; curve = project.Curve; baseline = project.Baseline; contrast = project.Contrast;
        RestoreStudy(project.Study);
        dirty = false;
        Render();
        M2Status.Text = "Proyecto 2T multicilíndrico abierto.";
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        string? file = OpenProject();
        if (file is null || !CanDiscard()) return;
        await LoadProjectAsync(file);
    }
    internal Task LoadProjectAsync(string file) => Execute((_, _) => TwoStrokeFiles.LoadMulti(file), Restore);
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        string? file = OpenProject();
        if (file is null || !CanDiscard()) return;
        FiringPhases layout;
        try { layout = ReadPhases(); }
        catch (ArgumentException ex) { Message(ex); return; }
        await Execute((_, _) => TwoStrokeFiles.Load(file).Current, c =>
        {
            var config = new MultiTwoStrokeConfiguration(c, layout);
            config.Validate();
            Restore(new(config));
            dirty = true;
            M2Status.Text = "Motor importado con admisión y escape independientes.";
        });
    }
    private string? OpenProject()
    {
        var dialog = new OpenFileDialog { Filter = "Proyecto 2T (*.json)|*.json" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var project = Snapshot();
            var dialog = new SaveFileDialog { Filter = "Proyecto 2T (*.json)|*.json", FileName = "motor-multicilindrico-2t.json" };
            if (dialog.ShowDialog(this) != true) return;
            TwoStrokeFiles.SaveMulti(dialog.FileName, project);
            dirty = false;
            M2Status.Text = "Proyecto 2T multicilíndrico guardado.";
        }
        catch (Exception ex) when (Expected(ex)) { Message(ex); }
    }

    private void Export(string name, Action<string> write)
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = name + ".csv" };
        if (dialog.ShowDialog(this) != true) return;
        try { write(dialog.FileName); M2Status.Text = "CSV exportado."; }
        catch (Exception ex) when (Expected(ex)) { Message(ex); }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if ((curve ?? baseline) is { } result) Export("2t-multicilindrico", path => TwoStrokeFiles.ExportMulti(path, result));
    }
    private void Trace_Click(object sender, RoutedEventArgs e)
    {
        if (CycleCurve is { } result && CycleRpm.SelectedItem is int rpm)
            Export("2t-multicilindrico-ciclo", path => TwoStrokeFiles.ExportMultiCycle(path, result, rpm));
    }
    private void Cylinders_Click(object sender, RoutedEventArgs e)
    {
        if (CycleCurve is { } result) Export("2t-cilindros", path => TwoStrokeFiles.ExportMultiCylinders(path, result));
    }
    private void ExportAB_Click(object sender, RoutedEventArgs e)
    {
        if (baseline is { } a && curve is { } b)
            Export("2t-multicilindrico-ab", path => TwoStrokeFiles.ExportMultiComparison(path, a, b, contrast));
    }

    private static bool Expected(Exception ex) => ex is ArgumentException or ArithmeticException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException;
    private void Message(Exception ex) => M2Status.Text = "No se completó. " + ex.Message;
}
