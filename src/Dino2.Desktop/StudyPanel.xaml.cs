using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dino2.Core.FourStroke;
using Dino2.Core.Studies;
using Dino2.Storage;
using Microsoft.Win32;

namespace Dino2.Desktop;

public partial class StudyPanel : UserControl
{
    private sealed record Choice<T>(T Id, string Text);
    private sealed record AxisEditor(ComboBox Parameter, TextBox Start, TextBox End, TextBox Step);
    private sealed record Row(int Id, string Label, int? Rank, string Parameters, double? Score, string Status);
    private readonly List<AxisEditor> axes = [];
    private readonly SortedDictionary<int, StudyEvaluation> results = [];
    private readonly List<StudyCheck> checks = [];
    private FourStrokeConfiguration? basis;
    private StudyPlan? resultPlan;
    private StudyRun? lastRun;
    private bool unsavedRun;
    private CancellationTokenSource? cancellation;
    private bool updating, refreshing;
    private Func<FourStrokeConfiguration>? currentEngine;
    private Func<FourStrokeConfiguration?>? originalEngine;
    private Action<FourStrokeConfiguration>? apply;

    internal bool IsBusy => cancellation is not null;
    internal StudyRun? LastRun => lastRun;
    internal IReadOnlyList<StudyCheck> Checks => checks;

