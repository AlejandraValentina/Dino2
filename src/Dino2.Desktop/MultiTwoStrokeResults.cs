using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dino2.Core.Studies;
using Dino2.Core.TwoStroke;

namespace Dino2.Desktop;

public partial class MultiTwoStrokeWindow
{
    private static readonly Brush[] Colors = [Brushes.SteelBlue, Brushes.IndianRed, Brushes.SeaGreen, Brushes.DarkViolet];
    private MultiTwoStrokeSweep? CycleCurve => CycleSource.SelectedIndex == 1 ? baseline : curve;
    private MultiTwoStrokePoint? CyclePoint => CycleRpm.SelectedItem is int rpm ? CycleCurve?.Runs.SingleOrDefault(r => r.Rpm == rpm)?.Point : null;
    private sealed record DiffRow(int RPM, double? Delta_potencia_W, double? Delta_par_Nm, double? Delta_fresca_mg,
        string Resolución, double? Banda_Nm);

    private static string State(TwoStrokeRunStatus status) => status switch
    {
        TwoStrokeRunStatus.Periodic => "Periódico",
        TwoStrokeRunStatus.NonConverged => "Sin periodicidad",
        TwoStrokeRunStatus.OutsideDomain => "Fuera de dominio",
        _ => "Fallo numérico"
    };

    private static PlotLine Curve(string label, MultiTwoStrokeSweep run, Func<MultiTwoStrokePoint, double> value,
        Brush color, bool dashed = false) => new(label,
        run.Runs.Select(r => new Point(r.Rpm, r.Point is { } p ? value(p) : double.NaN)).ToArray(), color, dashed);

    private void Curves_Changed(object sender, RoutedEventArgs e)
    {
        if (!initializing) RenderCurves();
    }

    private void RenderCurves()
    {
        IReadOnlyList<PlotLine> Lines(Func<MultiTwoStrokePoint, bool, double> total, Func<TwoStrokePoint, double> cylinder)
        {
            if (ByCylinder.IsChecked == true && (curve ?? baseline) is { } selected)
                return Enumerable.Range(0, selected.Configuration.Phases.Count).Select(i =>
                    Curve($"C{i + 1} · indicado", selected, p => cylinder(p.Cylinders[i].Point), Colors[i])).ToArray();
            var lines = new List<PlotLine>();
            foreach (var (result, label, brush) in new[] { (baseline, "A", Colors[1]), (curve, "Actual / B", Colors[0]) })
            {
                if (result is null) continue;
                lines.Add(Curve(label + " · cigüeñal estimado", result, p => total(p, false), brush));
                lines.Add(Curve(label + " · indicado", result, p => total(p, true), brush, true));
            }
            return lines;
        }
        PowerPlot.SetLines(Lines((p, indicated) => (indicated ? p.IndicatedPowerW : p.ShaftPowerW) / 1000, p => p.IndicatedPowerW / 1000));
        TorquePlot.SetLines(Lines((p, indicated) => indicated ? p.IndicatedTorqueNm : p.ShaftTorqueNm, p => p.IndicatedTorqueNm));
    }

    private void Render()
    {
        if (initializing) return;
        RenderCurves();
        RunRows.ItemsSource = (curve ?? baseline)?.Runs.Select(r => new
        {
            RPM = r.Rpm, Estado = State(r.Status), Indicada_kW = r.Point?.IndicatedPowerW / 1000,
            Cigüeñal_kW = r.Point?.ShaftPowerW / 1000, Par_Nm = r.Point?.ShaftTorqueNm,
            IMEP_bar = r.Point?.ImepPa / 1e5, BMEP_bar = r.Point?.BmepPa / 1e5, BSFC_g_kWh = r.Point?.BsfcGPerKwh,
            Ciclos = r.Convergence?.Cycles, Periodicidad = r.Convergence?.Residual, Masa_mg = r.Point?.Balance.MassKg * 1e6,
            Energía_J = r.Point?.Balance.EnergyJ, Dominante = r.Convergence?.Dominant, Diagnóstico = r.Failure
        }).ToArray();
        int? previous = (DiffRows.SelectedItem as DiffRow)?.RPM;
        var differences = baseline is not null && curve is not null && CurrentRevision(baseline) && CurrentRevision(curve)
            ? MultiTwoStrokeComparison.Compare(baseline, curve) : [];
        var rows = differences.Select(r => new DiffRow(r.Rpm, r.DeltaPowerW, r.DeltaTorqueNm, r.DeltaFreshTrappedKg * 1e6,
            r.Rpm == contrast?.Rpm ? TwoStrokeWindow.StudyLabel(contrast.Check.Verdict) : "Sin contraste",
            r.Rpm == contrast?.Rpm ? contrast.Check.ObservedBand : null)).ToArray();
        DiffRows.ItemsSource = rows;
        DiffRows.SelectedItem = rows.FirstOrDefault(r => r.RPM == previous) ?? rows.FirstOrDefault(r => r.Delta_par_Nm is not null);
        ResetRpm();
        Buttons();
    }

