using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dino2.Core.Multicylinder;
using Dino2.Core.Studies;
using Dino2.Storage;

namespace Dino2.Desktop;

public partial class MulticylinderWindow
{
    private sealed record AxisEditor(ComboBox Parameter, TextBox Start, TextBox End, TextBox Step);
    private sealed class ObjectiveOption(ObjectiveKind kind, string label)
    {
        internal ObjectiveKind Kind { get; } = kind;
        public override string ToString() => label;
    }
    private sealed record StudyDisplay(int Caso, int? Puesto_muestreado, string Parámetros,
        double? Objetivo, string Estado);
    private readonly List<AxisEditor> axes = [];
    private MultiConfiguration? studyBase;
    private MultiStudy? studyPlan;
    private MultiStudyProject? savedStudy;
    private bool editingStudy;

    private void BuildStudyEditor()
    {
        for (int i = 0; i < 3; i++)
        {
            var line = new WrapPanel { Margin = new Thickness(0, 3, 0, 4) };
            var parameter = new ComboBox { Name = $"MultiStudyAxis{i + 1}", Width = 248, Margin = new Thickness(0, 0, 6, 0) };
            RegisterName(parameter.Name, parameter);
            parameter.Items.Add("Sin parámetro");
            foreach (var spec in StudyParameters.All) parameter.Items.Add(spec.Display);
            parameter.SelectedIndex = 0;
            line.Children.Add(parameter);
            TextBox Field(string name)
            {
                var box = new TextBox { Name = $"MultiStudyAxis{i + 1}{name}", Width = 65, Margin = new Thickness(0, 0, 3, 0) };
                RegisterName(box.Name, box);
                System.Windows.Automation.AutomationProperties.SetName(box, name);
                box.TextChanged += StudyInput_Changed;
                line.Children.Add(box);
                return box;
            }
            var axis = new AxisEditor(parameter, Field("Inicio"), Field("Fin"), Field("Incremento"));
            axes.Add(axis);
            parameter.SelectionChanged += (_, _) =>
            {
                if (!editingStudy && studyBase is not null && parameter.SelectedIndex > 0)
                {
                    var spec = StudyParameters.All[parameter.SelectedIndex - 1];
                    editingStudy = true;
                    axis.Start.Text = axis.End.Text = Format(spec.Read(studyBase.Cylinder));
                    axis.Step.Text = Format(1);
                    editingStudy = false;
                }
                UpdateStudyPlan();
            };
            AxisPanel.Children.Add(line);
        }
        ObjectiveBox.ItemsSource = new[]
        {
            new ObjectiveOption(ObjectiveKind.PeakPower, "Potencia máxima"),
            new ObjectiveOption(ObjectiveKind.PeakTorque, "Par máximo"),
            new ObjectiveOption(ObjectiveKind.MeanPower, "Potencia media en RPM"),
            new ObjectiveOption(ObjectiveKind.MeanTorque, "Par medio en RPM"),
            new ObjectiveOption(ObjectiveKind.HighTorqueLimitedLowLoss, "Par en alta con límite de pérdida en baja"),
            new ObjectiveOption(ObjectiveKind.MinimumBsfc, "Menor consumo específico calculado")
        };
        ObjectiveBox.SelectedIndex = 0;
        Workers.ItemsSource = Enumerable.Range(1, StudyPlan.MaximumWorkers);
        Workers.SelectedIndex = 0;
    }

    private void StudyInput_Changed(object sender, RoutedEventArgs e) => UpdateStudyPlan();

    private void UpdateStudyPlan()
    {
        if (editingStudy || !initialized) return;
        studyPlan = null;
        var option = (ObjectiveOption)ObjectiveBox.SelectedItem;
        LowBand.Visibility = option.Kind == ObjectiveKind.HighTorqueLimitedLowLoss ? Visibility.Visible : Visibility.Collapsed;
        if (studyBase is null) { StudyCost.Text = ""; RefreshStudyButtons(); return; }
        RpmBand? Band(TextBox x, TextBox y) => x.Text.Length == 0 && y.Text.Length == 0 ? null :
            new((int)Value(x, "inicio de banda", true), (int)Value(y, "fin de banda", true));
        try
        {
            var ranges = axes.Where(a => a.Parameter.SelectedIndex > 0).Select(a => new ParameterRange(
                StudyParameters.All[a.Parameter.SelectedIndex - 1].Id, Value(a.Start, "inicio"), Value(a.End, "fin"),
                Value(a.Step, "incremento"))).ToArray();
            var target = option.Kind == ObjectiveKind.HighTorqueLimitedLowLoss
                ? new StudyObjective(option.Kind, Band(BandFrom, BandTo), Band(LowFrom, LowTo), Value(LowLoss, "pérdida en baja"))
                : new StudyObjective(option.Kind, Band(BandFrom, BandTo));
            var plan = new MultiStudy(new(studyBase, ranges, target));
            studyPlan = plan;
            StudyCost.Text = $"{plan.Grid.Variants.Count} casos · {plan.Grid.OperatingPoints} puntos RPM únicos · costo relativo {plan.EstimatedWork:F1}";
        }
        catch (ArgumentException ex) { StudyCost.Text = ex.Message; }
        finally { RefreshStudyButtons(); }
    }

