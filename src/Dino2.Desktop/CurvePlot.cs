using System.Windows;
using System.Windows.Media;
using Dino2.Core;

namespace Dino2.Desktop;

public sealed class CurvePlot : ScientificPlot
{
    public bool Power { get; set; }

    public void SetPoints(IReadOnlyList<OperatingPoint>? points)
    {
        Caption = Power ? "Potencia indicada ideal [kW]" : "Par indicado ideal [N·m]";
        SetLines(points is null ? [] : [new("Otto ideal", points.Select(p =>
            new Point(p.Rpm, Power ? p.IndicatedPowerW / 1000 : p.IndicatedTorqueNm)).ToArray(), Brushes.SteelBlue)]);
    }
}