    private void Source_Changed(object sender, SelectionChangedEventArgs e) { if (!initializing) ResetRpm(); }
    private void Cycle_Changed(object sender, SelectionChangedEventArgs e) { if (!initializing && !changingRpm) RenderCycle(); }
    private void ResetRpm()
    {
        int? previous = CycleRpm.SelectedItem as int?;
        changingRpm = true;
        int[] values = CycleCurve?.Runs.Where(r => r.Point is not null).Select(r => r.Rpm).ToArray() ?? [];
        CycleRpm.ItemsSource = values;
        if (previous is int rpm && values.Contains(rpm)) CycleRpm.SelectedItem = rpm;
        else if (values.Length > 0) CycleRpm.SelectedIndex = 0;
        changingRpm = false;
        RenderCycle();
    }

    private void RenderCycle()
    {
        var p = CyclePoint;
        CylinderRows.ItemsSource = p?.Cylinders.Select(c => new
        {
            Cilindro = c.Number, Desfase = c.PhaseDeg, Indicada_kW = c.Point.IndicatedPowerW / 1000,
            Cigüeñal_kW = c.Point.ShaftPowerW / 1000, Trabajo_cilindro_J = c.Point.CylinderWorkJ,
            Trabajo_cárter_J = c.Point.CrankcaseWorkJ, Suministro = c.Point.DeliveryRatio,
            Atrapamiento = c.Point.TrappingEfficiency, Barrido = c.Point.ScavengingEfficiency,
            Carga = c.Point.ChargingEfficiency, Fresca_atrapada_mg = c.Point.FreshTrappedKg * 1e6,
            Fresca_escapada_neta_mg = c.Point.NetFreshExhaustKg * 1e6, Residual_mg = c.Point.ResidualMassKg * 1e6,
            Fracción_residual = c.Point.ResidualMassKg / (c.Point.ResidualMassKg + c.Point.FreshTrappedKg)
        }).ToArray();
        List<PlotLine> Trace(Func<TwoStrokeSample, double> f) => p?.Cylinders.Select((m, i) =>
            new PlotLine($"C{m.Number}", m.Point.Trace.Select(s => new Point(s.AngleDeg, f(s))).ToArray(), Colors[i])).ToList() ?? [];
        void Add(List<PlotLine> lines, string name, Brush color, Func<MultiTwoStrokeSample, double?> f)
        {
            if (p is null) return;
            var values = p.Cycle.Select(s => new Point(s.AngleDeg, f(s) ?? double.NaN)).ToArray();
            if (values.Any(v => double.IsFinite(v.Y))) lines.Add(new(name, values, color, true));
        }
        List<PlotLine> a = Trace(s => s.IntakeValvePressurePa / 1e5);
        List<PlotLine> b = Trace(s => s.ExhaustPortPressurePa / 1e5);
        Add(a, "Unión", Brushes.Black, s => s.IntakeJunctionPa / 1e5);
        Add(a, "Tronco junto a unión", Brushes.SaddleBrown, s => s.IntakeTrunkPa / 1e5);
        Add(b, "Unión", Brushes.Black, s => s.ExhaustJunctionPa / 1e5);
        Add(b, "Expansión junto a unión", Brushes.SaddleBrown, s => s.ExhaustTrunkPa / 1e5);
        IntakePlot.SetLines(a);
        ExhaustPlot.SetLines(b);
        CylinderPlot.SetLines(Trace(s => s.CylinderPressurePa / 1e5));
        CasePlot.SetLines(Trace(s => s.CrankcasePressurePa / 1e5));
        FlowPlot.SetLines(Trace(s => s.TransferKgPerS * 1000));
        GasTorque.SetLines(p is null ? [] : [new("Indicado de gases", p.Cycle.Select(s => new Point(s.AngleDeg, s.GasTorqueNm)).ToArray(), Colors[0])]);
        Buttons();
    }

    private void Diff_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (initializing) return;
        ContrastText.Text = DiffRows.SelectedItem is DiffRow row && contrast?.Rpm == row.RPM
            ? string.Join(" · ", contrast.Check.Levels.Select(l => l.Error is null ? $"×{l.Scale}: {l.Advantage:G6} N·m" : $"×{l.Scale}: {l.Error}"))
            : "";
        Buttons();
    }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (baseline is not { } a || curve is not { } b || DiffRows.SelectedItem is not DiffRow row ||
            !CurrentRevision(a) || !CurrentRevision(b)) return;
        await Execute((token, progress) => MultiTwoStrokeComparison.Verify(a.Configuration, b.Configuration, row.RPM, token, progress), check =>
        {
            contrast = new(row.RPM, check);
            dirty = true;
            M2Status.Text = $"{row.RPM} RPM · {TwoStrokeWindow.StudyLabel(check.Verdict)}.";
        });
    }

    private static bool CurrentRevision(MultiTwoStrokeSweep? result) => result is not null &&
        result.Provenance?.NumericalRevision == result.Configuration.Cylinder.Revision + "-network-recovery-v1";

    private void Buttons()
    {
        if (initializing) return;
        bool ready = operation is null;
        M2Keep.IsEnabled = ready && curve is not null;
        M2Export.IsEnabled = ready && (curve ?? baseline) is not null;
        M2Trace.IsEnabled = ready && CyclePoint is not null;
        M2Cylinders.IsEnabled = ready && CycleCurve?.Runs.Any(r => r.Point is not null) == true;
        M2Compare.IsEnabled = ready && DiffRows.SelectedItem is DiffRow { Delta_par_Nm: not null };
        M2ExportAB.IsEnabled = ready && baseline is not null && curve is not null;
        StudyButtons();
    }
}
