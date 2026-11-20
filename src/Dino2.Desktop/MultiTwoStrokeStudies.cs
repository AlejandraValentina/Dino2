using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Dino2.Core.Studies;
using Dino2.Core.TwoStroke;
using Dino2.Storage;

namespace Dino2.Desktop;

public partial class MultiTwoStrokeWindow
{
    private sealed record Axis(ComboBox Spec, TextBox Start, TextBox End, TextBox Step);
    private sealed record ObjectiveItem(ObjectiveKind Kind, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record StudyRow(int Caso, int? Puesto, string Valores, double? Objetivo, string Estado);
    private readonly List<Axis> axes = [];
    private MultiTwoStrokeConfiguration? studyBase;
    private MultiTwoStrokeStudy? plan;
    private MultiTwoStrokeStudyRun? study;
    private bool editingStudy;

    private void BuildStudy()
    {
        for (int i = 0; i < 2; i++)
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
            var options = new ComboBox { Width = 272, Name = $"M2StudyAxis{i + 1}", Margin = new Thickness(0, 0, 5, 0) };
            RegisterName(options.Name, options);
            options.Items.Add("Sin parámetro");
            foreach (var option in TwoStrokeParameters.All) options.Items.Add(option);
            options.SelectedIndex = 0;
            panel.Children.Add(options);
            TextBox Field(string label)
            {
                var box = new TextBox { Width = 67, Name = $"M2StudyAxis{i + 1}{label}", Margin = new Thickness(0, 0, 3, 0) };
                RegisterName(box.Name, box);
                AutomationProperties.SetName(box, label);
                box.TextChanged += StudyInput_Changed;
                panel.Children.Add(box);
                return box;
            }
            var axis = new Axis(options, Field("Inicio"), Field("Fin"), Field("Paso"));
            axes.Add(axis);
            options.SelectionChanged += (_, _) =>
            {
                if (!editingStudy && studyBase is not null && options.SelectedItem is TwoStrokeParameterSpec spec)
                {
                    editingStudy = true;
                    axis.Start.Text = axis.End.Text = Text(spec.Read(studyBase.Cylinder));
                    axis.Step.Text = "1";
                    editingStudy = false;
                }
                PrepareStudy();
            };
            StudyAxes.Children.Add(panel);
        }

        Objective.ItemsSource = new ObjectiveItem[]
        {
            new(ObjectiveKind.PeakPower, "Potencia máxima"), new(ObjectiveKind.PeakTorque, "Par máximo"),
            new(ObjectiveKind.MeanPower, "Potencia media en RPM"), new(ObjectiveKind.MeanTorque, "Par medio en RPM"),
            new(ObjectiveKind.HighTorqueLimitedLowLoss, "Par en alta con límite de pérdida en baja"),
            new(ObjectiveKind.MinimumBsfc, "Menor consumo específico calculado")
        };
        Objective.SelectedIndex = 0;
        Workers.ItemsSource = Enumerable.Range(1, StudyPlan.MaximumWorkers);
        Workers.SelectedIndex = 0;
    }

    private void StudyInput_Changed(object sender, RoutedEventArgs e) => PrepareStudy();
    private void PrepareStudy()
    {
        if (initializing || editingStudy) return;
        plan = null;
        var option = (ObjectiveItem)Objective.SelectedItem;
        bool compromise = option.Kind == ObjectiveKind.HighTorqueLimitedLowLoss;
        LowFields.Visibility = compromise ? Visibility.Visible : Visibility.Collapsed;
        if (studyBase is null) { StudyHint.Text = ""; StudyButtons(); return; }
        var rpm = studyBase.Cylinder.Speeds;
        string basis = $"{studyBase.Phases.Count} cilindros · {rpm.Start}–{rpm.End} RPM · ";
        try
        {
            RpmBand? Band(TextBox x, TextBox y) => x.Text.Length == 0 && y.Text.Length == 0 ? null : new(Integer(x), Integer(y));
            var ranges = axes.Where(a => a.Spec.SelectedItem is TwoStrokeParameterSpec).Select(a =>
                new TwoStrokeAxis(((TwoStrokeParameterSpec)a.Spec.SelectedItem).Id, Number(a.Start), Number(a.End), Number(a.Step))).ToArray();
            plan = new(new(studyBase, ranges, new(option.Kind, Band(BandStart, BandEnd),
                compromise ? Band(LowStart, LowEnd) : null, compromise ? Number(LowLoss) : 0)));
            StudyHint.Text = $"{basis}{plan.Grid.Variants.Count} casos · {plan.Grid.OperatingPoints} puntos únicos · costo relativo {plan.EstimatedWork:F1}";
        }
        catch (ArgumentException ex) { StudyHint.Text = basis + ex.Message; }
        StudyButtons();
    }

