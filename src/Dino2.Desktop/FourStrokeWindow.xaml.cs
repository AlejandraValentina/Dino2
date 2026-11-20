using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;
using Dino2.Storage;
using Microsoft.Win32;

namespace Dino2.Desktop;

public partial class FourStrokeWindow : Window
{
    private readonly Dictionary<string, TextBox> fields = [];
    private FourStrokeConfiguration? original;
    private FourStrokeConfiguration editorTemplate = ThermochemicalOptions.EquilibriumExample;
    private EvidenceEditor evidenceEditor = null!;
    private bool populating, dirty, busy;
    private CancellationTokenSource? cancellation;
    private readonly Func<bool>? confirmDiscard;
    private FourStrokeResult? currentResult;
    internal DuctResolution? Resolution { get; private set; }
    internal FourStrokeResult? CurrentResult
    {
        get => currentResult;
        private set { currentResult = value; Resolution = null; }
    }
    internal FourStrokeResult? OriginalResult { get; private set; }

    private TwoStrokeWindow? twoStrokeWindow;
    private MainWindow? ottoWindow;
    private MulticylinderWindow? multicylinderWindow;

    private void Multicylinder_Click(object sender, RoutedEventArgs e)
    {
        if (multicylinderWindow is not null)
        {
            multicylinderWindow.Activate();
            return;
        }
        var child = new MulticylinderWindow { Owner = this };
        multicylinderWindow = child;
        child.Closed += (_, _) => multicylinderWindow = null;
        child.Show();
    }

    private void TwoStroke_Click(object sender, RoutedEventArgs e)
    {
        if (twoStrokeWindow is not null)
        {
            twoStrokeWindow.Show();
            twoStrokeWindow.Activate();
            return;
        }
        var child = new TwoStrokeWindow { Owner = this };
        twoStrokeWindow = child;
        child.Closed += (_, _) => twoStrokeWindow = null;
        child.Show();
    }

