using System.Windows;
using Dino2.Core.GasDynamics;
using MahApps.Metro.Controls;

namespace Dino2.Desktop;

public sealed partial class DischargeMapWindow : MetroWindow
{
    internal sealed record PointRow(string Component, string Direction, double OpeningFraction,
        double DownstreamUpstreamPressureRatio, double Cd);

    internal DischargeMapWindow(IEnumerable<(string Component, DischargeMap Map)> maps)
    {
        InitializeComponent();
        var entries = maps.ToArray();
        MapPoints.ItemsSource = entries.SelectMany(entry => entry.Map.Points.Select(point =>
            new PointRow(entry.Component, entry.Map.Metadata.Direction, point.Opening, point.PressureRatio, point.Cd))).ToArray();
        MapMetadata.Text = string.Join(Environment.NewLine, entries.Select(entry =>
        {
            var map = entry.Map;
            return $"{entry.Component} · {map.Metadata.CompatibleModel} · {map.Metadata.ReferenceArea} · {map.Metadata.PressureConvention} · " +
                $"dominio apertura {map.Points.Min(p => p.Opening):G5}–{map.Points.Max(p => p.Opening):G5}, relación {map.Points.Min(p => p.PressureRatio):G5}–{map.Points.Max(p => p.PressureRatio):G5} · " +
                $"{map.Metadata.DataKind}: {map.Metadata.Source} · SHA-256 {map.Metadata.DataSha256}";
        }));
    }
}
