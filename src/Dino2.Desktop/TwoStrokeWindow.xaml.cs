using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Studies;
using Dino2.Core.TwoStroke;
using Dino2.Storage;
using Microsoft.Win32;

namespace Dino2.Desktop;

public partial class TwoStrokeWindow : EngineDocument
{
    private TwoStrokeConfiguration template = TwoStrokeConfiguration.RecoveryExample;
    private TwoStrokeConfiguration? original;
    private TwoStrokeSweep? curve, baseline;
    private TwoStrokeComparisonEvidence? comparisonEvidence;
    private CancellationTokenSource? operation;
    private bool populating, dirty;
    private readonly Dictionary<Field, TextBox> inputs = [];
    private CheckBox equilibrium = null!;
    private ComboBox scheme = null!;
    private readonly TwoStrokeStudyPanel studies;
    private static readonly Brush Blue = Brushes.SteelBlue, Orange = Brushes.DarkOrange, Green = Brushes.SeaGreen;

    public TwoStrokeWindow()
    {
        InitializeComponent();
        DesktopLayout.Fit(this);
        DesktopLayout.Shortcut(this, System.Windows.Input.Key.F5, System.Windows.Input.ModifierKeys.None, CalculateButton);
        DesktopLayout.Shortcut(this, System.Windows.Input.Key.O, System.Windows.Input.ModifierKeys.Control, OpenTwoStroke);
        DesktopLayout.Shortcut(this, System.Windows.Input.Key.S, System.Windows.Input.ModifierKeys.Control, SaveTwoStroke);
        DesktopLayout.Shortcut(this, System.Windows.Input.Key.E, System.Windows.Input.ModifierKeys.Control, ExportButton);
        DesktopLayout.Shortcut(this, System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None, CancelButton);
        DesktopLayout.FormatGrid(ResultGrid);
        DesktopLayout.FormatGrid(ComparisonGrid);
        BuildEditor();
        Populate(template);
        studies = new(this);
        StudyHost.Content = studies;
        Closing += (_, e) => e.Cancel = !CanCloseDocument();
    }

    internal override bool CanCloseDocument()
    {
        if (operation is not null)
        {
            operation.Cancel();
            ShowStatus("Cancelando la operación 2T antes de cerrar.");
            return false;
        }
        return CanDiscard() && studies.ConfirmDiscard();
    }

