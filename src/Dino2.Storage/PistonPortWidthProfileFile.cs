using System.Globalization;
using System.Security.Cryptography;
using Dino2.Core.TwoStroke;

namespace Dino2.Storage;

public sealed record ImportedPortWidthProfile(IReadOnlyList<PortWidthPoint> Points, string Sha256, string Source);

public static class PistonPortWidthProfileFile
{
    /// <summary>Reads height above the roof [mm] and aggregate open width [mm] from a labelled CSV.</summary>
    public static ImportedPortWidthProfile Load(string path, double portHeightM, double maximumWidthM)
    {
        var bytes = File.ReadAllBytes(path);
        var points = new List<PortWidthPoint>();
        using var reader = new StringReader(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        string? line;
        int lineNumber = 0;
        bool header = false;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            char delimiter = line.Contains(';') ? ';' : ',';
            var columns = line.Split(delimiter, StringSplitOptions.TrimEntries);
            if (columns.Length != 2) throw new InvalidDataException($"Línea {lineNumber}: se esperan altura y ancho.");
            if (!double.TryParse(columns[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double heightMm) ||
                !double.TryParse(columns[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double widthMm))
            {
                if (points.Count == 0 && !header && IsHeader(columns)) { header = true; continue; }
                throw new InvalidDataException($"Línea {lineNumber}: use números con punto decimal.");
            }
            points.Add(new(heightMm / 1000, widthMm / 1000));
        }
        double tolerance = Math.Max(1e-9, portHeightM * 1e-6);
        if (points.Count < 2 || Math.Abs(points[0].HeightM) > tolerance ||
            Math.Abs(points[^1].HeightM - portHeightM) > tolerance)
            throw new InvalidDataException("El perfil requiere puntos desde 0 hasta la altura configurada de la lumbrera.");
        points[0] = points[0] with { HeightM = 0 };
        points[^1] = points[^1] with { HeightM = portHeightM };
        double previous = -1;
        foreach (var point in points)
        {
            if (!double.IsFinite(point.HeightM) || !double.IsFinite(point.WidthM) || point.HeightM <= previous ||
                point.WidthM < 0 || point.WidthM > maximumWidthM)
                throw new InvalidDataException("Alturas estrictamente crecientes; ancho entre cero y el ancho agregado configurado.");
            previous = point.HeightM;
        }
        return new(points.AsReadOnly(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Path.GetFileName(path));
    }

    private static bool IsHeader(string[] columns) =>
        columns[0].Equals("height_mm", StringComparison.OrdinalIgnoreCase) &&
        columns[1].Equals("width_mm", StringComparison.OrdinalIgnoreCase);
}
