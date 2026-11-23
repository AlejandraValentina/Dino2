using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dino2.Core.GasDynamics;
using Dino2.Core.Multicylinder;
using Dino2.Core.Studies;
using Dino2.Core.Thermochemistry;
using Dino2.Storage;
using Microsoft.Win32;

namespace Dino2.Desktop;

public partial class MulticylinderWindow : Window
{
    private static readonly Brush[] CylinderColors = [Brushes.SteelBlue, Brushes.IndianRed, Brushes.SeaGreen, Brushes.DarkViolet];
    private static readonly EngineParameter[] Parameters = EngineParameter.All
        .Where(p => p.Key is not ("Lhv" or "LocalTolerance")).ToArray();
    private readonly Dictionary<string, TextBox> engine = [];
    private readonly Dictionary<string, TextBox> collector = [];
    private readonly ComboBox cylinderCount = new() { Name = "MultiCylinderCount" };
    private readonly ComboBox scheme = new();
    private readonly ComboBox burnLaw = new();
    private readonly CheckBox shared = new() { Content = "Colectores compartidos" };
    private readonly TextBox[] phases = [new(), new(), new()];
    private readonly TextBlock topology = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Func<bool>? confirmDiscard;
    private MultiConfiguration template = MultiConfiguration.Example(exterior: ExteriorBoundary.PressureReservoir);
    private MultiConfiguration? original;
    private MultiResult? currentResult, originalResult;
    private IReadOnlyList<MultiDifference>? differences;
    private CancellationTokenSource? cancellation;
    private bool populating, dirty, renderingCycle, initialized;

    public MulticylinderWindow() : this(null) { }
    internal MulticylinderWindow(Func<bool>? confirmDiscard)
    {
        this.confirmDiscard = confirmDiscard;
        InitializeComponent();
        DesktopLayout.Fit(this);
        BuildEditor();
        BuildStudyEditor();
        foreach (var grid in new[] { MultiRows, CylinderRows, DifferenceRows, StudyRows, StudyPointRows })
            DesktopLayout.FormatGrid(grid);
        CycleSource.ItemsSource = new[] { "Actual / B", "Original A" };
        CycleSource.SelectedIndex = 0;
        initialized = true;
        Populate(template);
        Render();
        Closing += (_, e) => e.Cancel = !CanCloseFromOwner();
    }

    internal bool CanCloseFromOwner()
    {
        if (cancellation is not null)
        {
            cancellation.Cancel();
            MultiStatusText.Text = "Cancelando el trabajo. Cerrá cuando finalice.";
            return false;
        }
        return ConfirmDiscard();
    }

    private bool ConfirmDiscard() => !dirty || (confirmDiscard?.Invoke() ?? DesktopLayout.ConfirmDiscard(this, "Multicilíndrico 4T"));

    private void BuildEditor()
    {
        StackPanel Tab(string name)
        {
            var panel = new StackPanel { Margin = new Thickness(9) };
            Editor.Items.Add(new TabItem { Header = name, Content = new ScrollViewer
            {
                Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            } });
            return panel;
        }
        var setup = Tab("Motor");
        cylinderCount.ItemsSource = new[] { 2, 3, 4 };
        RegisterName(cylinderCount.Name, cylinderCount);
        Label(setup, "Disposición en línea · cilindros", cylinderCount);
        for (int i = 0; i < phases.Length; i++)
        {
            phases[i].Name = "MultiPhase" + (i + 2);
            RegisterName(phases[i].Name, phases[i]);
            Label(setup, $"Cilindro {i + 2} · desfase de PMS [°]", phases[i]);
            phases[i].TextChanged += (_, _) => Changed();
        }
        var presets = new Button { Content = "Fases de ejemplo", Margin = new Thickness(0, 7, 0, 0) };
        presets.Click += (_, _) => ResetPhases();
        setup.Children.Add(presets);
        cylinderCount.SelectionChanged += (_, _) => { if (!populating) ResetPhases(); };
        scheme.ItemsSource = Enum.GetValues<DuctScheme>();
        Label(setup, "Reconstrucción en conductos", scheme);
        scheme.SelectionChanged += (_, _) => Changed();
        burnLaw.ItemsSource = new[] { BurnLaw.PrescribedWiebe, BurnLaw.ConditionScaledWiebe };
        Label(setup, "Ley de combustión", burnLaw);
        burnLaw.SelectionChanged += (_, _) => Changed();
        shared.Margin = new Thickness(0, 9, 0, 4);
        setup.Children.Add(shared);
        shared.Checked += (_, _) => Changed();
        shared.Unchecked += (_, _) => Changed();
        setup.Children.Add(topology);
        var common = Tab("Colectores");
        foreach (bool intake in new[] { true, false })
        {
            string side = intake ? "Admisión" : "Escape";
            string key = intake ? "Intake" : "Exhaust";
            Add(common, key + "TrunkLength", side + " · longitud común [mm]", collector);
            Add(common, key + "TrunkOuter", side + " · Ø en depósito [mm]", collector);
            Add(common, key + "TrunkInner", side + " · Ø en unión [mm]", collector);
            Add(common, key + "TrunkCells", side + " · celdas", collector);
            Add(common, key + "Volume", side + " · volumen de unión [L]", collector);
        }
        foreach (var group in Parameters.GroupBy(p => p.Group))
        {
            var panel = Tab(group.Key == "Conductos" ? "Ramales" : group.Key);
            foreach (var p in group) Add(panel, p.Key, p.Label, engine);
        }
        void Add(Panel panel, string key, string caption, Dictionary<string, TextBox> fields)
        {
            var box = new TextBox { Name = "MultiField_" + key };
            RegisterName(box.Name, box);
            fields.Add(key, box);
            Label(panel, caption, box);
            box.TextChanged += (_, _) => Changed();
        }
    }

