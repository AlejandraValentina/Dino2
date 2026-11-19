using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Dino2.Core.Studies;
using Dino2.Core.TwoStroke;
using Dino2.Storage;

namespace Dino2.Desktop;

internal sealed class TwoStrokeStudyPanel : DockPanel
{
    private readonly TwoStrokeWindow host;
    private readonly StackPanel inputs = new();
    private readonly ComboBox baseSource = Combo("Motor base del estudio", 195);
    private TwoStrokeConfiguration? loadedBase;
    private readonly ComboBox first, second, objective, workers;
    private readonly TextBox firstStart, firstEnd, firstStep, secondStart, secondEnd, secondStep;
    private readonly TextBox bandStart, bandEnd, lowStart, lowEnd, lowLoss;
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private readonly DataGrid rows = Grid("Candidatos 2T");
    private readonly DataGrid diagnostics = Grid("Resultados de candidato por RPM");
    private readonly ScientificPlot power = new() { Caption = "Potencia [kW]" };
    private readonly ScientificPlot torque = new() { Caption = "Par [N·m]" };
    private TwoStrokeStudy? plan;
    private TwoStrokeStudyRun? run;
    private sealed record Candidate(int Id, string Valores, string Estado, double? Objetivo, double? Margen_bajo_Nm,
        string Resolución, string Diagnóstico);
    private sealed record ObjectiveChoice(ObjectiveKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

    public TwoStrokeStudyPanel(TwoStrokeWindow host)
    {
        this.host = host;
        Margin = new Thickness(6);

        SetDock(inputs, Dock.Top);
        Children.Add(inputs);
        var pick = new WrapPanel();
        baseSource.Items.Add("Motor actual");
        baseSource.Items.Add("Original A");
        baseSource.SelectedIndex = 0;
        pick.Children.Add(baseSource);
        pick.Children.Add(new TextBlock { Text = "RPM tomadas del motor elegido", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) });
        inputs.Children.Add(pick);
        inputs.Children.Add(new TextBlock { Text = "Rangos de cada parámetro en orden: inicio, fin, paso.", Margin = new Thickness(4) });

        var axisOne = AxisLine("Parámetro 1", out first, out firstStart, out firstEnd, out firstStep);
        var axisTwo = AxisLine("Parámetro 2", out second, out secondStart, out secondEnd, out secondStep);
        first.ItemsSource = TwoStrokeParameters.All;
        second.ItemsSource = new object[] { "Sin segundo parámetro" }.Concat(TwoStrokeParameters.All).ToArray();
        inputs.Children.Add(axisOne);
        inputs.Children.Add(axisTwo);

        var choice = new WrapPanel();
        objective = Combo("Objetivo del estudio 2T", 200);
        objective.ItemsSource = new ObjectiveChoice[]
        {
            new(ObjectiveKind.PeakPower, "Potencia máxima"), new(ObjectiveKind.PeakTorque, "Par máximo"),
            new(ObjectiveKind.MeanPower, "Potencia media"), new(ObjectiveKind.MeanTorque, "Par medio en banda"),
            new(ObjectiveKind.HighTorqueLimitedLowLoss, "Par alto con límite en baja"), new(ObjectiveKind.MinimumBsfc, "Consumo específico mínimo")
        };
        choice.Children.Add(objective);
        choice.Children.Add(new TextBlock { Text = "Banda RPM", Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center });
        bandStart = Box(choice, "Desde RPM objetivo", "", 66);
        bandEnd = Box(choice, "Hasta RPM objetivo", "", 66);
        workers = Combo("Trabajadores 2T", 49);
        workers.ItemsSource = new[] { 1, 2, 4 };
        workers.SelectedIndex = 0;
        choice.Children.Add(new TextBlock { Text = "Núcleos", Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center });
        choice.Children.Add(workers);
        inputs.Children.Add(choice);

        var low = new WrapPanel();
        low.Children.Add(new TextBlock { Text = "Banda baja RPM", Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center });
        lowStart = Box(low, "Desde RPM baja", "", 66);
        lowEnd = Box(low, "Hasta RPM baja", "", 66);
        low.Children.Add(new TextBlock { Text = "Pérdida admisible [N·m]", Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center });
        lowLoss = Box(low, "Pérdida de par en baja", "0", 66);
        inputs.Children.Add(low);
        objective.SelectionChanged += (_, _) => low.Visibility =
            ((ObjectiveChoice?)objective.SelectedItem)?.Kind == ObjectiveKind.HighTorqueLimitedLowLoss ? Visibility.Visible : Visibility.Collapsed;
        objective.SelectedIndex = 1;

        var runBar = new WrapPanel();
        Button(runBar, "Previsualizar costo 2T", () => host.Guard(Preview));
        Button(runBar, "Ejecutar estudio 2T", async () => await host.Guard(ExecuteStudy));
        Button(runBar, "Verificar candidato 2T", async () => await host.Guard(Verify));
        Button(runBar, "Aplicar como B", () => host.Guard(Apply));
        inputs.Children.Add(runBar);
        var files = new WrapPanel();
        Button(files, "Guardar estudio 2T…", () => host.Guard(Save));
        Button(files, "Abrir estudio 2T…", () => host.Guard(Open));
        Button(files, "Exportar estudio CSV…", () => host.Guard(Export));
        inputs.Children.Add(files);
        SetDock(summary, Dock.Top);
        Children.Add(summary);

        var displays = new TabControl();
        Children.Add(displays);
        displays.Items.Add(new TabItem { Header = "Candidatos", Content = rows });
        var curves = new System.Windows.Controls.Grid();
        curves.RowDefinitions.Add(new());
        curves.RowDefinitions.Add(new());
        curves.Children.Add(power);
        System.Windows.Controls.Grid.SetRow(torque, 1);
        curves.Children.Add(torque);
        displays.Items.Add(new TabItem { Header = "Curva seleccionada", Content = curves });
        displays.Items.Add(new TabItem { Header = "Diagnósticos RPM", Content = diagnostics });
        rows.SelectionChanged += (_, _) => ShowCandidate();
        first.SelectionChanged += (_, _) => FillSelected(first, firstStart, firstEnd, firstStep);
        second.SelectionChanged += (_, _) => FillSelected(second, secondStart, secondEnd, secondStep);
        first.SelectedItem = TwoStrokeParameters.Get(TwoStrokeParameter.DiffuserLength);
        second.SelectedIndex = 0;
        firstStart.Text = "250";
        firstEnd.Text = "290";
        firstStep.Text = "40";
        summary.Text = "Verificá las diferencias con refinamiento antes de elegir una variante.";
    }