    private bool CanDiscard() => !dirty ||
        MessageBox.Show(DialogOwner, "¿Descartar los cambios del proyecto 2T?", "Dino2", MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    internal TwoStrokeConfiguration CurrentConfiguration()
    {
        var configuration = template;
        foreach (var (field, input) in inputs)
        {
            double value = Parse(input.Text, field.Label);
            if (value != field.Read(configuration)) configuration = field.Set(configuration, value);
        }

        configuration = configuration with
        {
            Combustion = configuration.Combustion with { ProductEquilibrium = equilibrium.IsChecked == true },
            Numerics = configuration.Numerics with { Scheme = (Core.GasDynamics.DuctScheme)scheme.SelectedItem }
        };
        configuration.Validate();
        return configuration;
    }

    internal static double Parse(string input, string label)
    {
        if ((double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
            double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            double.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
            double.IsFinite(value)) return value;
        throw new ArgumentException($"Revisá el valor de {label}.");
    }

    internal void Apply(TwoStrokeVariant variant, TwoStrokeSweep? result, TwoStrokeSweep? studyBase)
    {
        if (original is null && studyBase is not null)
        {
            original = studyBase.Configuration;
            baseline = studyBase;
        }
        curve = result;
        Populate(variant.Configuration);
        dirty = true;
        Analysis.SelectedIndex = 0;
        ShowStatus("Variante aplicada. Simulá para obtener la traza angular.");
    }

    private void Populate(TwoStrokeConfiguration configuration)
    {
        populating = true;
        template = configuration;
        foreach (var (field, input) in inputs) input.Text = field.Read(configuration).ToString("R", CultureInfo.CurrentCulture);
        equilibrium.IsChecked = configuration.Combustion.ProductEquilibrium;
        scheme.SelectedItem = configuration.Numerics.Scheme;
        string Contour(PistonPort port) => port.WidthProfile is null
            ? "rectangular constante"
            : $"CSV {port.WidthProfileSource} · {port.WidthProfile.Count} puntos · SHA-256 {port.WidthProfileSha256}";
        string MapInfo(DischargeMap? forward, DischargeMap? reverse) => forward is null && reverse is null
            ? "Cd constante"
            : string.Join(" / ", new[] { forward, reverse }.Where(m => m is not null)
                .Select(m => $"mapa {m!.Metadata.Direction} · {m.Metadata.CompatibleModel} · {m.Points.Count} puntos · f={m.Points.Min(p => p.Opening):G3}–{m.Points.Max(p => p.Opening):G3}, r={m.Points.Min(p => p.PressureRatio):G3}–{m.Points.Max(p => p.PressureRatio):G3} · {m.Metadata.Source} · SHA {m.Metadata.DataSha256[..Math.Min(12, m.Metadata.DataSha256.Length)]}"));
        PortContourInfo.Text = $"Transfers: {Contour(configuration.Transfer)} · {MapInfo(configuration.Transfer.DischargeMap, configuration.Transfer.ReverseDischargeMap)}. " +
            $"Escape: {Contour(configuration.Exhaust)} · {MapInfo(configuration.Exhaust.DischargeMap, configuration.Exhaust.ReverseDischargeMap)}. " +
            $"Láminas: {MapInfo(configuration.Reed.DischargeMap, configuration.Reed.ReverseDischargeMap)}. Anchos múltiples se representan como agregado.";
        populating = false;
        ClearResolution();
        Render();
    }

    private void ImportTransferContour_Click(object sender, RoutedEventArgs e) => ImportPortContour(true);
    private void ImportExhaustContour_Click(object sender, RoutedEventArgs e) => ImportPortContour(false);

    private void ImportTransferMap_Click(object sender, RoutedEventArgs e) => ImportPortMap(true);
    private void ImportExhaustMap_Click(object sender, RoutedEventArgs e) => ImportPortMap(false);
    private void ImportReedMap_Click(object sender, RoutedEventArgs e) => ImportReedMap();
    private void ViewMaps_Click(object sender, RoutedEventArgs e) => ViewDischargeMaps();

    internal void ViewDischargeMaps()
    {
        var config = CurrentConfiguration();
        var maps = new List<(string Component, DischargeMap Map)>();
        if (config.Transfer.DischargeMap is { } transfer) maps.Add(("2T · transfer agregado", transfer));
        if (config.Transfer.ReverseDischargeMap is { } transferReverse) maps.Add(("2T · transfer inverso", transferReverse));
        if (config.Exhaust.DischargeMap is { } exhaust) maps.Add(("2T · escape 1D", exhaust));
        if (config.Exhaust.ReverseDischargeMap is { } exhaustReverse) maps.Add(("2T · escape inverso", exhaustReverse));
        if (config.Reed.DischargeMap is { } reed) maps.Add(("2T · láminas", reed));
        if (config.Reed.ReverseDischargeMap is { } reedReverse) maps.Add(("2T · láminas inverso", reedReverse));
        if (maps.Count == 0) { ShowStatus("No hay mapas Cd cargados."); return; }
        new DischargeMapWindow(maps) { Owner = DialogOwner }.ShowDialog();
    }

    private void ImportReedMap()
    {
        Guard(() =>
        {
            string? path = Choose(false, "Mapa Cd CSV (*.csv)|*.csv", "mapa-cd-laminas.csv");
            if (path is null) return;
            ApplyReedMap(DischargeMapFile.Load(path));
        });
    }

    internal void ApplyReedMap(ImportedDischargeMap imported)
    {
        var configuration = CurrentConfiguration();
        var map = imported.Map;
        if (!string.Equals(map.Metadata.CompatibleModel, "ports-v4-equivalent-capacity-v1", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(map.Metadata.ReferenceArea, "reed_open_area", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(map.Metadata.PressureConvention, "upstream_stagnation_to_downstream_static", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El mapa de láminas requiere ports-v4-equivalent-capacity-v1, reed_open_area y remanso→estática.");
        if (configuration.Numerics.Exterior != Dino2.Core.GasDynamics.ExteriorBoundary.PressureReservoir)
            throw new InvalidDataException("El mapa de láminas requiere el contorno de presión ports-v4.");
        var reed = map.Metadata.Direction.Equals("reverse", StringComparison.OrdinalIgnoreCase)
            ? configuration.Reed with { ReverseDischargeMap = map }
            : configuration.Reed with { DischargeMap = map };
        template = configuration with { Reed = reed };
        template.Validate();
        Populate(template);
        dirty = true;
        ShowStatus($"Mapa Cd de láminas importado: {imported.Source}, {map.Metadata.Direction}, {map.Points.Count} puntos, SHA-256 {imported.Sha256[..12]}…");
    }

    private void ImportPortMap(bool transfer)
    {
        Guard(() =>
        {
            string? path = Choose(false, "Mapa Cd CSV (*.csv)|*.csv", "mapa-cd.csv");
            if (path is null) return;
            var imported = DischargeMapFile.Load(path);
            ApplyPortMap(transfer, imported);
        });
    }

    internal void ApplyPortMap(bool transfer, ImportedDischargeMap imported)
    {
        var configuration = CurrentConfiguration();
        var port = transfer ? configuration.Transfer : configuration.Exhaust;
        var map = imported.Map;
        string compatible = transfer ? "two-stroke-0d-isentropic-nozzle" : "ports-v4-equivalent-capacity-v1";
        if (!string.Equals(map.Metadata.CompatibleModel, compatible, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(map.Metadata.ReferenceArea, "port_open_area", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(map.Metadata.PressureConvention, "upstream_stagnation_to_downstream_static", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Contrato incompatible: se requiere {compatible}, port_open_area y remanso→estática.");
        if (!transfer && configuration.Numerics.Exterior != Dino2.Core.GasDynamics.ExteriorBoundary.PressureReservoir)
            throw new InvalidDataException("El mapa de escape 2T requiere el contorno de presión ports-v4; seleccione ese modelo de interfaz antes de importar.");
        var updated = map.Metadata.Direction.Equals("reverse", StringComparison.OrdinalIgnoreCase)
            ? port with { ReverseDischargeMap = map }
            : port with { DischargeMap = map };
        template = transfer ? configuration with { Transfer = updated } : configuration with { Exhaust = updated };
        template.Validate();
        Populate(template);
        dirty = true;
        ShowStatus($"Mapa Cd {(transfer ? "de transfer 0D" : "de escape ports-v4 1D")} importado: {imported.Source}, {map.Metadata.Direction}, {map.Points.Count} puntos, SHA-256 {imported.Sha256[..12]}…");
    }

    private void ClearMaps_Click(object sender, RoutedEventArgs e)
    {
        Guard(() =>
        {
            ClearPortMaps();
        });
    }

    internal void ClearPortMaps()
    {
        var configuration = CurrentConfiguration();
        template = configuration with
        {
            Transfer = configuration.Transfer with { DischargeMap = null, ReverseDischargeMap = null },
            Exhaust = configuration.Exhaust with { DischargeMap = null, ReverseDischargeMap = null },
            Reed = configuration.Reed with { DischargeMap = null, ReverseDischargeMap = null }
        };
        Populate(template);
        dirty = true;
        ShowStatus("Se usa Cd constante en transfer y escape.");
    }

    private void ImportPortContour(bool transfer)
    {
        Guard(() =>
        {
            var configuration = CurrentConfiguration();
            var port = transfer ? configuration.Transfer : configuration.Exhaust;
            string? path = Choose(false, "Contorno de lumbrera CSV (*.csv)|*.csv", "contorno-lumbrera.csv");
            if (path is null) return;
            var imported = PistonPortWidthProfileFile.Load(path, port.HeightM, port.WidthM);
            var updated = port with { WidthProfile = imported.Points, WidthProfileSource = imported.Source,
                WidthProfileSha256 = imported.Sha256 };
            template = transfer ? configuration with { Transfer = updated } : configuration with { Exhaust = updated };
            template.Validate();
            Populate(template);
            dirty = true;
            ShowStatus($"Contorno {(transfer ? "de transfer" : "de escape")} importado: {imported.Source}, SHA-256 {imported.Sha256[..12]}… · la curva conserva CD constante.");
        });
    }

    private void Edited()
    {
        if (populating) return;
        dirty = true;
        curve = null;
        ClearResolution();
        Render();
        ShowStatus("Configuración modificada. Simulá para actualizar la curva.");
    }

    private void Example_Click(object sender, RoutedEventArgs e)
    {
        if (!CanDiscard()) return;
        original = null;
        baseline = curve = null;
        Populate(TwoStrokeConfiguration.RecoveryExample);
        dirty = false;
        ShowStatus("Ejemplo 2T cargado.");
    }

    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        if (curve is null) return;
        original = curve.Configuration;
        baseline = curve;
        ClearResolution();
        dirty = true;
        Render();
        ShowStatus("Curva original A conservada.");
    }

    private void RemoveOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (original is null) return;
        original = null;
        baseline = null;
        ClearResolution();
        dirty = true;
        Render();
        ShowStatus("Se quitó la referencia A del proyecto 2T.");
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => operation?.Cancel();

    private async void Calculate_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var config = CurrentConfiguration();
        var stamp = Dino2.Core.ResultProvenance.Capture(TwoStrokeConfiguration.ModelId, config.Revision);
        var pending = new List<TwoStrokeRun>();
        var clock = Stopwatch.StartNew();
        await Execute("Calculando curva 2T…", async token =>
        {
            var progress = new Progress<TwoStrokeRun>(run =>
            {
                if (operation?.Token != token) return;
                pending.Add(run);
                curve = new(config, pending.ToArray(), config.Revision) { Provenance = stamp };
                Render();
                ShowStatus($"{run.Rpm} RPM · {StateLabel(run.Status)} · {pending.Count}/{config.Speeds.Values().Count()}");
            });
            try
            {
                ClearResolution();
                var saved = original;
                if (saved is not null && saved != config && !CurrentRevision(baseline))
                {
                    ShowStatus("Calculando la curva original A…");
                    baseline = await Task.Run(() => TwoStrokeSimulator.Sweep(saved, cancellation: token));
                    dirty = true;
                    Render();
                }
                var result = await Task.Run(() => TwoStrokeSimulator.Sweep(config, progress: progress, cancellation: token));
                if (saved == config) baseline = result;
                curve = result;
                template = config;
                dirty = true;
                Render();
                ShowStatus($"Curva 2T: {result.Runs.Count(r => r.Point is not null)}/{result.Runs.Count} regímenes periódicos · {clock.Elapsed.TotalSeconds:F2} s.");
                if (config.Numerics.BoundaryNotice() is { } notice) Status.Text += " " + notice;
            }
            catch (OperationCanceledException)
            {
                if (pending.Count > 0) { curve = new(config, pending.ToArray(), config.Revision) { Provenance = stamp }; dirty = true; }
                Render();
                ShowStatus($"Cálculo cancelado. {pending.Count} regímenes procesados.");
            }
        });
    });

    private async void VerifyAB_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (ComparisonRpm.SelectedItem is not int rpm || original is null || curve is null ||
            !CurrentRevision(baseline) || !CurrentRevision(curve)) return;
        var a = original;
        var b = curve.Configuration;
        var pa = baseline!.Runs.Single(r => r.Rpm == rpm).Point!;
        var pb = curve.Runs.Single(r => r.Rpm == rpm).Point!;
        await Execute("Verificando malla A/B…", async token =>
        {
            var progress = new Progress<string>(ShowStatus);
            var result = await Task.Run(() => TwoStrokeComparison.Verify(a, b, pa, pb, token, progress));
            comparisonEvidence = new(rpm, result);
            dirty = true;
            RenderResolution();
            ShowStatus("Contraste A/B terminado.");
        });
    });

    private void ClearResolution()
    {
        comparisonEvidence = null;
        Resolution.Text = "";
    }

    private void RenderResolution()
    {
        if (comparisonEvidence is not { Rpm: var rpm, Check: var result }) return;
        string revision = result.NumericalRevision == curve?.Configuration.Revision ? "" : " · evidencia histórica o de otra revisión";
        Resolution.Text = $"{rpm} RPM · {StudyLabel(result.Verdict)} · " +
            string.Join(" / ", result.Levels.Select(l => $"×{l.Scale} {F(l.Advantage)}")) +
            $" N·m · banda observada {F(result.ObservedBand)} N·m{revision} " +
            string.Join(" ", result.Levels.Select(l => l.Error).Where(x => x is not null).Distinct());
    }

    internal async Task Execute(string status, Func<CancellationToken, Task> work)
    {
        if (operation is not null) return;
        using var active = new CancellationTokenSource();
        operation = active;
        Commands.IsEnabled = Editor.IsEnabled = ComparisonControls.IsEnabled = false;
        studies.SetBusy(true);
        CancelButton.IsEnabled = true;
        ShowStatus(status);
        try { await work(active.Token); }
        catch (OperationCanceledException) when (active.IsCancellationRequested) { ShowStatus("Operación cancelada."); }
        finally
        {
            operation = null;
            Commands.IsEnabled = Editor.IsEnabled = ComparisonControls.IsEnabled = true;
            studies.SetBusy(false);
            CancelButton.IsEnabled = false;
            Render();
        }
    }

    internal async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArithmeticException)
        { ShowStatus(ex.Message); }
    }

    internal void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArithmeticException)
        { ShowStatus(ex.Message); }
    }

    internal void ShowStatus(string message) => Status.Text = message;
    internal TwoStrokeConfiguration StudyBase(bool useOriginal) =>
        useOriginal ? original ?? throw new ArgumentException("Conservá primero la configuración A.") : CurrentConfiguration();

    private async void Open_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (!CanDiscard()) return;
        string? path = Choose(false, "Proyecto Dino2 2T (*.json)|*.json", "motor-2t.dino2.json");
        if (path is null) return;
        await LoadProjectAsync(path);
    });

    internal Task LoadProjectAsync(string path) => Execute("Abriendo proyecto 2T…", async token =>
    {
        var project = await Task.Run(() => TwoStrokeFiles.Load(path), token);
        token.ThrowIfCancellationRequested();
            RestoreProject(project);
    });

    internal void RestoreProject(TwoStrokeProject project)
    {
        original = project.Original;
        baseline = project.OriginalResult;
        curve = project.CurrentResult;
        Populate(project.Current);
        comparisonEvidence = project.Resolution;
        RenderResolution();
        dirty = false;
        ShowStatus("Proyecto 2T recuperado." + (curve is null ? "" : " " + Dino2.Core.ResultProvenance.Describe(curve.Provenance)));
        if (project.Current.Numerics.BoundaryNotice() is { } notice) Status.Text += " " + notice;
        if ((curve is not null && !CurrentRevision(curve)) || (baseline is not null && !CurrentRevision(baseline)))
            Status.Text += " Resultado de otra revisión. Recalculá antes de interpretarlo.";
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var current = CurrentConfiguration();
        string? path = Choose(true, "Proyecto Dino2 2T (*.json)|*.json", "motor-2t.dino2.json");
        if (path is null) return;
        TwoStrokeFiles.Save(path, new(current, original, curve?.Configuration == current ? curve : null, baseline, comparisonEvidence));
        dirty = false;
        ShowStatus("Proyecto 2T guardado.");
    });

    private void Export_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (curve is null) return;
        string? path = Choose(true, "CSV (*.csv)|*.csv", "curva-2t.csv");
        if (path is null) return;
        TwoStrokeFiles.Export(path, curve);
        if (baseline is not null && CurrentRevision(baseline) && CurrentRevision(curve))
        {
            TwoStrokeFiles.ExportComparison(Path.ChangeExtension(path, ".ab.csv"), baseline, curve);
            if (comparisonEvidence is not null) TwoStrokeFiles.ExportComparisonResolution(Path.ChangeExtension(path, ".ab-resolution.csv"), comparisonEvidence);
        }
        ShowStatus("CSV 2T exportado.");
    });

    private void ExportTrace_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (TracePoint() is not { } point) return;
        string? path = Choose(true, "CSV (*.csv)|*.csv", "ciclo-2t.csv");
        if (path is null) return;
        TwoStrokeFiles.ExportTrace(path, point, curve?.Provenance);
        ShowStatus("Traza 2T exportada.");
    });

    internal string? Choose(bool save, string filter, string name)
    {
        FileDialog dialog = save ? new SaveFileDialog() : new OpenFileDialog();
        dialog.Filter = filter;
        if (save) dialog.FileName = name;
        return dialog.ShowDialog(DialogOwner) == true ? dialog.FileName : null;
    }

    private void Render()
    {
        KeepButton.IsEnabled = ExportButton.IsEnabled = curve is not null;
        RemoveOriginalButton.IsEnabled = original is not null;
        RenderCurves(PowerPlot, TorquePlot, baseline, curve);
        ResultGrid.ItemsSource = curve?.Runs.Select(r => Describe(r)).ToArray();
        bool historical = baseline is not null && curve is not null && (!CurrentRevision(baseline) || !CurrentRevision(curve));
        var pairs = Points(historical ? null : baseline).Join(Points(curve), a => a.Rpm, b => b.Rpm, (a, b) => new
        {
            RPM = a.Rpm, A_W = a.ShaftPowerW, B_W = b.ShaftPowerW, Delta_W = b.ShaftPowerW - a.ShaftPowerW,
            A_Nm = a.ShaftTorqueNm, B_Nm = b.ShaftTorqueNm, Delta_Nm = b.ShaftTorqueNm - a.ShaftTorqueNm,
            Delta_atrapada_mg = (b.FreshTrappedKg - a.FreshTrappedKg) * 1e6
        }).ToArray();
        ComparisonNotice.Text = historical
            ? "Curvas históricas. Calculá A y B con la revisión actual para comparar y verificar la malla."
            : "Diferencias B − A en regímenes comunes. Resolución de par y potencia al mismo régimen.";
        if ((baseline?.Configuration.Numerics.BoundaryNotice() ?? curve?.Configuration.Numerics.BoundaryNotice()) is { } notice)
            ComparisonNotice.Text += " " + notice;
        ComparisonGrid.ItemsSource = pairs;
        FillRpm(ComparisonRpm, pairs.Select(p => p.RPM));
        FillRpm(TraceRpm, Points(curve).Where(p => p.Trace.Count > 0).Select(p => p.Rpm));
        TwoStrokeConfiguration cfg;
        try { cfg = CurrentConfiguration(); }
        catch (ArgumentException)
        {
            GeometrySummary.Text = "Revisá los campos del motor para actualizar la geometría.";
            RenderTrace();
            return;
        }
        GeometrySummary.Text = FormattableString.CurrentCulture($"{cfg.Geometry.DisplacementM3 * 1e6:F1} cm³ · Compresión geométrica {cfg.Geometry.CompressionRatio:G4} · Atrapada {cfg.TrappedCompressionRatio:F2} · Primaria {cfg.PrimaryCompressionRatio:F2}\nEscape abre {cfg.Exhaust.Opening(cfg.Geometry):F1}° · Transfers {cfg.Transfer.Opening(cfg.Geometry):F1}° después de PMS.");
        if (curve is not null)
            GeometrySummary.Text += "\nPeriodicidad y resolución espacial son comprobaciones distintas. Curva absoluta sin contraste de malla.\n" +
                Dino2.Core.ResultProvenance.Describe(curve.Provenance);
        RenderTrace();
    }

    internal static bool CurrentRevision(TwoStrokeSweep? result) => result is not null &&
        result.NumericalRevision == result.Configuration.Revision && result.Provenance?.NumericalRevision == result.Configuration.Revision;

    internal static object Describe(TwoStrokeRun run)
    {
        var p = run.Point;
        return new
        {
            RPM = run.Rpm, Estado = StateLabel(run.Status), Indicada_neta_W = p?.IndicatedPowerW, Cigüeñal_W = p?.ShaftPowerW,
            Par_Nm = p?.ShaftTorqueNm, IMEP_bar = p?.ImepPa / 1e5, BMEP_bar = p?.BmepPa / 1e5,
            BSFC_g_kWh = p?.BsfcGPerKwh, Entrega = p?.DeliveryRatio, Atrapamiento = p?.TrappingEfficiency,
            Barrido = p?.ScavengingEfficiency, Carga = p?.ChargingEfficiency, Fresca_mg = p?.FreshTrappedKg * 1e6,
            Residual_mg = p?.ResidualMassKg * 1e6, Cortocircuito_bruto_mg = p?.FreshExhaustOutKg * 1e6,
            Retorno_escape_mg = p?.FreshExhaustReturnKg * 1e6, Pmáx_bar = p?.PeakPressurePa / 1e5,
            Tmáx_K = p?.PeakTemperatureK, Ciclos = p?.Cycles, Error_periódico = p?.PeriodicError,
            Balance_J = p?.Balance.EnergyJ, Cortocircuito_neto_mg = p?.NetFreshExhaustKg * 1e6,
            Atrapada_por_entrega = p?.TrappingPerTransferDelivery, Ciclos_completados = run.Convergence?.Cycles,
            Residuo_final = run.Convergence?.Residual, Componente_dominante = run.Convergence?.Dominant, Diagnóstico = run.Failure
        };
    }

    private static string StateLabel(TwoStrokeRunStatus status) => status switch
    {
        TwoStrokeRunStatus.Periodic => "Periódico",
        TwoStrokeRunStatus.NonConverged => "No convergió",
        TwoStrokeRunStatus.OutsideDomain => "Fuera de dominio",
        _ => "Fallo numérico"
    };

    internal static string F(double? value) => value?.ToString("G6", CultureInfo.CurrentCulture) ?? "—";

    private static void FillRpm(ComboBox box, IEnumerable<int> values)
    {
        var all = values.ToArray();
        if (box.ItemsSource is int[] before && before.SequenceEqual(all)) return;
        var previous = box.SelectedItem;
        box.ItemsSource = all;
        box.SelectedItem = previous;
        if (box.SelectedIndex < 0 && all.Length > 0) box.SelectedIndex = 0;
    }

    internal static IEnumerable<TwoStrokePoint> Points(TwoStrokeSweep? sweep) =>
        sweep?.Runs.Select(r => r.Point).OfType<TwoStrokePoint>() ?? [];

    internal static void RenderCurves(ScientificPlot power, ScientificPlot torque, TwoStrokeSweep? a, TwoStrokeSweep? b)
    {
        IReadOnlyList<PlotLine> Lines(Func<TwoStrokePoint, double> shaft, Func<TwoStrokePoint, double> indicated)
        {
            var lines = new List<PlotLine>();
            void Add(TwoStrokeSweep? sweep, string name, Brush brush)
            {
                if (sweep is null) return;
                var points = Points(sweep).ToArray();
                bool markers = !sweep.Complete;
                lines.Add(new(name + " cigüeñal", points.Select(p => new Point(p.Rpm, shaft(p))).ToArray(), brush, MarkersOnly: markers));
                lines.Add(new(name + " indicada", points.Select(p => new Point(p.Rpm, indicated(p))).ToArray(), brush, true, markers));
            }
            Add(a, "A", Blue);
            Add(b, a is null ? "Actual" : "B", Orange);
            return lines;
        }
        power.SetLines(Lines(p => p.ShaftPowerW / 1000, p => p.IndicatedPowerW / 1000));
        torque.SetLines(Lines(p => p.ShaftTorqueNm, p => p.IndicatedTorqueNm));
    }

    private TwoStrokePoint? TracePoint() => TraceRpm.SelectedItem is int rpm
        ? Points(curve).FirstOrDefault(p => p.Rpm == rpm) : null;
    private void Trace_Changed(object sender, SelectionChangedEventArgs e) => RenderTrace();
    private void RenderTrace()
    {
        var p = TracePoint();
        if (p is null)
        {
            foreach (var plot in new[] { PressurePlot, PortsPlot, TemperaturePlot, FlowPlot }) plot.SetLines([]);
            TraceInfo.Text = "Simulá la configuración actual para inspeccionar el ciclo periódico.";
            return;
        }
        PlotLine Trace(string name, Brush brush, Func<TwoStrokeSample, double> y) =>
            new(name, p.Trace.Select(s => new Point(s.AngleDeg, y(s))).ToArray(), brush);
        PressurePlot.SetLines([Trace("Cilindro", Blue, s => s.CylinderPressurePa / 1e5),
            Trace("Cárter", Orange, s => s.CrankcasePressurePa / 1e5)]);
        PortsPlot.SetLines([Trace("Admisión en láminas", Blue, s => s.IntakeValvePressurePa / 1e5),
            Trace("Escape en lumbrera", Green, s => s.ExhaustPortPressurePa / 1e5)]);
        TemperaturePlot.SetLines([Trace("Cilindro", Blue, s => s.CylinderTemperatureK), Trace("Cárter", Orange, s => s.CrankcaseTemperatureK)]);
        FlowPlot.SetLines([Trace("Láminas", Blue, s => s.ReedKgPerS * 1000), Trace("Transfer hacia cilindro", Orange, s => s.TransferKgPerS * 1000),
            Trace("Escape saliente", Green, s => s.ExhaustOutKgPerS * 1000)]);
        var r = p.ResidualCompositionKg;
        TraceInfo.Text = $"Residuales al cierre [mg] · i-C₈H₁₈ {F(r.FuelKg * 1e6)} · O₂ {F(r.OxygenKg * 1e6)} · N₂ {F(r.NitrogenKg * 1e6)} · CO₂ {F(r.CarbonDioxideKg * 1e6)} · H₂O {F(r.WaterKg * 1e6)} · CO {F(r.CarbonMonoxideKg * 1e6)} · H₂ {F(r.HydrogenKg * 1e6)}";
    }

    internal static string StudyLabel(StudyVerdict verdict) => verdict switch
    {
        StudyVerdict.Superior => "Superior dentro de la resolución observada",
        StudyVerdict.Inferior => "Inferior dentro de la resolución observada",
        StudyVerdict.Indistinguishable => "Numéricamente indistinguible",
        StudyVerdict.DiscretizationSensitive => "Sensible a discretización",
        _ => "Contraste incompleto"
    };
}