    public FourStrokeWindow() : this(null) { }
    internal FourStrokeWindow(Func<bool>? confirmDiscard)
    {
        this.confirmDiscard = confirmDiscard;
        InitializeComponent();
        DesktopLayout.Fit(this);
        Studies.Configure(() => ReadConfiguration(), () => original, ApplyStudyVariant);
        PowerChart.Caption = "Potencia [kW]"; TorqueChart.Caption = "Par [N·m]";
        PressureChart.Caption = "Presión absoluta [bar]"; PressureChart.XLabel = "Ángulo [°]";
        TemperatureChart.Caption = "Temperatura [K]"; TemperatureChart.XLabel = "Ángulo [°]";
        PortChart.Caption = "Presión junto a la válvula [bar]"; PortChart.XLabel = "Ángulo [°]";
        FlowChart.Caption = "Caudal hacia el cilindro [g/s]"; FlowChart.XLabel = "Ángulo [°]";
        PvChart.Caption = "Presión absoluta [bar]"; PvChart.XLabel = "Volumen [cm³]";
        DesktopLayout.FormatGrid(ResultGrid);
        DesktopLayout.FormatGrid(ComparisonGrid);
        foreach (var group in EngineParameter.All.GroupBy(p => p.Group))
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            foreach (var parameter in group)
            {
                var input = new TextBox { Name = "Field_" + parameter.Key };
                var label = new Label { Content = new TextBlock { Text = parameter.Label, TextWrapping = TextWrapping.Wrap }, Target = input,
                    Padding = new Thickness(0, 10, 0, 4) };
                RegisterName("Label_"+parameter.Key,label);
                RegisterName(input.Name, input);
                System.Windows.Automation.AutomationProperties.SetName(input, parameter.Label);
                fields.Add(parameter.Key, input); panel.Children.Add(label); panel.Children.Add(input);
                input.TextChanged += (_, _) =>
                {
                    if (populating) return;
                    evidenceEditor.InvalidateCalibration(parameter.Group);
                    dirty = true; CurrentResult = null;
                    Title = "Dino2 · configuración modificada *";
                    RenderResults();
                    Status.Text = "Configuración modificada. A se conserva; vuelva a simular para obtener la curva actual.";
                };
            }
            ParameterTabs.Items.Add(new TabItem { Header = group.Key, Content = new ScrollViewer
                { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        }
        evidenceEditor = new EvidenceEditor(() =>
        {
            if(populating)return;
            dirty=true;CurrentResult=null;RenderResults();Status.Text="Evidencia modificada; vuelva a simular. La procedencia declarada no certifica validación.";
        });
        ParameterTabs.Items.Add(evidenceEditor.Tab);
        Populate(ThermochemicalOptions.EquilibriumExample);
        Closing += (_, e) =>
        {
            if (Studies.IsBusy) { Studies.Cancel(); e.Cancel = true; Status.Text = "Cancelando el estudio."; }
            else if (busy) { cancellation?.Cancel(); e.Cancel = true; Status.Text = "Cancelando; cierre la ventana cuando termine el cálculo."; }
            else if (!ConfirmDiscard() || !Studies.ConfirmDiscard(this, confirmDiscard) ||
                twoStrokeWindow?.CanCloseFromOwner() == false || ottoWindow?.CanCloseFromOwner() == false ||
                multicylinderWindow?.CanCloseFromOwner() == false)
                e.Cancel = true;
        };
    }

    private static int ModelIndex(FourStrokeConfiguration configuration) => configuration.Ducts is { } ducts
        ? (ducts.Scheme == DuctScheme.Muscl ? 7 : 5) + (configuration.Thermochemistry?.BurnLaw == BurnLaw.PrescribedWiebe ? 0 : 1)
        : configuration.Thermochemistry switch
    {
        null => 0,
        { UsesEquilibrium: true, BurnLaw: BurnLaw.PrescribedWiebe } => 4,
        { UsesEquilibrium: true } => 3,
        { BurnLaw: BurnLaw.PrescribedWiebe } => 1,
        _ => 2
    };

    private void Populate(FourStrokeConfiguration configuration)
    {
        populating = true;
        editorTemplate = configuration;
        PhysicsModel.SelectedIndex = ModelIndex(configuration);
        foreach (var p in EngineParameter.All) fields[p.Key].Text = (p.Get(configuration) * p.Scale).ToString("R", CultureInfo.InvariantCulture);
        foreach(var parameter in EngineParameter.All.Where(p=>p.Group=="Comb. v2")) fields[parameter.Key].IsEnabled=configuration.Thermochemistry?.BurnLaw==BurnLaw.ConditionScaledWiebe;
        foreach (var parameter in EngineParameter.All.Where(p => p.Group == "Conductos"))
            fields[parameter.Key].IsEnabled = configuration.Ducts is not null;
        fields["LocalTolerance"].IsEnabled = configuration.Ducts is null;
        void Label(string key,string text)=>((TextBlock)((Label)FindName("Label_"+key)).Content).Text=text;
        Label("Duration",configuration.Thermochemistry?.BurnLaw==BurnLaw.ConditionScaledWiebe?"Duración de referencia [ms]":"Duración prescrita [ms]");
        Label("BurnEfficiency",configuration.Thermochemistry is null?"Fracción de energía liberada [0–1]":"Conversión de combustible [0–1]");
        fields["Lhv"].IsReadOnly=configuration.Thermochemistry is not null;
        fields["Lhv"].ToolTip=configuration.Thermochemistry is null?"PCI prescrito del modelo de referencia":"PCI del iso-octano gaseoso derivado de entalpías NASA; no se añade de nuevo al balance total.";
        evidenceEditor.Populate(configuration.Thermochemistry);
        ModelNotice.Text = configuration.Ducts is not null
            ? "Conductos según Euler 1D, con transición lineal de diámetro. Paredes adiabáticas y sin rozamiento. Simulación numérica sin validación experimental."
            : configuration.Thermochemistry is null
            ? "Referencia v1: pseudoaire con cp(T), Wiebe prescrita, depósitos sin ondas y pérdidas empíricas. Sin validación experimental de motor."
            : configuration.Thermochemistry.UsesEquilibrium
                ? "Iso-octano vaporizado. Equilibrio reducido CO/H₂ durante combustión y expansión cerrada; composición congelada desde apertura de escape. Wiebe y pérdidas requieren identificación. Sin validación experimental de motor."
                : "Iso-octano vaporizado y productos congelados: conserva especies y energía química, omite disociación y ondas. Wiebe prescrita o escalada; no predice propagación de llama. Evidencia/calibración declaradas por el usuario; sin validación independiente del motor.";
        populating = false;
        CurrentResult = null;
        RenderResults();
    }

    private FourStrokeConfiguration ReadConfiguration(bool validate = true)
    {
        var config = editorTemplate;
        foreach (var parameter in EngineParameter.All)
        {
            var input = fields[parameter.Key];
            // IsEnabled also reflects the container lock. The applicability rules are based on the model snapshot.
            if ((parameter.Group == "Conductos" && config.Ducts is null) ||
                (parameter.Group == "Comb. v2" && config.Thermochemistry?.BurnLaw != BurnLaw.ConditionScaledWiebe) ||
                (parameter.Key == "LocalTolerance" && config.Ducts is not null) ||
                (parameter.Key == "Lhv" && config.Thermochemistry is not null)) continue;
            if (!double.TryParse(input.Text.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                throw new ArgumentException("Número inválido en " + parameter.Label);
            if (parameter.Integer && (value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue))
                throw new ArgumentException("Se requiere un entero en " + parameter.Label);
            if (value != parameter.Get(config) * parameter.Scale)
                config = parameter.Set(config, value / parameter.Scale);
        }
        if(config.Thermochemistry is not null) config=config with{Thermochemistry=evidenceEditor.Read(config.Thermochemistry)};
        if(validate) config.Validate();
        return config;
    }

    internal async Task SimulateAsync()
    {
        if (busy) return;
        try
        {
            var config = ReadConfiguration(); // Immutable input captured before leaving the UI thread.
            cancellation = new CancellationTokenSource();
            busy = true; Commands.IsEnabled = ParameterTabs.IsEnabled = PhysicsModel.IsEnabled = false; CancelButton.IsEnabled = true;
            CurrentResult = null; RenderResults();
            var token = cancellation.Token;
            var progress = new Progress<string>(message => { if (busy && !token.IsCancellationRequested) Status.Text = message; });
            if (original is not null && OriginalResult is null)
                OriginalResult = await Task.Run(() => EngineSimulation.Run(original, token, progress), token);
            CurrentResult = await Task.Run(() => EngineSimulation.Run(config, token, progress), token);
            RenderResults();
            Status.Text = $"{CurrentResult.Points.Count} RPM · {CurrentResult.ElapsedMilliseconds:F0} ms · {CurrentResult.Model}. Sin validación experimental de motor.";
        }
        catch (OperationCanceledException) { Status.Text = "Simulación cancelada. A se conserva; no se publican resultados parciales."; }
        catch (Exception ex) when (Expected(ex)) { Status.Text = "No se completó la simulación: " + ex.Message; }
        finally
        {
            busy = false; Commands.IsEnabled = ParameterTabs.IsEnabled = PhysicsModel.IsEnabled = true; CancelButton.IsEnabled = false;
            cancellation?.Dispose(); cancellation = null;
            RenderResults();
            KeepOriginalButton.IsEnabled = ExportButton.IsEnabled = CurrentResult is not null;
        }
    }
    private async void Simulate_Click(object sender, RoutedEventArgs e) => await SimulateAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => cancellation?.Cancel();

    private void PhysicsModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if(populating || evidenceEditor is null || PhysicsModel.SelectedIndex<0) return;
        try
        {
            var config=ReadConfiguration(false);
            bool ducts = PhysicsModel.SelectedIndex >= 5;
            var path = config.Ducts ?? GasPath.Example;
            config = config with { Ducts = ducts ? path with
            {
                Scheme = PhysicsModel.SelectedIndex >= 7 ? DuctScheme.Muscl : DuctScheme.FirstOrder
            } : null };
            if(PhysicsModel.SelectedIndex==0) config=config with{Thermochemistry=null};
            else
            {
                bool wasLegacy=config.Thermochemistry is null;
                var settings=config.Thermochemistry??ThermochemicalOptions.Default;
                settings=settings with{BurnLaw=PhysicsModel.SelectedIndex is 1 or 4 or 5 or 7 ? BurnLaw.PrescribedWiebe : BurnLaw.ConditionScaledWiebe,
                    Products=PhysicsModel.SelectedIndex >= 3 ? ProductChemistry.ClosedCycleEquilibrium : ProductChemistry.Frozen,
                    CombustionEvidence=settings.CombustionEvidence with{Status=EvidenceStatus.Estimated},
                    HeatTransferEvidence=settings.HeatTransferEvidence with{Status=EvidenceStatus.Estimated},
                    FrictionEvidence=settings.FrictionEvidence with{Status=EvidenceStatus.Estimated}};
                config=config with{Thermochemistry=settings,Combustion=config.Combustion with{
                    LowerHeatingValueJPerKg=SpeciesThermodynamics.LowerHeatingValue,
                    AirFuelRatio=wasLegacy?SpeciesThermodynamics.StoichiometricAfr:config.Combustion.AirFuelRatio}};
            }
            Populate(config);dirty=true;Title="Dino2 · modelo modificado *";
            Status.Text="Modelo cambiado. Al activar iso-octano desde v1 se fijan AFR estequiométrico y PCI NASA. Revise entradas y evidencia; A se conserva.";
        }
        catch(Exception ex) when(Expected(ex))
        {
            populating=true;PhysicsModel.SelectedIndex=ModelIndex(editorTemplate);populating=false;
            Status.Text="No se cambió el modelo: "+ex.Message;
        }
    }

    internal void KeepOriginal()
    {
        if (CurrentResult is null) throw new InvalidOperationException("Simule antes de conservar el original.");
        Resolution = null;
        original = CurrentResult.Configuration;
        OriginalResult = CurrentResult;
        dirty = true;
        Title = "Dino2 · original A conservado *";
        RenderResults();
        Status.Text = "Original A conservado. Edite una característica para simular B; A permanecerá intacto.";
    }
    private void Keep_Click(object sender, RoutedEventArgs e) => KeepOriginal();
    private void RemoveOriginal_Click(object sender, RoutedEventArgs e)
    {
        Resolution = null;
        original = null; OriginalResult = null; dirty = true; RenderResults();
        Status.Text = "Se quitó la referencia A del proyecto.";
    }

    private List<PlotLine> SweepLines(bool power)
    {
        var lines = new List<PlotLine>();
        void Explicit(FourStrokeResult? r, string name, Brush color)
        {
            if (r is null) return;
            lines.Add(new(name + " indicada", r.Points.Select(p => new Point(p.Rpm, power ? p.IndicatedPowerW / 1000 : p.IndicatedTorqueNm)).ToArray(), color, true));
            lines.Add(new(name + " cigüeñal", r.Points.Select(p => new Point(p.Rpm, power ? p.BrakePowerW / 1000 : p.BrakeTorqueNm)).ToArray(), color));
        }
        Explicit(OriginalResult, "A", Brushes.SteelBlue);
        Explicit(CurrentResult, original is null ? "Actual" : "B", Brushes.DarkOrange);
        return lines;
    }

    private void RenderResults()
    {
        PowerChart.SetLines(SweepLines(true));
        TorqueChart.SetLines(SweepLines(false));
        KeepOriginalButton.IsEnabled = ExportButton.IsEnabled = CurrentResult is not null && !busy;
        var r = CurrentResult;
        Summary.Text = r is null ? (OriginalResult is null ? "Sin simulación para la configuración actual." : "Original A conservado. La configuración actual requiere simulación.") :
            FormattableString.Invariant($"{r.Configuration.Geometry.DisplacementM3 * 1e6:F2} cm³ · {r.Points.Count} RPM · {r.ElapsedMilliseconds:F0} ms · Tmáx {r.Points.Max(p => p.PeakTemperatureK):F0} K.");
        if(r?.Configuration.Thermochemistry is not null)
            Summary.Text+=$" Extrapolación de combustión: {r.Points.Count(p=>p.Chemistry!.FlameCorrelationExtrapolated)}/{r.Points.Count} RPM. Ver diagnósticos.";
        ResultGrid.ItemsSource = r?.Points.Select(p => new { p.Rpm, Indicada_neta_kW = p.IndicatedPowerW / 1000,
            Cigüeñal_kW = p.BrakePowerW / 1000, Par_indicado_Nm = p.IndicatedTorqueNm, Par_cigüeñal_Nm = p.BrakeTorqueNm,
            BMEP_bar = p.BmepPa / 1e5, Eficiencia_volumétrica = p.VolumetricEfficiency,
            Eficiencia_indicada = p.IndicatedEfficiency, Eficiencia_cigüeñal = p.BrakeEfficiency, Ciclos = p.Cycles }).ToArray();
        var comparison = OriginalResult is not null && r is not null ? EngineComparison.Compare(OriginalResult, r) : null;
        ComparisonGrid.ItemsSource = comparison?.Select(p => new { p.Rpm, A_kW = p.OriginalBrakePowerW / 1000, B_kW = p.VariantBrakePowerW / 1000,
            Delta_kW = p.PowerDifferenceW / 1000, A_Nm = p.OriginalBrakeTorqueNm, B_Nm = p.VariantBrakeTorqueNm, Delta_Nm = p.TorqueDifferenceNm, Delta_VE = p.FillingDifference }).ToArray();
        ComparisonNotice.Text = comparison is null ? "Conserve A, modifique un parámetro y vuelva a simular. El original no cambia al editar." :
            $"B − A: {comparison.Count} RPM comunes exactas; sin interpolación. Se comparan estimaciones al cigüeñal. " +
            (comparison is not null && OriginalResult!.Model!=r!.Model?"A/B usan modelos diferentes: no atribuya la diferencia a una modificación mecánica.":"Positivo = aumenta, negativo = disminuye en el modelo; no demuestra una ganancia experimental.");
        bool ducts = OriginalResult?.Configuration.Ducts is not null && r?.Configuration.Ducts is not null;
        ResolutionPanel.Visibility = ducts ? Visibility.Visible : Visibility.Collapsed;
        if (ducts)
        {
            int? selected = ResolutionRpm.SelectedItem as int?;
            int[] common = comparison!.Select(p => p.Rpm).ToArray();
            ResolutionRpm.ItemsSource = common;
            ResolutionRpm.SelectedItem = selected is { } rpm && common.Contains(rpm) ? rpm :
                common.Contains(4000) ? 4000 : common.FirstOrDefault();
            bool eligible = DuctResolution.CanEvaluate(OriginalResult!, r!) && common.Length > 0;
            var a = OriginalResult!.Configuration.Ducts!;
            var b = r!.Configuration.Ducts!;
            bool unequal = DifferentSpacing(a.Intake, b.Intake) || DifferentSpacing(a.Exhaust, b.Exhaust);
            if (unequal) ComparisonNotice.Text += " A y B tienen pasos espaciales distintos. Revisá la resolución antes de interpretar diferencias pequeñas.";
            ResolveMeshButton.IsEnabled = eligible && !busy;
            ResolutionText.Text = Resolution is { } check ? FormatResolution(check) : eligible
                ? "Resolución A/B sin comprobar. El contraste usa mallas base, ×2 y ×4 en el régimen elegido."
                : "Resolución A/B sin comprobar. Para el contraste automático igualá modelos y ajustes, con hasta 64 celdas por conducto.";
        }
        int? inspected = TraceRpm.SelectedItem as int?;
        int[] traceSpeeds = r?.Points.Select(p => p.Rpm).ToArray() ?? [];
        if (TraceRpm.ItemsSource is not int[] previousSpeeds || !previousSpeeds.SequenceEqual(traceSpeeds))
        {
            TraceRpm.ItemsSource = traceSpeeds;
            if (traceSpeeds.Length > 0)
                TraceRpm.SelectedItem = inspected is int rpm && traceSpeeds.Contains(rpm) ? rpm :
                    traceSpeeds.Contains(3000) ? 3000 : traceSpeeds[0];
        }
        if (r is not null) RenderTrace();
        else { PressureChart.SetLines([]); PvChart.SetLines([]); TemperatureChart.SetLines([]); CycleDiagnostics.Text = ""; }
        if (r?.Configuration.Ducts is null)
        {
            PortChart.SetLines([]); FlowChart.SetLines([]);
            PortCaption.Text = "Seleccione un modelo 1D y simule para ver la presión de los conductos.";
        }
        Diagnostics.Text = BuildDiagnostics(r);
    }

    private static string BuildDiagnostics(FourStrokeResult? r)
    {
        if (r is null) return "Sin resultados actuales. La referencia A, si existe, se conserva por separado.";
        if(r.Configuration.Thermochemistry is not null) return ReactiveDiagnosticText.Build(r);
        var text = new StringBuilder();
        text.AppendLine("MODELO: " + FourStrokeResult.ModelId);
        text.AppendLine("0° = PMS de intercambio; combustión alrededor de 360°. Admisión 0–180°, compresión 180–360°, expansión 360–540°, escape 540–720°.");
        text.AppendLine("Pi: trabajo neto del gas en 720°. Pb: Pi menos fricción. Bombeo se integra en 0–180° y 540–720°; ya está incluido en Pi. BMEP usa trabajo al cigüeñal. VE usa aire fresco retenido al cierre de admisión y densidad del depósito de admisión.");
        text.AppendLine("No hay datos calibrados. Cd y combustión son entradas asumidas. Fricción del ejemplo: transferencia de coeficientes de literatura, no calibración de este cilindro. El modelo no incluye ondas, química, detonación, fugas ni accesorios separados.");
        text.AppendLine("Valores negativos de par/potencia se conservan: no se recortan a cero. El ejemplo es sintético; sus cifras no especifican una precisión predictiva.");
        foreach (var p in r.Points)
            text.AppendLine(FormattableString.Invariant($"\n{p.Rpm} RPM · {p.Cycles} ciclos · periodicidad {p.PeriodicError:E3} · {p.AcceptedSteps} pasos aceptados / {p.RejectedSteps} rechazados · {p.ElapsedMilliseconds:F1} ms\nResidual masa {p.MassResidualKg:E3} kg; energía {p.EnergyResidualJ:E3} J\nTrabajo bruto {p.GrossWorkJ:F4} J; bombeo {p.PumpingWorkJ:F4} J; neto {p.NetWorkJ:F4} J; fricción {p.FrictionWorkJ:F4} J\nQ liberado {p.HeatReleasedJ:F4} J; pared {p.WallHeatJ:F4} J; entalpía neta {p.FlowEnthalpyJ:F4} J; T pico {p.PeakTemperatureK:F1} K."));
        return text.ToString();
    }

    private void TraceRpm_Changed(object sender, SelectionChangedEventArgs e) => RenderTrace();

    private void RenderTrace()
    {
        if (CurrentResult is null || TraceRpm.SelectedItem is not int rpm) return;
        var p = CurrentResult.Points.Single(p => p.Rpm == rpm);
        PressureChart.SetLines([new("Presión [bar]", p.Trace.Select(s => new Point(s.AngleDeg, s.PressurePa / 1e5)).ToArray(), Brushes.SteelBlue)]);
        PvChart.SetLines([new("p-V", p.Trace.Select(s => new Point(s.VolumeM3 * 1e6, s.PressurePa / 1e5)).ToArray(), Brushes.DarkOrange)]);
        TemperatureChart.SetLines([new("Cilindro", p.Trace.Select(s => new Point(s.AngleDeg, s.TemperatureK)).ToArray(), Brushes.SteelBlue)]);
        if (p.GasDynamics is not null)
        {
            var pressures = new List<PlotLine>();
            var flows = new List<PlotLine>();
            void Add(FourStrokePoint point, string label, bool original)
            {
                pressures.Add(new(label + " admisión", point.Trace.Select(s => new Point(s.AngleDeg, s.IntakePortPressurePa / 1e5)).ToArray(), Brushes.SteelBlue, original));
                pressures.Add(new(label + " escape", point.Trace.Select(s => new Point(s.AngleDeg, s.ExhaustPortPressurePa / 1e5)).ToArray(), Brushes.DarkOrange, original));
                flows.Add(new(label + " admisión", point.Trace.Select(s => new Point(s.AngleDeg, s.IntakeFlowKgPerS * 1000)).ToArray(), Brushes.SteelBlue, original));
                flows.Add(new(label + " escape", point.Trace.Select(s => new Point(s.AngleDeg, s.ExhaustFlowKgPerS * 1000)).ToArray(), Brushes.DarkOrange, original));
            }
            if (OriginalResult?.Points.FirstOrDefault(q => q.Rpm == rpm && q.GasDynamics is not null) is { } a)
                Add(a, "A", true);
            Add(p, "Actual", false);
            PortChart.SetLines(pressures);
            FlowChart.SetLines(flows);
            PortCaption.Text = $"{rpm} RPM · Admisión en azul, escape en naranja, A en trazo discontinuo. Caudal negativo hacia el conducto.";
        }
        CycleDiagnostics.Text = FormattableString.Invariant($"{p.Cycles} ciclos · residual masa {p.MassResidualKg:E2} kg · residual energía {p.EnergyResidualJ:E2} J · periodicidad {p.PeriodicError:E2}. Último ciclo periódico.");
        if (p.Chemistry is not null)
        {
            var species = p.Trace[^1].Composition;
            CycleDiagnostics.Text += FormattableString.CurrentCulture(
                $" Cierre del ciclo [mg] · combustible {species.FuelKg * 1e6:G4} · O₂ {species.OxygenKg * 1e6:G4} · N₂ {species.NitrogenKg * 1e6:G4} · CO₂ {species.CarbonDioxideKg * 1e6:G4} · H₂O {species.WaterKg * 1e6:G4} · CO {species.CarbonMonoxideKg * 1e6:G4} · H₂ {species.HydrogenKg * 1e6:G4}.");
        }
    }

    private static bool DifferentSpacing(DuctGeometry a, DuctGeometry b)
    {
        double dxA = a.LengthM / a.Cells, dxB = b.LengthM / b.Cells;
        return Math.Max(dxA, dxB) > 1.15 * Math.Min(dxA, dxB);
    }

    private static string FormatResolution(DuctResolution check)
    {
        string Signal(string label, string unit, MeshSignal s) =>
            FormattableString.Invariant($"{label}: {s.Coarse:G5} / {s.Medium:G5} / {s.Fine:G5} {unit}, banda observada {s.ObservedEnvelope:G3} {unit}. ") +
            (s.AboveObservedVariation ? "Señal superior a la variación. " : "Diferencia indeterminada. ");
        return $"{check.Rpm} RPM · B−A con mallas base / ×2 / ×4. " +
            Signal("Potencia", "W", check.BrakePowerW) + Signal("Par", "N·m", check.BrakeTorqueNm) +
            Signal("VE", "–", check.Filling) + "Banda empírica de discretización.";
    }

    internal async Task CheckResolutionAsync(int rpm)
    {
        if (busy || OriginalResult is not { } a || CurrentResult is not { } b || !DuctResolution.CanEvaluate(a, b)) return;
        Resolution = null;
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        busy = true; Commands.IsEnabled = ParameterTabs.IsEnabled = PhysicsModel.IsEnabled = ResolveMeshButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ResolutionText.Text = $"Comprobando la resolución a {rpm} RPM.";
        try
        {
            var progress = new Progress<string>(message => { if (busy && !token.IsCancellationRequested) Status.Text = message; });
            Resolution = await Task.Run(() => DuctResolution.Evaluate(a, b, rpm, token, progress), token);
            Status.Text = $"Contraste de malla completado a {rpm} RPM.";
        }
        catch (OperationCanceledException) { Status.Text = "Contraste de malla cancelado. Las curvas base siguen disponibles."; }
        catch (Exception ex) when (Expected(ex)) { Status.Text = "Resolución sin comprobar: " + ex.Message; }
        finally
        {
            busy = false; Commands.IsEnabled = ParameterTabs.IsEnabled = PhysicsModel.IsEnabled = true;
            CancelButton.IsEnabled = false; cancellation.Dispose(); cancellation = null; RenderResults();
        }
    }

    private async void ResolveMesh_Click(object sender, RoutedEventArgs e)
    {
        if (ResolutionRpm.SelectedItem is int rpm) await CheckResolutionAsync(rpm);
    }

    private void ApplyStudyVariant(FourStrokeConfiguration configuration)
    {
        if (busy) throw new ArgumentException("Esperá a que termine el cálculo A/B antes de aplicar una variante.");
        Populate(configuration);
        dirty = true;
        AnalysisTabs.SelectedIndex = 0;
        Title = "Dino2 · variante de estudio *";
        Status.Text = "Variante aplicada. Simulá para regenerar el ciclo y compararla con el original A si existe.";
    }

    internal void SaveTo(string path)
    {
        FourStrokeFiles.Save(path, new(ReadConfiguration(), original)); dirty = false;
        Title = "Dino2 · " + Path.GetFileName(path); Status.Text = "Proyecto guardado: " + path;
    }
    internal void LoadFrom(string path)
    {
        var project = FourStrokeFiles.Load(path);
        original = project.Original; OriginalResult = null; Populate(project.Current); dirty = false;
        Title = "Dino2 · " + Path.GetFileName(path); Status.Text = "Proyecto recuperado. Pulse Simular para reconstruir las curvas A y actual.";
    }
    private bool ConfirmDiscard() => !dirty || (confirmDiscard?.Invoke() ?? (MessageBox.Show(this, "Hay cambios del proyecto sin guardar. ¿Desea descartarlos?",
        "Dino2", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes));
    private static bool Expected(Exception e) => e is ArgumentException or ArithmeticException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException;
    private void Guard(Action action) { try { action(); } catch (Exception e) when (Expected(e)) { Status.Text = "No se completó la operación: " + e.Message; } }
    private void Example_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        original = null; OriginalResult = null; Populate(ThermochemicalOptions.EquilibriumExample); dirty = false;
        Title = "Dino2 · ejemplo 4T"; Status.Text = "Caso sintético cargado. Pulse Simular.";
    }
    private void Open_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "Proyecto 4T (*.json)|*.json" };
        if (dialog.ShowDialog(this) == true) LoadFrom(dialog.FileName);
    });
    private void Save_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new SaveFileDialog { Filter = "Proyecto 4T (*.json)|*.json", FileName = "motor-4t.json" };
        if (dialog.ShowDialog(this) == true) SaveTo(dialog.FileName);
    });
    private void Export_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (CurrentResult is null) return;
        var dialog = new SaveFileDialog { Filter = "Resultados CSV (*.csv)|*.csv", FileName = "motor-4t.csv" };
        if (dialog.ShowDialog(this) != true) return;
        ExportTo(dialog.FileName);
    });

    internal void ExportTo(string path)
    {
        if (CurrentResult is null) return;
        FourStrokeFiles.Export(path, CurrentResult);
        int rpm = TraceRpm.SelectedItem is int selected ? selected : CurrentResult.Points[0].Rpm;
        FourStrokeFiles.ExportTrace(Path.ChangeExtension(path, ".trace.csv"), CurrentResult, rpm);
        if (OriginalResult is not null)
        {
            FourStrokeFiles.Export(Path.ChangeExtension(path, ".original.csv"), OriginalResult);
            FourStrokeFiles.ExportComparison(Path.ChangeExtension(path, ".comparison.csv"), OriginalResult, CurrentResult);
            if (Resolution is { } check)
                File.WriteAllText(Path.ChangeExtension(path, ".mesh.json"),
                    JsonSerializer.Serialize(check, new JsonSerializerOptions { WriteIndented = true }));
        }
        Status.Text = "Se exportaron curvas, traza del régimen seleccionado y comparación cuando existe A: " + path;
    }
    private void Otto_Click(object sender, RoutedEventArgs e)
    {
        if (ottoWindow is not null) { ottoWindow.Activate(); return; }
        var child = new MainWindow { Owner = this };
        ottoWindow = child;
        child.Closed += (_, _) => ottoWindow = null;
        child.Show();
    }
}
