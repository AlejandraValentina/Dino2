using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dino2.Core.Multicylinder;
using Dino2.Core.Studies;

namespace Dino2.Desktop;

public partial class MulticylinderWindow
{
    private static string StatusLabel(MultiStatus status) => status switch
    {
        MultiStatus.Converged => "Periódico",
        MultiStatus.NotPeriodic => "Sin periodicidad",
        MultiStatus.OutsideDomain => "Fuera de dominio",
        _ => "Fallo numérico"
    };

    private static string VerdictLabel(StudyVerdict verdict) => verdict switch
    {
        StudyVerdict.Superior => "B superior en la resolución verificada",
        StudyVerdict.Inferior => "B inferior en la resolución verificada",
        StudyVerdict.Indistinguishable => "Numéricamente indistinguibles",
        StudyVerdict.DiscretizationSensitive => "Sensible a discretización",
        _ => "Sin contraste concluyente"
    };

    private MultiResult? SelectedCurve => CycleSource.SelectedIndex == 1 ? originalResult : currentResult;
    private static bool CurrentRevision(MultiResult? result) => result is not null &&
        result.Provenance?.NumericalRevision == result.Configuration.Cylinder.ModelIdentifier + "-network-recovery-v1";
    private IReadOnlyList<MultiDifference> ComparisonRows() => originalResult is null || currentResult is null ||
        !CurrentRevision(originalResult) || !CurrentRevision(currentResult) ? [] :
        differences ?? MultiComparison.Compare(originalResult, currentResult);

    private sealed record DifferenceDisplay(int RPM, double? Delta_potencia_W, double? Delta_par_Nm,
        double? Delta_llenado, string Resolución, double? Banda_W);

    private void CylinderCurve_Changed(object sender, RoutedEventArgs e) => Render();

    private void Render()
    {
        if (!initialized) return;
        var pairs = new[] { (Result: currentResult, Name: "Actual / B", Color: Brushes.SteelBlue),
            (Result: originalResult, Name: "Original A", Color: Brushes.IndianRed) };
        void Curve(ScientificPlot plot, Func<MultiMetrics, bool, double> read,
            Func<CylinderContribution, double> contribution)
        {
            var lines = new List<PlotLine>();
            if (CylinderCurveMode.IsChecked == true && (currentResult ?? originalResult) is { } selectedCurve)
            {
                for (int i = 0; i < selectedCurve.Configuration.Phases.Count; i++)
                    lines.Add(new($"C{i + 1} · indicado", selectedCurve.Points.Select(p => new Point(p.Rpm,
                        p.Metrics is null ? double.NaN : contribution(p.Cylinders[i]))).ToArray(), CylinderColors[i]));
                plot.SetLines(lines);
                return;
            }
            foreach (var (result, name, color) in pairs)
            {
                if (result is null) continue;
                foreach (bool indicated in new[] { false, true })
                    lines.Add(new(name + (indicated ? " · indicado" : " · cigüeñal estimado"),
                        result.Points.Select(p => new Point(p.Rpm, p.Metrics is { } m ? read(m, indicated) : double.NaN)).ToArray(),
                        color, indicated));
            }
            plot.SetLines(lines);
        }
        Curve(MultiPower, (m, indicated) => (indicated ? m.IndicatedPowerW : m.BrakePowerW) / 1000,
            c => c.IndicatedPowerW / 1000);
        Curve(MultiTorque, (m, indicated) => indicated ? m.IndicatedTorqueNm : m.BrakeTorqueNm,
            c => c.NetWorkJ / (4 * Math.PI));
        MultiRows.ItemsSource = (currentResult ?? originalResult)?.Points.Select(p => new
        {
            RPM = p.Rpm, Estado = StatusLabel(p.Status), Potencia_indicada_kW = p.Metrics?.IndicatedPowerW / 1000,
            Potencia_cigüeñal_kW = p.Metrics?.BrakePowerW / 1000, Par_cigüeñal_Nm = p.Metrics?.BrakeTorqueNm,
            IMEP_bar = p.Metrics?.ImepPa / 1e5, BMEP_bar = p.Metrics?.BmepPa / 1e5, Llenado = p.Metrics?.VolumetricEfficiency,
            Ciclos = p.Cycles, Error_periódico = p.PeriodicError, Balance_masa_mg = p.Balance?.MassKg * 1e6,
            Balance_energía_J = p.Balance?.TotalEnergyJ, Diagnóstico = p.Detail
        }).ToArray();
        int? selected = (DifferenceRows.SelectedItem as DifferenceDisplay)?.RPM;
        var rows = ComparisonRows().Select(d => new DifferenceDisplay(d.Rpm, d.PowerDifferenceW, d.TorqueDifferenceNm,
            d.FillingDifference, VerdictLabel(d.Verdict), d.ObservedBandW)).ToArray();
        DifferenceRows.ItemsSource = rows;
        DifferenceRows.SelectedItem = rows.FirstOrDefault(d => d.RPM == selected) ?? rows.FirstOrDefault(d => d.Delta_potencia_W.HasValue);
        ResetCycleSpeeds();
        RefreshButtons();
    }

    private void ResetCycleSpeeds()
    {
        int? old = MultiRpm.SelectedItem as int?;
        renderingCycle = true;
        int[] speeds = SelectedCurve?.Points.Where(p => p.Metrics is not null).Select(p => p.Rpm).ToArray() ?? [];
        MultiRpm.ItemsSource = speeds;
        if (old is int rpm && speeds.Contains(rpm)) MultiRpm.SelectedItem = rpm;
        else if (speeds.Length > 0) MultiRpm.SelectedIndex = 0;
        renderingCycle = false;
        RenderCycle();
    }