    private void TakeBase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            studyBase = (sender as Button)?.Tag as string == "A"
                ? original ?? throw new ArgumentException("Todavía no hay una configuración A.") : ReadEditor();
            if (axes[0].Spec.SelectedIndex <= 0)
                axes[0].Spec.SelectedItem = TwoStrokeParameters.Get(TwoStrokeParameter.IntakeLength);
            PrepareStudy();
            StudyPages.SelectedIndex = 0;
        }
        catch (ArgumentException ex) { Message(ex); }
    }

    private async void Study_Click(object sender, RoutedEventArgs e) => await RunStudy();
    internal async Task RunStudy()
    {
        if (plan is not { } next || (study is not null && !CanDiscard())) return;
        int workers = (int)Workers.SelectedItem;
        await Execute((token, progress) => next.Run(workers, token, progress), result =>
        {
            study = result; dirty = true;
            RenderStudy();
            StudyPages.SelectedIndex = 1;
            M2Status.Text = $"{(result.Cancelled ? "Estudio cancelado" : "Estudio terminado")} · {result.Rows.Count} casos.";
        });
    }

    private void RestoreStudy(MultiTwoStrokeStudyRun? saved)
    {
        editingStudy = true;
        study = saved;
        studyBase = saved?.Definition.Base;
        for (int i = 0; i < axes.Count; i++)
        {
            var axis = axes[i];
            var value = saved?.Definition.Axes.ElementAtOrDefault(i);
            if (value is null) axis.Spec.SelectedIndex = 0;
            else axis.Spec.SelectedItem = TwoStrokeParameters.Get(value.Parameter);
            axis.Start.Text = value is null ? "" : Text(value.Start);
            axis.End.Text = value is null ? "" : Text(value.End);
            axis.Step.Text = value is null ? "" : Text(value.Step);
        }
        var objective = saved?.Definition.Objective ?? new(ObjectiveKind.PeakPower);
        Objective.SelectedItem = ((IEnumerable<ObjectiveItem>)Objective.ItemsSource).Single(o => o.Kind == objective.Kind);
        BandStart.Text = objective.Band?.Start.ToString() ?? ""; BandEnd.Text = objective.Band?.End.ToString() ?? "";
        LowStart.Text = objective.LowBand?.Start.ToString() ?? ""; LowEnd.Text = objective.LowBand?.End.ToString() ?? "";
        LowLoss.Text = Text(objective.AllowedLowLossNm);
        Workers.SelectedItem = saved?.Workers ?? 1;
        editingStudy = false;
        PrepareStudy();
        RenderStudy();
    }

    private void RenderStudy()
    {
        if (study is not { } data)
        {
            StudyRows.ItemsSource = null; StudyNote.Text = "";
            RenderSelection();
            return;
        }

        int? id = (StudyRows.SelectedItem as StudyRow)?.Caso;
        var grid = new MultiTwoStrokeStudy(data.Definition);
        var rank = grid.Rank(data).Select((p, i) => (p.Id, Position: i + 1, p.Objective.Score)).ToDictionary(v => v.Id);
        var rows = data.Rows.Select(r => new StudyRow(r.Id, rank.TryGetValue(r.Id, out var v) ? v.Position : null,
            r.Id == 0 ? "Base" : string.Join(" · ", grid.Grid.Variants[r.Id].Values.Select((value, i) =>
                $"{TwoStrokeParameters.Get(data.Definition.Axes[i].Parameter)} = {value:G6}")),
            v.Score, r.Error ?? (r.Status == StudyStatus.Completed ? "Periódico" : r.Status.ToString())))
            .OrderBy(v => v.Caso == 0 ? 0 : v.Puesto ?? int.MaxValue).ToArray();
        StudyRows.ItemsSource = rows;
        StudyRows.SelectedItem = rows.FirstOrDefault(p => p.Caso == id) ?? rows.FirstOrDefault(p => p.Puesto == 1) ?? rows.FirstOrDefault();
        StudyNote.Text = $"Clasificación de la grilla · objetivo [{data.Definition.Objective.Unit}]." +
            (data.Check is { } check ? $" Caso {check.CandidateId} · {TwoStrokeWindow.StudyLabel(check.Verdict)} · banda {TwoStrokeWindow.F(check.ObservedBand)}. " +
                (check.Levels.LastOrDefault()?.Error ?? "") : "");
        RenderSelection();
    }

    private void StudySelection_Changed(object sender, SelectionChangedEventArgs e) => RenderSelection();
    private void RenderSelection()
    {
        if (initializing) return;
        var selected = StudyRows.SelectedItem as StudyRow;
        var data = study?.Rows.SingleOrDefault(r => r.Id == selected?.Caso)?.Curve;
        var basis = study?.Rows.FirstOrDefault(r => r.Id == 0)?.Curve;
        IReadOnlyList<PlotLine> RenderValue(Func<MultiTwoStrokePoint, double> value)
        {
            var result = new List<PlotLine>();
            if (basis is not null) result.Add(Curve("Base", basis, value, Colors[1]));
            if (data is not null && selected?.Caso > 0) result.Add(Curve($"Caso {selected.Caso}", data, value, Colors[0]));
            return result;
        }
        StudyPower.SetLines(RenderValue(p => p.ShaftPowerW / 1000));
        StudyTorque.SetLines(RenderValue(p => p.ShaftTorqueNm));
        StudyRunRows.ItemsSource = data?.Runs.Select(r => new
        {
            RPM = r.Rpm, Estado = State(r.Status), Cigüeñal_kW = r.Point?.ShaftPowerW / 1000,
            Par_Nm = r.Point?.ShaftTorqueNm, Ciclos = r.Convergence?.Cycles, Residual = r.Convergence?.Residual,
            Fresca_total_mg = r.Point?.Cylinders.Sum(p => p.Point.FreshTrappedKg) * 1e6, Diagnóstico = r.Failure
        }).ToArray();
        StudyButtons();
    }

    private void StudyButtons()
    {
        if (initializing) return;
        bool ready = operation is null;
        int id = (StudyRows.SelectedItem as StudyRow)?.Caso ?? 0;
        M2StudyRun.IsEnabled = ready && plan is not null;
        M2StudyExport.IsEnabled = ready && study is not null;
        M2Apply.IsEnabled = ready && id > 0 && study?.Rows.Any(r => r.Id == id && r.Status == StudyStatus.Completed) == true;
        M2StudyCheck.IsEnabled = M2Apply.IsEnabled && study?.Rows.Any(r => r.Id == 0 && r.Status == StudyStatus.Completed) == true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (study is not { } data || StudyRows.SelectedItem is not StudyRow row) return;
        var grid = new MultiTwoStrokeStudy(data.Definition);
        original ??= grid.Variant(0);
        Populate(grid.Variant(row.Caso));
        Edited();
        Pages.SelectedIndex = 0;
        M2Status.Text = $"Caso {row.Caso} aplicado como B.";
    }

    private async void StudyCheck_Click(object sender, RoutedEventArgs e)
    {
        if (study is not { } data || StudyRows.SelectedItem is not StudyRow row) return;
        var grid = new MultiTwoStrokeStudy(data.Definition);
        await Execute((token, progress) => grid.Verify(row.Caso, token, progress), check =>
        {
            study = data with { Check = check }; dirty = true;
            RenderStudy();
            M2Status.Text = $"Caso {row.Caso} · {TwoStrokeWindow.StudyLabel(check.Verdict)}.";
        });
    }

    private void StudyExport_Click(object sender, RoutedEventArgs e)
    {
        if (study is { } data) Export("estudio-2t-multicilindrico", path => TwoStrokeFiles.ExportMultiStudy(path, data));
    }
}