    internal void SetBusy(bool busy) => inputs.IsEnabled = !busy;

    private static DataGrid Grid(string name)
    {
        var grid = new DataGrid { IsReadOnly = true, CanUserAddRows = false, AutoGenerateColumns = true };
        AutomationProperties.SetName(grid, name);
        return grid;
    }

    private static ComboBox Combo(string name, double width)
    {
        var box = new ComboBox { Width = width, Margin = new Thickness(2) };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static TextBox Box(WrapPanel panel, string name, string text, double width)
    {
        var box = new TextBox { Text = text, Width = width, Margin = new Thickness(2), ToolTip = name };
        AutomationProperties.SetName(box, name);
        panel.Children.Add(box);
        return box;
    }

    private static WrapPanel AxisLine(string name, out ComboBox parameter, out TextBox start, out TextBox end, out TextBox step)
    {
        var panel = new WrapPanel();
        parameter = Combo(name, 268);
        panel.Children.Add(parameter);
        start = Box(panel, $"{name} inicio", "", 58);
        end = Box(panel, $"{name} fin", "", 58);
        step = Box(panel, $"{name} paso", "", 58);
        return panel;
    }

    private static void Button(WrapPanel parent, string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        parent.Children.Add(button);
    }

    private void FillSelected(ComboBox box, TextBox start, TextBox end, TextBox step)
    {
        start.IsEnabled = end.IsEnabled = step.IsEnabled = box.SelectedItem is TwoStrokeParameterSpec;
        if (box.SelectedItem is not TwoStrokeParameterSpec spec) return;
        host.Guard(() =>
        {
            var current = ReadBase();
            double value = spec.Read(current);
            start.Text = end.Text = TwoStrokeWindow.F(value);
            step.Text = "1";
        });
    }

    private static double Number(TextBox box) => TwoStrokeWindow.Parse(box.Text, AutomationProperties.GetName(box));
    private static int Rpm(TextBox box)
    {
        double value = Number(box);
        return value is >= 500 and <= 10000 && value == Math.Truncate(value)
            ? (int)value : throw new ArgumentException("Usá una RPM entera de la grilla.");
    }

    private static RpmBand? Band(TextBox start, TextBox end) =>
        string.IsNullOrWhiteSpace(start.Text) && string.IsNullOrWhiteSpace(end.Text) ? null : new(Rpm(start), Rpm(end));

    private static TwoStrokeAxis Axis(ComboBox parameter, TextBox start, TextBox end, TextBox step) =>
        parameter.SelectedItem is TwoStrokeParameterSpec spec
            ? new(spec.Id, Number(start), Number(end), Number(step))
            : throw new ArgumentException("Elegí un parámetro del estudio.");

    private TwoStrokeConfiguration ReadBase() => baseSource.SelectedIndex == 2
        ? loadedBase ?? throw new ArgumentException("Abrí un estudio para usar su motor base.")
        : host.StudyBase(baseSource.SelectedIndex == 1);

    private TwoStrokeStudy ReadPlan()
    {
        var basis = ReadBase();
        var axes = new List<TwoStrokeAxis> { Axis(first, firstStart, firstEnd, firstStep) };
        if (second.SelectedItem is TwoStrokeParameterSpec) axes.Add(Axis(second, secondStart, secondEnd, secondStep));
        var kind = ((ObjectiveChoice)objective.SelectedItem).Kind;
        var target = kind == ObjectiveKind.HighTorqueLimitedLowLoss
            ? new StudyObjective(kind, Band(bandStart, bandEnd), Band(lowStart, lowEnd), Number(lowLoss))
            : new StudyObjective(kind, Band(bandStart, bandEnd));
        return TwoStrokeStudy.Create(new(basis, axes, target));
    }

    private void Preview()
    {
        var preview = ReadPlan();
        summary.Text = $"{preview.Variants.Count - 1} variantes · {preview.OperatingPoints} simulaciones RPM distintas, incluida A del estudio.";
    }

    private async Task ExecuteStudy()
    {
        var activePlan = ReadPlan();
        int concurrency = (int)workers.SelectedItem;
        await host.Execute("Ejecutando estudio 2T…", async token =>
        {
            plan = activePlan;
            var partial = new Dictionary<int, TwoStrokeEvaluation>();
            run = null;
            Render();
            bool acceptingProgress = true;
            var progress = new Progress<TwoStrokeStudyProgress>(p =>
            {
                if (!acceptingProgress) return;
                partial[p.Evaluation.Id] = p.Evaluation;
                run = new(activePlan.Definition, partial.Values.OrderBy(row => row.Id).ToArray(), true, 0, concurrency);
                Render();
                host.ShowStatus($"Estudio 2T: {partial.Count}/{p.Total} configuraciones evaluadas.");
            });
            var result = await Task.Run(() => activePlan.Run(concurrency, token, progress));
            acceptingProgress = false;
            run = result;
            Render();
            host.ShowStatus(result.Cancelled ? "Estudio 2T cancelado. Se conservan las evaluaciones completas."
                : "Estudio 2T terminado.");
        });
    }

    private async Task Verify()
    {
        if (plan is null || run is null || rows.SelectedItem is not Candidate { Id: > 0 } candidate)
            throw new ArgumentException("Seleccioná una variante convergida para contrastar con la base del estudio.");
        var active = plan;
        var snapshot = run;
        await host.Execute("Verificando candidato 2T…", async token =>
        {
            var progress = new Progress<string>(host.ShowStatus);
            var result = await Task.Run(() => active.Verify(snapshot, candidate.Id, token: token, progress: progress));
            run = snapshot with { Checks = [.. (snapshot.Checks ?? []).Where(c => c.CandidateId != candidate.Id), result] };
            Render(candidate.Id);
            var delta = string.Join(" / ", result.Levels.Select(l => $"×{l.Scale}: {TwoStrokeWindow.F(l.Advantage)}"));
            summary.Text = $"#{candidate.Id} · {TwoStrokeWindow.StudyLabel(result.Verdict)} · {delta} {plan.Definition.Objective.Unit} · banda {TwoStrokeWindow.F(result.ObservedBand)}. " +
                string.Join(" ", result.Levels.Select(l => l.Error).Where(x => x is not null).Distinct());
            host.ShowStatus("Contraste de candidato terminado.");
        });
    }

    private void Render(int? keep = null)
    {
        if (plan is null || run is null)
        {
            rows.ItemsSource = null;
            ShowCandidate();
            return;
        }

        var baseline = run.Evaluations.FirstOrDefault(row => row.Id == 0)?.Sweep;
        int? previous = keep ?? (rows.SelectedItem as Candidate)?.Id;
        var table = run.Evaluations.Select(row =>
        {
            var score = plan.Score(row, baseline);
            var check = run.Checks?.LastOrDefault(v => v.CandidateId == row.Id);
            var values = plan.Variants[row.Id].Values;
            return (Candidate: new Candidate(row.Id, row.Id == 0 ? "Base del estudio" : string.Join(" / ", values.Select(x => TwoStrokeWindow.F(x))),
                row.Status.ToString(), score.Score, score.LowMarginNm,
                check is null ? "Pendiente de refinamiento" : TwoStrokeWindow.StudyLabel(check.Verdict), row.Error ?? score.Exclusion ?? ""),
                Eligible: score.Eligible);
        }).OrderByDescending(x => x.Eligible)
            .ThenByDescending(x => x.Candidate.Objetivo is double value ? plan.Definition.Objective.Preference(value) : double.NegativeInfinity)
            .Select(x => x.Candidate).ToArray();
        rows.ItemsSource = table;
        rows.SelectedItem = table.FirstOrDefault(row => row.Id == previous) ?? table.FirstOrDefault();
        summary.Text = $"{run.Evaluations.Count}/{plan.Variants.Count} evaluadas · {plan.Definition.Objective.Unit} · " +
            string.Join(" / ", plan.Definition.Axes.Select(a => TwoStrokeParameters.Get(a.Parameter))) + " · orden preliminar en malla base.";
    }

    private void ShowCandidate()
    {
        var baseline = run?.Evaluations.FirstOrDefault(row => row.Id == 0)?.Sweep;
        var result = rows.SelectedItem is Candidate candidate ? run?.Evaluations.FirstOrDefault(r => r.Id == candidate.Id)?.Sweep : null;
        TwoStrokeWindow.RenderCurves(power, torque, baseline, result);
        diagnostics.ItemsSource = result?.Runs.Select(TwoStrokeWindow.Describe).ToArray();
    }

    private void Apply()
    {
        if (plan is null || run is null || rows.SelectedItem is not Candidate { Id: > 0 } candidate ||
            run.Evaluations.FirstOrDefault(row => row.Id == candidate.Id) is not { Status: StudyStatus.Completed, Sweep: { } result })
            throw new ArgumentException("Seleccioná una variante con curva periódica completa.");
        host.Apply(plan.Variants[candidate.Id], result, run.Evaluations.FirstOrDefault(row => row.Id == 0)?.Sweep);
    }

    private void Open()
    {
        string? path = host.Choose(false, "Estudio Dino2 2T (*.json)|*.json", "estudio-2t.dino2.json");
        if (path is null) return;
        var loaded = TwoStrokeFiles.LoadStudy(path);
        plan = TwoStrokeStudy.Create(loaded.Definition);
        run = loaded;
        loadedBase = loaded.Definition.Base;
        if (baseSource.Items.Count == 2) baseSource.Items.Add("Base del estudio abierto");
        baseSource.SelectedIndex = 2;
        void SetAxis(TwoStrokeAxis axis, ComboBox box, TextBox start, TextBox end, TextBox step)
        {
            box.SelectedItem = TwoStrokeParameters.Get(axis.Parameter);
            start.Text = Exact(axis.Start);
            end.Text = Exact(axis.End);
            step.Text = Exact(axis.Step);
        }
        static string Exact(double value) => value.ToString("R", System.Globalization.CultureInfo.CurrentCulture);
        var axes = loaded.Definition.Axes;
        SetAxis(axes[0], first, firstStart, firstEnd, firstStep);
        if (axes.Count == 2) SetAxis(axes[1], second, secondStart, secondEnd, secondStep);
        else second.SelectedIndex = 0;
        var target = loaded.Definition.Objective;
        objective.SelectedItem = objective.Items.Cast<ObjectiveChoice>().Single(choice => choice.Kind == target.Kind);
        bandStart.Text = target.Band?.Start.ToString() ?? "";
        bandEnd.Text = target.Band?.End.ToString() ?? "";
        lowStart.Text = target.LowBand?.Start.ToString() ?? "";
        lowEnd.Text = target.LowBand?.End.ToString() ?? "";
        lowLoss.Text = Exact(target.AllowedLowLossNm);
        workers.SelectedItem = loaded.Workers;
        Render();
        host.ShowStatus("Estudio 2T recuperado.");
    }

    private void Save()
    {
        if (run is null) throw new ArgumentException("Ejecutá primero un estudio.");
        string? path = host.Choose(true, "Estudio Dino2 2T (*.json)|*.json", "estudio-2t.dino2.json");
        if (path is null) return;
        TwoStrokeFiles.SaveStudy(path, run);
        host.ShowStatus("Estudio 2T guardado.");
    }

    private void Export()
    {
        if (run is null) throw new ArgumentException("Ejecutá primero un estudio.");
        string? path = host.Choose(true, "CSV (*.csv)|*.csv", "estudio-2t.csv");
        if (path is null) return;
        TwoStrokeFiles.ExportStudy(path, run);
        host.ShowStatus("CSV de estudio 2T exportado.");
    }
}