    private static void Label(Panel panel, string caption, Control input)
    {
        panel.Children.Add(new Label { Content = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap },
            Target = input, Padding = new Thickness(0, 8, 0, 2) });
        System.Windows.Automation.AutomationProperties.SetName(input, caption);
        panel.Children.Add(input);
    }

    private void ResetPhases()
    {
        if (cylinderCount.SelectedItem is not int count) return;
        populating = true;
        SetPhases(FiringPhases.Inline(count));
        populating = false;
        Changed();
    }

    private void SetPhases(FiringPhases layout)
    {
        var values = layout.Values();
        for (int i = 0; i < phases.Length; i++)
        {
            bool inLayout = i + 1 < values.Length;
            phases[i].IsEnabled = inLayout;
            phases[i].Text = inLayout ? Format(values[i + 1]) : "";
        }
    }

    private static string Format(double value) => value.ToString("G17", CultureInfo.CurrentCulture);
    private static double Value(TextBox box, string label, bool whole = false)
    {
        if ((!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double d) &&
             !double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ||
            !double.IsFinite(d) || (whole && (d != Math.Truncate(d) || d is < int.MinValue or > int.MaxValue)))
            throw new ArgumentException($"Revisá el valor de {label}.");
        return d;
    }

    private FiringPhases ReadPhases()
    {
        int count = (int)cylinderCount.SelectedItem;
        double Phase(int i) => Value(phases[i - 2], $"desfase del cilindro {i}");
        return new(Phase(2), count >= 3 ? Phase(3) : null, count == 4 ? Phase(4) : null);
    }

    private MultiConfiguration ReadEditor()
    {
        var c = template.Cylinder;
        foreach (var p in Parameters)
        {
            if ((p.Group == "Comb. v2" && burnLaw.SelectedItem is not BurnLaw.ConditionScaledWiebe) ||
                (p.Key == "DurationDeg" && burnLaw.SelectedItem is BurnLaw.ConditionScaledWiebe)) continue;
            double value = Value(engine[p.Key], p.Label, p.Integer);
            var previous = p.Get(c);
            if (value != previous * p.Scale) c = p.Set(c, value / p.Scale);
        }
        var law = (BurnLaw)burnLaw.SelectedItem;
        var evidence = c.Thermochemistry!;
        if (c != template.Cylinder || evidence.BurnLaw != law)
        {
            ParameterEvidence Edited(ParameterEvidence p) => p.Status != EvidenceStatus.Calibrated ? p :
                p with { Status = EvidenceStatus.Estimated, Source = "Configuración editada; " + p.Source, CalibrationDataset = "" };
            evidence = evidence with { CombustionEvidence = Edited(evidence.CombustionEvidence),
                HeatTransferEvidence = Edited(evidence.HeatTransferEvidence), FrictionEvidence = Edited(evidence.FrictionEvidence) };
        }
        c = c with { Ducts = c.Ducts! with { Scheme = (DuctScheme)scheme.SelectedItem },
            Thermochemistry = evidence with { BurnLaw = law }, Combustion = law == BurnLaw.ConditionScaledWiebe
                ? c.Combustion with { DurationDeg = null } : c.Combustion };
        ManifoldSide Side(string key)
        {
            var existing = template.Manifolds ?? MultiConfiguration.Example().Manifolds!;
            var side = key == "Intake" ? existing.Intake : existing.Exhaust;
            double D(string suffix, bool whole = false) => Value(collector[key + suffix], key + " " + suffix, whole);
            double Si(string suffix, double old)
            {
                double value = D(suffix);
                return value == old * 1000 ? old : value / 1000;
            }
            return new(new(Si("TrunkLength", side.Trunk.LengthM), Si("TrunkOuter", side.Trunk.ReservoirDiameterM),
                Si("TrunkInner", side.Trunk.ValveDiameterM), (int)D("TrunkCells", true)), Si("Volume", side.VolumeM3));
        }
        var result = new MultiConfiguration(c, ReadPhases(),
            shared.IsChecked == true ? new(Side("Intake"), Side("Exhaust")) : null);
        result.Validate();
        return result;
    }

    private void Populate(MultiConfiguration config)
    {
        populating = true;
        template = config;
        cylinderCount.SelectedItem = config.Phases.Count;
        SetPhases(config.Phases);
        scheme.SelectedItem = config.Cylinder.Ducts!.Scheme;
        burnLaw.SelectedItem = config.Cylinder.Thermochemistry!.BurnLaw;
        shared.IsChecked = config.Manifolds is not null;
        foreach (var p in Parameters) engine[p.Key].Text = Format(p.Get(config.Cylinder) * p.Scale);
        var network = config.Manifolds ?? MultiConfiguration.Example().Manifolds!;
        Side("Intake", network.Intake);
        Side("Exhaust", network.Exhaust);
        populating = false;
        UpdateTopology();
        void Side(string key, ManifoldSide side)
        {
            collector[key + "TrunkLength"].Text = Format(side.Trunk.LengthM * 1000);
            collector[key + "TrunkOuter"].Text = Format(side.Trunk.ReservoirDiameterM * 1000);
            collector[key + "TrunkInner"].Text = Format(side.Trunk.ValveDiameterM * 1000);
            collector[key + "TrunkCells"].Text = Format(side.Trunk.Cells);
            collector[key + "Volume"].Text = Format(side.VolumeM3 * 1000);
        }
    }

    private void UpdateTopology()
    {
        foreach (var field in collector.Values) field.IsEnabled = shared.IsChecked == true;
        foreach (var p in Parameters.Where(p => p.Group == "Comb. v2"))
            engine[p.Key].IsEnabled = burnLaw.SelectedItem is BurnLaw.ConditionScaledWiebe;
        engine["DurationDeg"].IsEnabled = burnLaw.SelectedItem is not BurnLaw.ConditionScaledWiebe;
        string order;
        try { order = string.Join(" → ", ReadPhases().FiringOrder()); }
        catch (ArgumentException) { order = "revisar desfases"; }
        string cylinders = string.Join("  ", Enumerable.Range(1, (int)cylinderCount.SelectedItem).Select(n => $"C{n}"));
        topology.Text = (shared.IsChecked == true
            ? $"Depósito admisión ↔ tronco ↔ unión\n↕ ramales individuales\n{cylinders}\n↕ ramales individuales\nUnión ↔ tronco ↔ depósito escape"
            : $"Depósitos separados ↔ ramales ↔ {cylinders}") + $"\n\nOrden de encendido\n{order}";
    }

    private void Changed()
    {
        if (populating || !initialized) return;
        dirty = true;
        currentResult = null;
        differences = null;
        UpdateTopology();
        Render();
        MultiStatusText.Text = "Configuración editada. A se conserva.";
    }

    private async Task ExecuteAsync<T>(Func<CancellationToken, IProgress<string>, T> work, Action<T> show)
    {
        if (cancellation is not null) return;
        using var source = new CancellationTokenSource();
        cancellation = source;
        SetBusy(true);
        var progress = new Progress<string>(message => { if (cancellation == source) MultiStatusText.Text = message; });
        try
        {
            var result = await Task.Run(() => work(source.Token, progress), source.Token);
            show(result);
        }
        catch (OperationCanceledException) { MultiStatusText.Text = "Cancelado. Se conservan los resultados anteriores."; }
        catch (Exception ex) when (IsInputError(ex)) { Message(ex); }
        finally
        {
            cancellation = null;
            SetBusy(false);
            Render();
        }
    }

    private void SetBusy(bool busy)
    {
        Editor.IsEnabled = Commands.IsEnabled = StudyCommands.IsEnabled = StudyEditor.IsEnabled = !busy;
        MultiCancel.IsEnabled = busy;
        RefreshButtons();
    }

    private async void Run_Click(object sender, RoutedEventArgs e) => await RunEditor();
    internal async Task RunEditor()
    {
        MultiConfiguration config;
        try { config = ReadEditor(); }
        catch (ArgumentException ex) { Message(ex); return; }
        var a = original;
        var old = originalResult;
        await ExecuteAsync((token, progress) =>
        {
            var b = MultiSimulator.Run(config, token, progress);
            var solvedA = a is null ? null : a == config ? b : old ?? MultiSimulator.Run(a, token, progress);
            return (b, solvedA);
        }, result =>
        {
            currentResult = result.b;
            originalResult = result.solvedA;
            differences = null;
            dirty = true;
            MultiStatusText.Text = $"Cálculo completado · {result.b.Points.Count(p => p.Status == MultiStatus.Converged)}/{result.b.Points.Count} RPM periódicas · {result.b.ElapsedMilliseconds / 1000:F2} s";
            if (config.Cylinder.Ducts?.BoundaryNotice() is { } notice) MultiStatusText.Text += " · " + notice;
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => cancellation?.Cancel();

    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        if (currentResult is null) return;
        original = currentResult.Configuration;
        originalResult = currentResult;
        differences = null;
        dirty = true;
        Render();
        MultiStatusText.Text = "Configuración y curva guardadas como A.";
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        original = null;
        originalResult = null;
        differences = null;
        dirty = true;
        Render();
    }

    internal MultiProject Snapshot() => new(ReadEditor(), original, currentResult, originalResult, savedStudy, differences);

    internal void Restore(MultiProject project)
    {
        Populate(project.Current);
        original = project.Original;
        currentResult = project.CurrentResult;
        originalResult = project.OriginalResult;
        differences = project.Differences;
        RestoreStudy(project.Study);
        dirty = false;
        Render();
        MultiStatusText.Text = "Proyecto multicilíndrico abierto.";
        if (project.Current.Cylinder.Ducts?.BoundaryNotice() is { } notice) MultiStatusText.Text += " " + notice;
        if (currentResult is { } loaded)
        {
            MultiStatusText.Text += " " + Dino2.Core.ResultProvenance.Describe(loaded.Provenance);
            if (loaded.Provenance?.NumericalRevision != project.Current.Cylinder.ModelIdentifier + "-network-recovery-v1")
                MultiStatusText.Text += " Recalculá antes de interpretar la curva histórica.";
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Proyecto multicilíndrico 4T (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true || !ConfirmDiscard()) return;
        await LoadProjectAsync(dialog.FileName);
    }

    internal Task LoadProjectAsync(string path) => ExecuteAsync((_, _) => MultiFiles.Load(path), Restore);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var project = Snapshot();
            var dialog = new SaveFileDialog { Filter = "Proyecto multicilíndrico 4T (*.json)|*.json", FileName = "motor-multicilindrico.json" };
            if (dialog.ShowDialog(this) != true) return;
            MultiFiles.Save(dialog.FileName, project);
            dirty = false;
            MultiStatusText.Text = "Proyecto multicilíndrico guardado.";
        }
        catch (Exception ex) when (IsInputError(ex)) { Message(ex); }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Proyecto 4T monocilíndrico (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true || !ConfirmDiscard()) return;
        try
        {
            var mono = FourStrokeFiles.Load(dialog.FileName).Current;
            if (mono.Ducts is null) throw new ArgumentException("La importación requiere un 4T termoquímico con ramales 1D.");
            Restore(new(MultiConfiguration.Example((int)cylinderCount.SelectedItem) with { Cylinder = mono }));
            dirty = true;
        }
        catch (Exception ex) when (IsInputError(ex)) { Message(ex); }
    }

    private void DoExport(string name, Action<string> export)
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = name + ".csv" };
        if (dialog.ShowDialog(this) != true) return;
        try { export(dialog.FileName); MultiStatusText.Text = "CSV exportado."; }
        catch (Exception ex) when (IsInputError(ex)) { Message(ex); }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var result = currentResult ?? originalResult;
        if (result is not null) DoExport("4t-multicilindrico", path => MultiFiles.Export(path, result));
    }

    private void ExportTrace_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCurve is { } result && MultiRpm.SelectedItem is int rpm)
            DoExport("4t-multicilindrico-ciclo", path => MultiFiles.ExportTrace(path, result, rpm));
    }

    private void ExportCylinders_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCurve is { } result)
            DoExport("4t-multicilindrico-cilindros", path => MultiFiles.ExportCylinders(path, result));
    }

    private void ExportComparison_Click(object sender, RoutedEventArgs e)
    {
        if (originalResult is not null && currentResult is not null)
            DoExport("4t-multicilindrico-ab", path => MultiFiles.ExportComparison(path, originalResult, currentResult, ComparisonRows()));
    }

    private static bool IsInputError(Exception ex) =>
        ex is ArgumentException or ArithmeticException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException;
    private void Message(Exception ex) => MultiStatusText.Text = "No se completó. " + ex.Message;
}
