using System.Text.Json.Serialization;

namespace Dino2.Core.GasDynamics;

/// <summary>Measured mass-discharge coefficient map. Coefficients are relative to the declared
/// reference area and the isentropic, ideal-gas nozzle mass flux evaluated from upstream stagnation state.</summary>
public sealed record DischargeMapMetadata(
    string ReferenceArea,
    string PressureConvention,
    string Direction,
    string Units,
    string Source,
    string CompatibleModel,
    string DataKind)
{
    public string DataSha256 { get; init; } = "";
}

public readonly record struct DischargeMapPoint(double Opening, double PressureRatio, double Cd);

/// <summary>
/// Rectangular, strictly increasing opening × downstream/upstream pressure-ratio grid.
/// Bilinear interpolation is bounded by tabulated points; extrapolation is always rejected.
/// Opening is a caller-defined normalized geometric opening (typically lift/maximum lift).
/// </summary>
public sealed class DischargeMap
{
    private readonly double[] openings, ratios, values;
    [JsonInclude]
    public DischargeMapMetadata Metadata { get; init; }
    [JsonInclude]
    public IReadOnlyList<DischargeMapPoint> Points { get; init; }

    public DischargeMap(DischargeMapMetadata metadata, IReadOnlyList<DischargeMapPoint> points)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        if (string.IsNullOrWhiteSpace(metadata.ReferenceArea) || string.IsNullOrWhiteSpace(metadata.PressureConvention) ||
            string.IsNullOrWhiteSpace(metadata.Direction) || string.IsNullOrWhiteSpace(metadata.Units) ||
            string.IsNullOrWhiteSpace(metadata.Source) || string.IsNullOrWhiteSpace(metadata.CompatibleModel) ||
            string.IsNullOrWhiteSpace(metadata.DataKind))
            throw new ArgumentException("El mapa Cd requiere metadatos de área, presión, sentido, unidades, fuente, modelo y clase de datos.", nameof(metadata));
        if (!new[] { "forward", "reverse", "both" }.Contains(metadata.Direction, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Direction debe ser forward, reverse o both.", nameof(metadata));
        var rows = points?.ToArray() ?? throw new ArgumentNullException(nameof(points));
        Points = Array.AsReadOnly(rows);
        if (rows.Length < 4 || rows.Any(p => !double.IsFinite(p.Opening) || !double.IsFinite(p.PressureRatio) ||
                !double.IsFinite(p.Cd) || p.Opening < 0 || p.PressureRatio <= 0 || p.PressureRatio > 1 || p.Cd < 0))
            throw new ArgumentException("Mapa Cd inválido: se requieren al menos 2×2 puntos finitos, apertura ≥0, 0<r≤1 y Cd≥0.", nameof(points));
        openings = rows.Select(p => p.Opening).Distinct().Order().ToArray();
        ratios = rows.Select(p => p.PressureRatio).Distinct().Order().ToArray();
        if (openings.Length < 2 || ratios.Length < 2 || rows.Length != openings.Length * ratios.Length)
            throw new ArgumentException("El mapa Cd debe cubrir una grilla rectangular completa.", nameof(points));
        values = new double[rows.Length];
        var occupied = new HashSet<(double Opening, double Ratio)>();
        foreach (var p in rows)
        {
            if (!occupied.Add((p.Opening, p.PressureRatio)))
                throw new ArgumentException("El mapa Cd contiene coordenadas duplicadas.", nameof(points));
            int i = Array.BinarySearch(openings, p.Opening), j = Array.BinarySearch(ratios, p.PressureRatio);
            values[i * ratios.Length + j] = p.Cd;
        }
        if (openings.Zip(openings.Skip(1)).Any(p => p.Second <= p.First) ||
            ratios.Zip(ratios.Skip(1)).Any(p => p.Second <= p.First))
            throw new ArgumentException("Ejes del mapa Cd no estrictamente crecientes.", nameof(points));
    }

    public bool Equals(DischargeMap? other) => other is not null && Metadata == other.Metadata && Points.SequenceEqual(other.Points);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Metadata);
        foreach (var point in Points) hash.Add(point);
        return hash.ToHashCode();
    }

    public double Evaluate(double opening, double downstreamToUpstreamPressureRatio)
    {
        if (!double.IsFinite(opening) || !double.IsFinite(downstreamToUpstreamPressureRatio))
            throw new ArgumentOutOfRangeException(nameof(opening), "Consulta Cd no finita.");
        int i = Bracket(openings, opening, nameof(opening));
        int j = Bracket(ratios, downstreamToUpstreamPressureRatio, nameof(downstreamToUpstreamPressureRatio));
        double fx = (opening - openings[i]) / (openings[i + 1] - openings[i]);
        double fy = (downstreamToUpstreamPressureRatio - ratios[j]) / (ratios[j + 1] - ratios[j]);
        double a = Lerp(values[i * ratios.Length + j], values[i * ratios.Length + j + 1], fy);
        double b = Lerp(values[(i + 1) * ratios.Length + j], values[(i + 1) * ratios.Length + j + 1], fy);
        return Lerp(a, b, fx);
    }

    private static int Bracket(double[] axis, double value, string name)
    {
        if (value < axis[0] || value > axis[^1])
            throw new ArgumentOutOfRangeException(name, value, $"Consulta fuera del dominio [{axis[0]:R}, {axis[^1]:R}]; no se extrapola.");
        if (value == axis[^1]) return axis.Length - 2;
        int index = Array.BinarySearch(axis, value);
        return index >= 0 ? Math.Min(index, axis.Length - 2) : ~index - 1;
    }
    private static double Lerp(double a, double b, double f) => Math.Max(0, a + f * (b - a));
}