    public StudyPanel()
    {
        InitializeComponent();
        StudyPower.Caption = "Potencia al cigüeñal [kW]";
        StudyTorque.Caption = "Par al cigüeñal [N·m]";
        ObjectiveBox.ItemsSource = new Choice<ObjectiveKind>[]
        {
            new(ObjectiveKind.PeakPower, "Potencia máxima muestreada"),
            new(ObjectiveKind.PeakTorque, "Par máximo muestreado"),
            new(ObjectiveKind.MeanPower, "Potencia media"),
            new(ObjectiveKind.MeanTorque, "Par medio"),
            new(ObjectiveKind.HighTorqueLimitedLowLoss, "Par alto con límite de pérdida en baja"),
            new(ObjectiveKind.MinimumBsfc, "Consumo específico mínimo")
        };
        ObjectiveBox.DisplayMemberPath = "Text";
        ObjectiveBox.SelectedValuePath = "Id";
        ObjectiveBox.SelectedValue = ObjectiveKind.PeakPower;
        WorkersBox.ItemsSource = Enumerable.Range(1, StudyPlan.MaximumWorkers).ToArray();
        WorkersBox.SelectedItem = Math.Min(2, Environment.ProcessorCount);
        for (int i = 0; i < 3; i++)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var selector = new ComboBox { Width = 318, DisplayMemberPath = "Text", SelectedValuePath = "Id",
                Margin = new Thickness(0, 0, 7, 0) };
            TextBox Field()
            {
                var field = new TextBox { Width = 75, Margin = new Thickness(0, 0, 7, 0) };
                field.TextChanged += (_, _) => UpdatePreview();
                return field;
            }
            var axis = new AxisEditor(selector, Field(), Field(), Field());
            axes.Add(axis);
            row.Children.Add(selector); row.Children.Add(axis.Start); row.Children.Add(axis.End); row.Children.Add(axis.Step);
            AxesEditor.Children.Add(row);
            selector.SelectionChanged += (_, _) => ParameterChanged(axis);
        }
        foreach (var box in new[] { RpmStart, RpmEnd, RpmStep, BandStart, BandEnd, LowStart, LowEnd, LowLoss })
            box.TextChanged += (_, _) => UpdatePreview();
        ObjectiveBox.SelectionChanged += (_, _) => UpdatePreview();
        UseBand.Checked += (_, _) => UpdatePreview();
        UseBand.Unchecked += (_, _) => UpdatePreview();
        StudyRows.SelectionChanged += (_, _) => { if (!refreshing) ShowSelection(); };
        ReferenceBox.SelectionChanged += (_, _) => { if (!refreshing) ShowSelection(); };
        PointRows.AutoGeneratingColumn += (_, e) =>
        {
            e.Column.Header = e.PropertyName switch
            {
                "BrakePowerW" => "Potencia [W]", "BrakeTorqueNm" => "Par [N·m]",
                "IndicatedPowerW" => "Indicada [W]", "VolumetricEfficiency" => "VE",
                "DeltaBaseW" => "Δ base [W]", "DeltaBaseNm" => "Δ base [N·m]",
                "PeriodicError" => "Residual periódico", _ => e.PropertyName
            };
            if (e.PropertyType == typeof(double) && e.Column is DataGridTextColumn column)
                column.Binding = new System.Windows.Data.Binding(e.PropertyName) { StringFormat = "G7" };
        };
    }

    internal void Configure(Func<FourStrokeConfiguration> current, Func<FourStrokeConfiguration?> original,
        Action<FourStrokeConfiguration> applyVariant)
    {
        currentEngine = current; originalEngine = original; apply = applyVariant;
    }

    private void UseCurrent_Click(object sender, RoutedEventArgs e) => Guard(() => SelectBase(currentEngine!()));
    private void UseOriginal_Click(object sender, RoutedEventArgs e) => Guard(() => SelectBase(originalEngine!() ??
        throw new ArgumentException("Conservá un motor como original A en la ventana 4T.")));

    internal void SelectBase(FourStrokeConfiguration engine)
    {
        double spark = engine.Combustion.SparkAdvanceDeg;
        double start = Math.Clamp(spark - 5, -10, 50);
        PopulatePlan(new(engine, [new(StudyParameter.Spark, start, start + 10, 5)], engine.Speeds,
            new(ObjectiveKind.PeakPower)));
        Pages.SelectedIndex = 0;
        StudyMessage.Text = "Base capturada. Editá los rangos y comprobá el costo antes de ejecutar.";
    }

    internal void PopulatePlan(StudyDefinition definition)
    {
        updating = true;
        basis = definition.Base;
        BaseLabel.Text = $"Base capturada · {basis.ModelIdentifier} · {basis.Geometry.DisplacementM3 * 1e6:F1} cm³";
        var choices = new[] { new Choice<StudyParameter?>(null, "Sin parámetro adicional") }
            .Concat(StudyParameters.All.Where(p => !p.RequiresDucts || basis.Ducts is not null)
                .Select(p => new Choice<StudyParameter?>(p.Id, p.Display))).ToArray();
        for (int i = 0; i < axes.Count; i++)
        {
            var editor = axes[i];
            editor.Parameter.ItemsSource = choices;
            var input = i < definition.Axes.Count ? definition.Axes[i] : null;
            editor.Parameter.SelectedItem = choices.Single(p => p.Id == input?.Parameter);
            SetAxisEnabled(editor, input is not null);
            editor.Start.Text = Format(input?.Start);
            editor.End.Text = Format(input?.End);
            editor.Step.Text = Format(input?.Increment);
        }
        RpmStart.Text = Format(definition.Speeds.Start);
        RpmEnd.Text = Format(definition.Speeds.End);
        RpmStep.Text = Format(definition.Speeds.Step);
        var objective = definition.Objective;
        ObjectiveBox.SelectedValue = objective.Kind;
        UseBand.IsChecked = objective.Band is not null;
        BandStart.Text = Format(objective.Band?.Start ?? definition.Speeds.Start);
        BandEnd.Text = Format(objective.Band?.End ?? definition.Speeds.End);
        LowStart.Text = Format(objective.LowBand?.Start ?? definition.Speeds.Start);
        LowEnd.Text = Format(objective.LowBand?.End ?? definition.Speeds.Start);
        LowLoss.Text = Format(objective.AllowedLowLossNm);
        updating = false;
        UpdatePreview();
    }

    private void ParameterChanged(AxisEditor editor)
    {
        if (updating) return;
        var parameter = (editor.Parameter.SelectedItem as Choice<StudyParameter?>)?.Id;
        SetAxisEnabled(editor, parameter.HasValue);
        if (parameter is { } id && basis is not null)
        {
            double value = StudyParameters.Get(id).Read(basis);
            editor.Start.Text = editor.End.Text = Format(value);
            editor.Step.Text = "1";
        }
        UpdatePreview();
    }

    private static void SetAxisEnabled(AxisEditor editor, bool enabled) =>
        editor.Start.IsEnabled = editor.End.IsEnabled = editor.Step.IsEnabled = enabled;

    private StudyPlan ReadPlan()
    {
        if (basis is null) throw new ArgumentException("Tomá un motor base.");
        var ranges = axes.Where(e => e.Parameter.SelectedItem is Choice<StudyParameter?> { Id: not null })
            .Select(e => new ParameterRange(((Choice<StudyParameter?>)e.Parameter.SelectedItem).Id!.Value,
                Parse(e.Start), Parse(e.End), Parse(e.Step))).ToArray();
        var speeds = new RpmRange(Integer(RpmStart), Integer(RpmEnd), Integer(RpmStep));
        var objective = (ObjectiveKind)ObjectiveBox.SelectedValue;
        bool compromise = objective == ObjectiveKind.HighTorqueLimitedLowLoss;
        return StudyPlan.Create(new(basis, ranges, speeds, new(objective,
            UseBand.IsChecked == true || compromise ? new(Integer(BandStart), Integer(BandEnd)) : null,
            compromise ? new(Integer(LowStart), Integer(LowEnd)) : null, compromise ? Parse(LowLoss) : 0)));
    }

    private void UpdatePreview()
    {
        if (updating) return;
        bool compromise = ObjectiveBox.SelectedValue is ObjectiveKind.HighTorqueLimitedLowLoss;
        BandFields.Visibility = compromise || UseBand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LowFields.Visibility = compromise ? Visibility.Visible : Visibility.Collapsed;
        UseBand.IsEnabled = !compromise;
        try
        {
            var plan = ReadPlan();
            Preview.Text = $"{plan.Variants.Count - 1} variantes + base · {plan.DistinctCurves} curvas únicas · {plan.OperatingPoints} simulaciones por RPM como máximo.";
            if (plan.EstimatedDuctWork > 0) Preview.Text += $" · Esfuerzo 1D relativo {plan.EstimatedDuctWork:F1}/{StudyPlan.MaximumDuctWork:F0}.";
            StudyStart.IsEnabled = !IsBusy;
        }
        catch (ArgumentException ex) { Preview.Text = ex.Message; StudyStart.IsEnabled = false; }
    }

    internal async Task RunAsync()
    {
        if (IsBusy) return;
        try
        {
            var plan = ReadPlan();
            int workers = (int)WorkersBox.SelectedItem;
            resultPlan = plan;
            lastRun = null;
            results.Clear(); checks.Clear();
            BeginWork();
            Pages.SelectedIndex = 1;
            RunProgress.Maximum = plan.Variants.Count;
            RunProgress.Value = 0;
            var token = cancellation!.Token;
            var progress = new Progress<StudyProgress>(p =>
            {
                if (!IsBusy || lastRun is not null) return;
                results[p.Evaluation.Id] = p.Evaluation;
                RunProgress.Value = results.Count;
                StudyMessage.Text = $"{results.Count}/{p.Total} casos evaluados";
                RefreshRows();
            });
            lastRun = await Task.Run(() => StudyRunner.Run(plan, workers, token, progress));
            unsavedRun = lastRun.Evaluations.Count > 0;
            RestoreRun(lastRun);
            StudyMessage.Text = $"{(lastRun.Cancelled ? "Estudio cancelado" : "Estudio completo")} · {results.Count}/{plan.Variants.Count} casos · {lastRun.Milliseconds / 1000:F2} s";
        }
        catch (Exception ex) when (Expected(ex)) { StudyMessage.Text = ex.Message; }
        finally { EndWork(); }
    }

    internal async Task VerifyAsync(int candidate, int reference)
    {
        if (IsBusy || lastRun is null || resultPlan is null) return;
        if (HistoricalRun)
        {
            StudyMessage.Text = "Estudio histórico. Ejecutá de nuevo el estudio para contrastar con el solver actual.";
            return;
        }
        if (checks.Any(c => c.CandidateId == candidate && c.ReferenceId == reference && c.Verdict != StudyVerdict.Unavailable))
        {
            StudyMessage.Text = "Contraste ya disponible para este par.";
            return;
        }
        try
        {
            BeginWork();
            RunProgress.IsIndeterminate = true;
            var plan = resultPlan; var run = lastRun;
            var progress = new Progress<string>(message => { if (IsBusy) StudyMessage.Text = message; });
            var token = cancellation!.Token;
            var check = await Task.Run(() => StudyVerification.Compare(plan, run, candidate, reference, token, progress));
            checks.RemoveAll(c => c.CandidateId == candidate && c.ReferenceId == reference);
            checks.Add(check);
            unsavedRun = true;
            StudyMessage.Text = $"Contraste finalizado · {check.Milliseconds / 1000:F2} s";
        }
        catch (OperationCanceledException) { StudyMessage.Text = "Contraste cancelado. Se conservan las curvas del estudio."; }
        catch (Exception ex) when (Expected(ex)) { StudyMessage.Text = ex.Message; }
        finally { EndWork(); }
    }

    private void BeginWork()
    {
        cancellation = new CancellationTokenSource();
        PlanFields.IsEnabled = StudyOpen.IsEnabled = StudyStart.IsEnabled = StudySave.IsEnabled = StudyExport.IsEnabled = false;
        StudyCancel.IsEnabled = true;
        RefreshRows();
    }

    private void EndWork()
    {
        cancellation?.Dispose();
        cancellation = null;
        RunProgress.IsIndeterminate = false;
        PlanFields.IsEnabled = StudyOpen.IsEnabled = true;
        StudySave.IsEnabled = StudyExport.IsEnabled = lastRun is not null;
        StudyCancel.IsEnabled = false;
        UpdatePreview();
        RefreshRows();
    }

    internal void Cancel() => cancellation?.Cancel();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmDiscard(Window.GetWindow(this))) await RunAsync();
    }

    internal bool ConfirmDiscard(Window owner, Func<bool>? confirm = null) => !unsavedRun ||
        (confirm?.Invoke() ?? DesktopLayout.ConfirmDiscard(owner, "el estudio 4T"));
    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        if (StudyRows.SelectedItem is Row row && ReferenceBox.SelectedValue is int reference) await VerifyAsync(row.Id, reference);
    }

    private void RestoreRun(StudyRun run)
    {
        results.Clear();
        foreach (var point in run.Evaluations) results[point.Id] = point;
        RunProgress.Maximum = resultPlan!.Variants.Count;
        RunProgress.Value = results.Count;
        RefreshRows();
    }

    private void RefreshRows()
    {
        if (resultPlan is null) return;
        var objective = resultPlan.Definition.Objective;
        var ranking = StudyRunner.Rank(objective, results.Values.ToArray());
        var positions = ranking.Select((r, i) => (r.Id, Position: i + 1)).ToDictionary(x => x.Id, x => x.Position);
        var baseline = results.GetValueOrDefault(0)?.Points;
        var selected = (StudyRows.SelectedItem as Row)?.Id;
        int reference = ReferenceBox.SelectedValue is int id ? id : 0;
        refreshing = true;
        var rows = resultPlan.Variants.Select(v =>
        {
            var row = results.GetValueOrDefault(v.Id);
            var value = row is { Status: StudyStatus.Completed } ? objective.Evaluate(row.Points, baseline) : null;
            return new Row(v.Id, Label(v.Id), positions.TryGetValue(v.Id, out int pos) ? pos : null,
                Parameters(v), value?.Score, value?.Exclusion ?? StatusLabel(row?.Status));
        }).OrderBy(v => v.Id == 0 ? 0 : v.Rank ?? int.MaxValue).ThenBy(v => v.Id).ToArray();
        StudyRows.ItemsSource = rows;
        StudyRows.SelectedItem = rows.FirstOrDefault(r => r.Id == (selected ?? ranking.FirstOrDefault()?.Id ?? 0));
        var refs = results.Values.Where(r => r.Status == StudyStatus.Completed)
            .Select(r => new Choice<int>(r.Id, Label(r.Id))).ToArray();
        ReferenceBox.ItemsSource = refs;
        ReferenceBox.DisplayMemberPath = "Text";
        ReferenceBox.SelectedValuePath = "Id";
        ReferenceBox.SelectedValue = refs.Any(r => r.Id == reference) ? reference : refs.FirstOrDefault()?.Id;
        refreshing = false;
        ResultNote.Text = $"Objetivo [{objective.Unit}] · Ranking de muestras convergidas{(lastRun?.Cancelled == true ? ", estudio parcial" : "")}. " +
            "El orden inicial usa la resolución base del estudio ejecutado. Contrastá contra la base o un rival para evaluar diferencias pequeñas.";
        ShowSelection();
        if (!IsBusy) DrawSensitivity(ranking);
    }

    private void ShowSelection()
    {
        if (resultPlan is null) return;
        var row = StudyRows.SelectedItem as Row;
        var selection = row is null ? null : results.GetValueOrDefault(row.Id);
        int alternate = ReferenceBox.SelectedValue is int number ? number : 0;
        bool ready = selection is { Status: StudyStatus.Completed, Id: > 0 };
        StudyApply.IsEnabled = ready && !IsBusy && lastRun is not null;
        StudyCheckButton.IsEnabled = !HistoricalRun && StudyApply.IsEnabled && row!.Id != alternate &&
            results.GetValueOrDefault(alternate)?.Status == StudyStatus.Completed;
        CheckText.Text = selection?.Error ?? "";
        if (ready)
        {
            var check = checks.LastOrDefault(c => c.CandidateId == row!.Id && c.ReferenceId == alternate);
            var definition = resultPlan.Definition;
            int additional = 2 * definition.Speeds.Values().Count() *
                (definition.Objective.Kind == ObjectiveKind.HighTorqueLimitedLowLoss && alternate != 0 ? 3 : 2);
            if (additional > StudyPlan.MaximumVerificationPoints) StudyCheckButton.IsEnabled = false;
            CheckText.Text = check is null
                ? additional > StudyPlan.MaximumVerificationPoints
                    ? "El contraste supera el límite de puntos adicionales. Ejecutá un estudio con menos RPM."
                    : $"Resolución sin contrastar. Hasta {additional} simulaciones RPM adicionales para esta comparación."
                : DescribeCheck(check);
        }
        var basePoints = results.GetValueOrDefault(0)?.Points.ToDictionary(p => p.Rpm);
        PointRows.ItemsSource = selection?.Points.Select(p => new
        {
            p.Rpm, p.BrakePowerW, p.BrakeTorqueNm, p.IndicatedPowerW, p.VolumetricEfficiency,
            DeltaBaseW = basePoints?.GetValueOrDefault(p.Rpm) is StudyPoint bp ? p.BrakePowerW - bp.BrakePowerW : (double?)null,
            DeltaBaseNm = basePoints?.GetValueOrDefault(p.Rpm) is StudyPoint bt ? p.BrakeTorqueNm - bt.BrakeTorqueNm : (double?)null,
            p.FuelMassKgPerCycle, p.NetWorkJ, p.PeakPressurePa, p.PeakTemperatureK, p.Cycles, p.PeriodicError,
            p.MassResidualKg, p.EnergyResidualJ, p.SpeciesResidualKg, p.DuctEnergyResidualJ, p.DuctMomentumResidualNs,
            p.FlameCorrelationExtrapolated
        }).ToArray();
        var curves = new List<(StudyEvaluation Result, Brush Brush)>();
        void Add(int id, Brush brush)
        {
            if (results.GetValueOrDefault(id) is { Status: StudyStatus.Completed } curve && curves.All(c => c.Result.Id != id))
                curves.Add((curve, brush));
        }
        Add(0, Brushes.SteelBlue); Add(alternate, Brushes.SeaGreen);
        if (row is not null) Add(row.Id, Brushes.DarkOrange);
        StudyPower.SetLines(curves.Select(c => new PlotLine(Label(c.Result.Id),
            c.Result.Points.Select(p => new Point(p.Rpm, p.BrakePowerW / 1000)).ToArray(), c.Brush)).ToArray());
        StudyTorque.SetLines(curves.Select(c => new PlotLine(Label(c.Result.Id),
            c.Result.Points.Select(p => new Point(p.Rpm, p.BrakeTorqueNm)).ToArray(), c.Brush)).ToArray());
        CurveNote.Text = string.Join(" · ", curves.Select(x => Label(x.Result.Id))) +
            ". Azul base, verde referencia adicional, naranja selección. Los marcadores son RPM simuladas.";
        if (row is not null && resultPlan.Variants[row.Id].Configuration.Ducts is { } duct)
            CurveNote.Text += $" Malla de la selección {duct.Intake.Cells}/{duct.Exhaust.Cells} celdas.";
    }

    private string DescribeCheck(StudyCheck check)
    {
        var unit = resultPlan!.Definition.Objective.Unit;
        string state = check.Verdict switch
        {
            StudyVerdict.Superior => "Superior a la referencia en este contraste",
            StudyVerdict.Inferior => "Inferior a la referencia en este contraste",
            StudyVerdict.Indistinguishable => "Numéricamente indistinguibles",
            StudyVerdict.DiscretizationSensitive => "Sensible a discretización o al límite de torque",
            _ => "Contraste no disponible"
        };
        string values = string.Join(" / ", check.Levels.Select(l => Format(l.Advantage)));
        var error = check.Levels.LastOrDefault(l => l.Error is not null)?.Error;
        string history = HistoricalRun ? "Histórico · " : "";
        return $"{history}{state}. Ventaja base / ×2 / ×4 [{unit}] {values}. Banda empírica {Format(check.ObservedBand)}. {error}";
    }

    private bool HistoricalRun => lastRun is not null && lastRun.Provenance?.NumericalRevision != lastRun.Definition.Base.ModelIdentifier;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (StudyRows.SelectedItem is Row row) Guard(() => ApplyVariant(row.Id));
    }

    internal void ApplyVariant(int id)
    {
        if (IsBusy || resultPlan is null || id <= 0 || results.GetValueOrDefault(id)?.Status != StudyStatus.Completed)
            throw new ArgumentException("Seleccioná una variante convergida.");
        apply!(resultPlan.Variants[id].Configuration);
        StudyMessage.Text = $"Variante {id} aplicada como B. Conservá este estudio para recuperar sus curvas.";
    }

    internal void SaveTo(string path)
    {
        StudyFiles.Save(path, new(lastRun ?? throw new ArgumentException("Sin estudio ejecutado."), checks));
        unsavedRun = false;
    }
    internal void ExportTo(string path) => StudyFiles.Export(path, new(lastRun ?? throw new ArgumentException("Sin estudio ejecutado."), checks));
    internal void LoadFrom(string path)
    {
        var document = StudyFiles.Load(path);
        resultPlan = StudyPlan.Create(document.Run.Definition);
        PopulatePlan(resultPlan.Definition);
        lastRun = document.Run;
        unsavedRun = false;
        checks.Clear(); checks.AddRange(document.Checks);
        RestoreRun(lastRun);
        Pages.SelectedIndex = 1;
        StudyMessage.Text = lastRun.Cancelled ? "Estudio parcial recuperado." : "Estudio recuperado.";
        StudyMessage.Text += " " + Dino2.Core.ResultProvenance.Describe(lastRun.Provenance);
        if (HistoricalRun) StudyMessage.Text += " Estudio histórico. Ejecutalo de nuevo para verificar con el solver actual.";
        if (lastRun.Definition.Base.Ducts?.BoundaryNotice() is { } notice) StudyMessage.Text += " " + notice;
        EndWork();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new SaveFileDialog { Filter = "Estudio Dino2 (*.dino-study.json)|*.dino-study.json", FileName = "motor.dino-study.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) SaveTo(dialog.FileName);
    });
    private void Open_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (!ConfirmDiscard(Window.GetWindow(this))) return;
        var dialog = new OpenFileDialog { Filter = "Estudio Dino2 (*.dino-study.json)|*.dino-study.json|JSON (*.json)|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) LoadFrom(dialog.FileName);
    });
    private void Export_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "estudio.csv" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) ExportTo(dialog.FileName);
    });

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) when (Expected(ex)) { StudyMessage.Text = ex.Message; }
    }

    private static bool Expected(Exception e) => e is ArgumentException or ArithmeticException or IOException or
        InvalidDataException or UnauthorizedAccessException or JsonException;

    private static double Parse(TextBox box)
    {
        if (!double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            !double.IsFinite(value)) throw new ArgumentException("Completá los valores con números finitos.");
        return value;
    }

    private static int Integer(TextBox box)
    {
        var value = Parse(box);
        if (value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new ArgumentException("Ingresá RPM enteras.");
        return (int)value;
    }

    private static string Format(double? value) => value?.ToString("G10", CultureInfo.InvariantCulture) ?? "";
    private static string Label(int id) => id == 0 ? "Base" : $"V{id}";
    private static string Parameters(StudyVariant variant) => string.Join(" · ",
        variant.Parameters.Select(p =>
        {
            var spec = StudyParameters.Get(p.Parameter);
            return $"{spec.Label} {Format(p.Value)} {spec.Unit}";
        }));
    private static string StatusLabel(StudyStatus? status) => status switch
    {
        StudyStatus.Completed => "Convergida",
        StudyStatus.Inadmissible => "Fuera de dominio",
        StudyStatus.NonConverged => "Sin periodicidad",
        StudyStatus.NumericalFailure => "Fallo numérico",
        _ => "Pendiente"
    };
}