    private void StudyTake_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = (sender as Button)?.Tag as string == "A" ? original : ReadEditor();
            if (config is null) throw new ArgumentException("Todavía no hay una configuración A.");
            SetStudyBase(config);
            if (axes[0].Parameter.SelectedIndex <= 0)
                axes[0].Parameter.SelectedIndex = StudyParameters.All.ToList().FindIndex(s => s.Id == StudyParameter.IntakeLength) + 1;
            UpdateStudyPlan();
            StudyPages.SelectedIndex = 0;
        }
        catch (ArgumentException ex) { Message(ex); }
    }

    private void SetStudyBase(MultiConfiguration? config)
    {
        studyBase = config;
        if (config is null) { StudyBaseText.Text = "Tomá una configuración base. Ajustá las RPM en el motor antes de tomarlo."; return; }
        var rpm = config.Cylinder.Speeds;
        StudyBaseText.Text = $"Base fijada · {config.Phases.Count} cilindros · {rpm.Start}–{rpm.End} RPM · paso {rpm.Step}.";
    }

    private async void StudyRun_Click(object sender, RoutedEventArgs e) => await RunStudyEditor();
    internal async Task RunStudyEditor()
    {
        if (studyPlan is not { } plan) return;
        if (savedStudy is not null && !ConfirmDiscard()) return;
        int workers = (int)Workers.SelectedItem;
        await ExecuteAsync((token, progress) => plan.Run(workers, token,
            new InlineProgress<StudyProgress>(p => progress.Report($"Estudio · {p.Evaluated}/{p.Total} · caso {p.Evaluation.Id}"))), run =>
        {
            savedStudy = new(plan.Definition, run);
            dirty = true;
            RenderStudy();
            StudyPages.SelectedIndex = 1;
            MultiStatusText.Text = $"{(run.Cancelled ? "Estudio cancelado" : "Estudio completado")} · {run.Evaluations.Count} casos · {run.Milliseconds / 1000:F2} s.";
        });
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private void RestoreStudy(MultiStudyProject? study)
    {
        editingStudy = true;
        savedStudy = study;
        SetStudyBase(study?.Definition.Base);
        var def = study?.Definition;
        foreach (var (axis, i) in axes.Select((axis, index) => (axis, index)))
        {
            var range = def?.Axes.ElementAtOrDefault(i);
            axis.Parameter.SelectedIndex = range is null ? 0 :
                StudyParameters.All.ToList().FindIndex(p => p.Id == range.Parameter) + 1;
            axis.Start.Text = range is null ? "" : Format(range.Start);
            axis.End.Text = range is null ? "" : Format(range.End);
            axis.Step.Text = range is null ? "" : Format(range.Increment);
        }
        var objective = def?.Objective ?? new(ObjectiveKind.PeakPower);
        ObjectiveBox.SelectedItem = ((IEnumerable<ObjectiveOption>)ObjectiveBox.ItemsSource).Single(o => o.Kind == objective.Kind);
        BandFrom.Text = objective.Band?.Start.ToString() ?? ""; BandTo.Text = objective.Band?.End.ToString() ?? "";
        LowFrom.Text = objective.LowBand?.Start.ToString() ?? ""; LowTo.Text = objective.LowBand?.End.ToString() ?? "";
        LowLoss.Text = Format(objective.AllowedLowLossNm);
        Workers.SelectedItem = study?.Run.Workers ?? 1;
        editingStudy = false;
        UpdateStudyPlan();
        RenderStudy();
    }

    private void RenderStudy()
    {
        int? selected = (StudyRows.SelectedItem as StudyDisplay)?.Caso;
        if (savedStudy is not { } study)
        {
            StudyRows.ItemsSource = null;
            StudyNote.Text = "";
            StudySelection_Changed(this, null!);
            return;
        }
        var grid = new MultiStudy(study.Definition).Grid;
        var rank = StudyRunner.Rank(study.Definition.Objective, study.Run.Evaluations)
            .Select((r, i) => (r.Id, Position: i + 1, r.Objective.Score)).ToDictionary(r => r.Id);
        var rows = study.Run.Evaluations.Select(row =>
        {
            var parameters = grid.Variants[row.Id].Parameters;
            return new StudyDisplay(row.Id, rank.TryGetValue(row.Id, out var r) ? r.Position : null,
                row.Id == 0 ? "Base" : string.Join(" · ", parameters.Select(p => $"{StudyParameters.Get(p.Parameter).Display} = {p.Value:G6}")),
                r.Score, row.Error ?? (row.Status == StudyStatus.Completed ? "Periódico" : row.Status.ToString()));
        }).OrderBy(r => r.Caso == 0 ? 0 : r.Puesto_muestreado ?? int.MaxValue).ToArray();
        StudyRows.ItemsSource = rows;
        StudyRows.SelectedItem = rows.FirstOrDefault(r => r.Caso == selected) ?? rows.FirstOrDefault(r => r.Puesto_muestreado == 1)
            ?? rows.FirstOrDefault();
        StudyNote.Text = $"Puestos en la grilla; objetivo en {study.Definition.Objective.Unit}." +
            (study.Check is { } check ? $" Caso {check.CandidateId} · {VerdictLabel(check.Verdict)}" +
                (check.ObservedBand is double band ? $" · banda {band:G6} {study.Definition.Objective.Unit}." : ".") +
                (check.Levels.LastOrDefault()?.Error is { } error ? " " + error : "") : "");
        StudySelection_Changed(this, null!);
    }

    private void StudySelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        var study = savedStudy;
        var row = StudyRows.SelectedItem as StudyDisplay;
        var evaluated = study?.Run.Evaluations.SingleOrDefault(r => r.Id == row?.Caso);
        var basis = study?.Run.Evaluations.SingleOrDefault(r => r.Id == 0);
        StudyPointRows.ItemsSource = evaluated?.Points;
        IReadOnlyList<PlotLine> Curves(Func<StudyPoint, double> read)
        {
            var lines = new List<PlotLine>();
            int[] speeds = study?.Definition.Base.Cylinder.Speeds.Values().ToArray() ?? [];
            void Line(StudyEvaluation? candidate, string label, Brush brush)
            {
                if (candidate is null) return;
                var values = candidate.Points.ToDictionary(p => p.Rpm);
                lines.Add(new(label, speeds.Select(r => new Point(r, values.TryGetValue(r, out var p) ? read(p) : double.NaN)).ToArray(), brush));
            }
            Line(basis, "Base", Brushes.IndianRed);
            if (row?.Caso > 0) Line(evaluated, $"Caso {row.Caso}", Brushes.SteelBlue);
            return lines;
        }
        StudyPower.SetLines(Curves(p => p.BrakePowerW / 1000));
        StudyTorque.SetLines(Curves(p => p.BrakeTorqueNm));
        RefreshStudyButtons();
    }

    private void RefreshStudyButtons()
    {
        if (!initialized) return;
        bool ready = cancellation is null;
        MultiStudyStart.IsEnabled = ready && studyPlan is not null;
        MultiStudyExport.IsEnabled = ready && savedStudy is not null;
        int selected = (StudyRows.SelectedItem as StudyDisplay)?.Caso ?? 0;
        bool periodic = savedStudy?.Run.Evaluations.Any(r => r.Id == selected && r.Status == StudyStatus.Completed) == true;
        MultiStudyApply.IsEnabled = ready && selected > 0 && periodic;
        MultiStudyCheck.IsEnabled = MultiStudyApply.IsEnabled &&
            savedStudy?.Run.Evaluations.Any(r => r.Id == 0 && r.Status == StudyStatus.Completed) == true;
    }

    private void StudyApply_Click(object sender, RoutedEventArgs e)
    {
        if (savedStudy is not { } study || StudyRows.SelectedItem is not StudyDisplay row) return;
        var plan = new MultiStudy(study.Definition);
        original ??= plan.Variant(0);
        Populate(plan.Variant(row.Caso));
        Changed();
        MultiStatusText.Text = $"Caso {row.Caso} aplicado como B. Simulá para inspeccionar su ciclo y compararlo con A.";
        Pages.SelectedIndex = 0;
    }

    private async void StudyCheck_Click(object sender, RoutedEventArgs e)
    {
        if (savedStudy is not { } study || StudyRows.SelectedItem is not StudyDisplay row) return;
        var plan = new MultiStudy(study.Definition);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await ExecuteAsync((token, progress) => MultiComparison.VerifyObjective(plan, row.Caso, token, progress), levels =>
        {
            var (verdict, band) = StudyVerification.Classify(levels);
            savedStudy = study with { Check = new(row.Caso, 0, verdict, band, levels, clock.Elapsed.TotalMilliseconds,
                NumericalRevision: MultiConfiguration.ModelId) };
            dirty = true;
            RenderStudy();
            MultiStatusText.Text = $"Caso {row.Caso} · {VerdictLabel(verdict)}.";
        });
    }

    private void StudyExport_Click(object sender, RoutedEventArgs e)
    {
        if (savedStudy is { } study) DoExport("estudio-4t-multicilindrico", path => MultiFiles.ExportStudy(path, study));
    }
}
