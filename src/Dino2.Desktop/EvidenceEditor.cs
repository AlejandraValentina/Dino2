using System.Windows;
using System.Windows.Controls;
using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Desktop;

internal sealed class EvidenceEditor
{
    private readonly Dictionary<string,(ComboBox Status,TextBox Source,TextBox Dataset,TextBox Range)> editors=[];
    public TabItem Tab { get; }
    public EvidenceEditor(Action changed)
    {
        var panel=new StackPanel{Margin=new Thickness(10)};
        panel.Children.Add(new TextBlock{Text="Procedencia declarada por el usuario. Marcar ‘Calibrado’ requiere fuente, datos y rango; no certifica validación independiente.",TextWrapping=TextWrapping.Wrap});
        foreach(string group in new[]{"Combustión","Calor","Fricción"})
        {
            panel.Children.Add(new TextBlock{Text=group,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,14,0,4)});
            var status=new ComboBox{ItemsSource=new[]{new KeyValuePair<EvidenceStatus,string>(EvidenceStatus.Estimated,"Estimado"),
                new KeyValuePair<EvidenceStatus,string>(EvidenceStatus.Calibrated,"Calibrado (declarado)")},DisplayMemberPath="Value",SelectedValuePath="Key"};
            TextBox Box(string label)
            {
                panel.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,3)});
                var box=new TextBox{TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MinHeight=34}; panel.Children.Add(box);
                box.TextChanged+=(_,_)=>changed(); return box;
            }
            panel.Children.Add(status); status.SelectionChanged+=(_,_)=>changed();
            editors[group]=(status,Box("Fuente / método"),Box("Identificador de datos de calibración"),Box("Rango aplicable declarado"));
        }
        Tab=new TabItem{Header="Evidencia",Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
    }
    public void Populate(ThermochemicalOptions? options)
    {
        var values=options??ThermochemicalOptions.Default;
        void Set(string group,ParameterEvidence evidence)
        {
            var e=editors[group]; e.Status.SelectedValue=evidence.Status;e.Source.Text=evidence.Source;e.Dataset.Text=evidence.CalibrationDataset;e.Range.Text=evidence.ApplicableRange;
            e.Status.IsEnabled=e.Source.IsEnabled=e.Dataset.IsEnabled=e.Range.IsEnabled=options is not null;
        }
        Set("Combustión",values.CombustionEvidence);Set("Calor",values.HeatTransferEvidence);Set("Fricción",values.FrictionEvidence);
    }
    public ThermochemicalOptions Read(ThermochemicalOptions options)
    {
        ParameterEvidence Get(string group)
        {
            var e=editors[group];return new((EvidenceStatus)e.Status.SelectedValue,e.Source.Text,e.Dataset.Text,e.Range.Text);
        }
        return options with{CombustionEvidence=Get("Combustión"),HeatTransferEvidence=Get("Calor"),FrictionEvidence=Get("Fricción")};
    }
    public void InvalidateCalibration(string group)
    {
        string? mapped=group switch{"Combustión" or "Comb. v2"=>"Combustión","Contornos"=>"Calor","Fricción"=>"Fricción",_=>null};
        if(mapped is not null) editors[mapped].Status.SelectedValue=EvidenceStatus.Estimated;
    }
}

internal static class ReactiveDiagnosticText
{
    public static string Build(FourStrokeResult r)
    {
        var text=new System.Text.StringBuilder(); var o=r.Configuration.Thermochemistry!;
        text.AppendLine("MODELO: "+r.Model);
        text.AppendLine(o.UsesEquilibrium
            ? "Siete especies. Equilibrio UV de CO₂/CO y H₂O/H₂ desde el inicio de combustión hasta la apertura de escape. Fuera de ese tramo se transporta composición congelada. Sin predicción de NOx ni CO de escape."
            : "Iso-octano gaseoso + O2 + N2 → CO2 + H2O; productos congelados, sin disociación. cp(T), entalpías de formación y R de mezcla variable. No representa evaporación ni mezclas ricas.");
        text.AppendLine("ENERGÍA TOTAL: ΔU = Hflujo − Qpared − W. U y h incluyen energía química. La energía química convertida es un diagnóstico; NO se suma otra vez como calor al balance total.");
        text.AppendLine("Combustión: "+o.BurnLaw+". La duración escalada usa condiciones al inicio de combustión y una aproximación de turbulencia. No es un solver de propagación de llama ni predice detonación.");
        text.AppendLine("Pi neta ya incluye bombeo; Pb resta FMEP. VE usa aire fresco retenido al cierre de admisión y densidad del aire seco del depósito. Las eficiencias usan combustible neto admitido, incluida su energía química. No se recortan pérdidas ni potencias negativas.");
        void Evidence(string name,ParameterEvidence e)=>text.AppendLine($"\n{name}: {e.Status} (declaración del usuario)\nFuente: {e.Source}\nCalibración: {e.CalibrationDataset}\nRango: {e.ApplicableRange}");
        Evidence("Combustión",o.CombustionEvidence);Evidence("Calor",o.HeatTransferEvidence);Evidence("Fricción",o.FrictionEvidence);
        text.AppendLine("\nSin validación experimental de motor. Una diferencia A/B es una sensibilidad del modelo, no una ganancia mecánica demostrada. Evaluación de disociación y alcance: ver informe de etapa 3.");
        if (r.Configuration.Ducts is not null)
            text.AppendLine("Conductos Euler 1D adiabáticos. SSPRK2 limitado por CFL y paso angular; la tolerancia local RK4 solo se usa con depósitos. VE 1D incluye la etiqueta de aire fresco que entra desde el depósito de admisión y puede retornar desde los conductos.");
        foreach(var p in r.Points)
        {
            var d=p.Chemistry!;
            if (p.GasDynamics is { } duct)
                text.AppendLine(FormattableString.Invariant($"\n{p.Rpm} RPM · conductos: residual masa {duct.MassResidualKg:E3} kg, especie máx {duct.MaximumSpeciesResidualKg:E3} kg, energía {duct.EnergyResidualJ:E3} J, momento {duct.MomentumResidualNs:E3} N·s, periodicidad {duct.PeriodicError:E3}; {duct.CellEvaluations:N0} evaluaciones de celda."));
            text.AppendLine(FormattableString.Invariant($"\n{p.Rpm} RPM · {p.Cycles} ciclos · periodicidad {p.PeriodicError:E3} · {p.ElapsedMilliseconds:F1} ms\nResidual especie máx {d.MaximumSpeciesResidualKg:E3} kg; elemento máx {d.MaximumElementResidualKg:E3} kg; energía total {p.EnergyResidualJ:E3} J\nWi bruto {p.GrossWorkJ:F3} J; bombeo {p.PumpingWorkJ:F3} J; neto {p.NetWorkJ:F3} J; fricción {p.FrictionWorkJ:F3} J\nSOC: {d.SocTemperatureK:F1} K, {d.SocPressurePa/1e5:F3} bar; residuo {d.SocResidualFraction:P2}; duración {d.BurnDurationDeg:F3}°; CA10/50/90 {d.Ca10Deg:F2}/{d.Ca50Deg:F2}/{d.Ca90Deg:F2}°\nT pico {p.PeakTemperatureK:F1} K; correlación de velocidad extrapolada: {d.FlameCorrelationExtrapolated}."));
        }
        return text.ToString();
    }
}
