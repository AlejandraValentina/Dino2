using System.Globalization;
using System.Security.Cryptography;
using Dino2.Core.FourStroke;

namespace Dino2.Storage;

public sealed record ImportedValveLiftProfile(IReadOnlyList<ValveLiftPoint> Points, string Sha256, string Source);

public static class ValveLiftProfileFile
{
    /// <summary>Reads crank angle [deg] and valve lift [mm] from an explicitly unit-labelled CSV.</summary>
    public static ImportedValveLiftProfile Load(string path, double maximumLiftM)
    {
        var bytes = File.ReadAllBytes(path);
        var points = new List<ValveLiftPoint>();
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
            if (columns.Length != 2) throw new InvalidDataException($"Línea {lineNumber}: se esperan ángulo y alzada.");
            if (!double.TryParse(columns[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double angle) ||
                !double.TryParse(columns[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double liftMm))
            {
                if (points.Count == 0 && !header && IsHeader(columns)) { header = true; continue; }
                throw new InvalidDataException($"Línea {lineNumber}: use números SI/CSV, con punto decimal.");
            }
            points.Add(new(angle, liftMm / 1000));
        }
        if (points.Count < 3 || points[0].CrankAngleDeg != 0 || points[^1].CrankAngleDeg != 720 ||
            points[0].LiftM != points[^1].LiftM)
            throw new InvalidDataException("El perfil requiere puntos desde 0° hasta 720° y alzada periódica.");
        double previous = -1;
        foreach (var point in points)
        {
            if (!double.IsFinite(point.CrankAngleDeg) || !double.IsFinite(point.LiftM) ||
                point.CrankAngleDeg <= previous || point.LiftM < 0 || point.LiftM > maximumLiftM)
                throw new InvalidDataException("Ángulos estrictamente crecientes y alzadas entre 0 y la alzada máxima configurada.");
            previous = point.CrankAngleDeg;
        }
        return new(points.AsReadOnly(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Path.GetFileName(path));
    }

    private static bool IsHeader(string[] columns) =>
        columns[0].Equals("crank_angle_deg", StringComparison.OrdinalIgnoreCase) &&
        columns[1].Equals("lift_mm", StringComparison.OrdinalIgnoreCase);
}