    private void Cycle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || renderingCycle) return;
        if (sender == CycleSource) ResetCycleSpeeds(); else RenderCycle();
    }

    private void RenderCycle()
    {
        var point = MultiRpm.SelectedItem is int rpm ? SelectedCurve?.Points.SingleOrDefault(p => p.Rpm == rpm) : null;
        MultiTraceExport.IsEnabled = cancellation is null && point?.Metrics is not null;
        MultiCylinderExport.IsEnabled = cancellation is null && SelectedCurve?.Points.Any(p => p.Metrics is not null) == true;
        if (point?.Metrics is null)
        {
            CylinderRows.ItemsSource = null;
            foreach (var plot in new[] { CylinderPressure, IntakePressure, ExhaustPressure, ShaftTorque }) plot.SetLines([]);
            CycleNotice.Text = "Seleccioná una curva con puntos periódicos.";
            return;
        }
        CycleNotice.Text = $"{point.Cylinders.Count} cilindros · {point.Cycles} ciclos · error global {point.PeriodicError:E2}" +
            (point.Cylinders.Any(c => c.BurnCorrelationExtrapolated) ? " · combustión escalada fuera de su rango." : "");
        CylinderRows.ItemsSource = point.Cylinders.Select(c => new
        {
            Cilindro = c.CylinderNumber, Desfase_grados = c.FiringOffsetDeg, Trabajo_neto_J = c.NetWorkJ,
            Bombeo_J = c.PumpingWorkJ, Fricción_J = c.FrictionWorkJ, Indicada_kW = c.IndicatedPowerW / 1000,
            Cigüeñal_kW = c.BrakePowerW / 1000, Aire_fresco_al_cierre_mg = c.FreshAirAtIntakeCloseKg * 1e6,
            Admisión_bruta_mg = c.GrossIntakeMassKg * 1e6, Admisión_neta_mg = c.NetIntakeMassKg * 1e6,
            Fracción_residual_encendido = c.ResidualAtSpark, Pmáx_bar = c.PeakPressurePa / 1e5,
            Tmáx_K = c.PeakTemperatureK, Error_periódico = c.PeriodicError
        }).ToArray();
        List<PlotLine> Traces(Func<Dino2.Core.FourStroke.CycleSample, double> get) => point.Cylinders.Select((c, i) =>
            new PlotLine($"C{c.CylinderNumber}", c.Trace.Select(s => new Point(s.AngleDeg, get(s) / 1e5)).ToArray(),
                CylinderColors[i])).ToList();
        PlotLine Phase(string label, Brush color, Func<ManifoldSample, double> get, bool dash = false) =>
            new(label, point.Cycle.Select(s => new Point(s.CrankAngleDeg, get(s))).ToArray(), color, dash);
        CylinderPressure.SetLines(Traces(s => s.PressurePa));
        var inlet = Traces(s => s.IntakePortPressurePa);
        var outlet = Traces(s => s.ExhaustPortPressurePa);
        if (SelectedCurve!.Configuration.Manifolds is not null)
        {
            inlet.Add(Phase("Unión", Brushes.DimGray, m => m.IntakeJunctionPa / 1e5, true));
            inlet.Add(Phase("Tronco junto a unión", Brushes.SaddleBrown, m => m.IntakeTrunkPa / 1e5, true));
            outlet.Add(Phase("Unión", Brushes.DimGray, m => m.ExhaustJunctionPa / 1e5, true));
            outlet.Add(Phase("Tronco junto a unión", Brushes.SaddleBrown, m => m.ExhaustTrunkPa / 1e5, true));
        }
        IntakePressure.SetLines(inlet);
        ExhaustPressure.SetLines(outlet);
        ShaftTorque.SetLines([Phase("Gas indicado total", Brushes.SteelBlue, m => m.IndicatedTorqueNm)]);
    }

    private void Difference_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        var selected = DifferenceRows.SelectedItem as DifferenceDisplay;
        var row = ComparisonRows().FirstOrDefault(r => r.Rpm == selected?.RPM);
        ResolutionText.Text = row?.Levels.Count > 0 ? string.Join(" · ", row.Levels.Select(level =>
            level.Error is null ? $"×{level.Scale}: {level.Advantage:G6} W" : $"×{level.Scale}: {level.Error}")) : "";
        RefreshButtons();
    }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (originalResult is not { } a || currentResult is not { } b ||
            DifferenceRows.SelectedItem is not DifferenceDisplay row || !CurrentRevision(a) || !CurrentRevision(b)) return;
        var before = ComparisonRows();
        await ExecuteAsync((token, progress) => MultiComparison.Verify(a, b, row.RPM, token, progress), result =>
        {
            var checkedRow = result.Single(d => d.Rpm == row.RPM);
            differences = before.Select(d => d.Rpm == row.RPM ? checkedRow : d).ToArray();
            dirty = true;
            MultiStatusText.Text = $"{row.RPM} RPM · {VerdictLabel(checkedRow.Verdict)}.";
        });
    }

    private void RefreshButtons()
    {
        if (!initialized) return;
        bool ready = cancellation is null;
        MultiKeep.IsEnabled = ready && currentResult is not null;
        MultiExport.IsEnabled = ready && (originalResult is not null || currentResult is not null);
        MultiCheck.IsEnabled = ready && DifferenceRows.SelectedItem is DifferenceDisplay { Delta_potencia_W: not null };
        MultiABExport.IsEnabled = ready && CurrentRevision(originalResult) && CurrentRevision(currentResult);
        MultiTraceExport.IsEnabled = ready && MultiRpm.SelectedItem is int;
        MultiCylinderExport.IsEnabled = ready && SelectedCurve?.Points.Any(p => p.Metrics is not null) == true;
        RefreshStudyButtons();
    }
}
