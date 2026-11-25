using System.Globalization;
using System.Security.Cryptography;
using Dino2.Core.GasDynamics;

namespace Dino2.Storage;

public sealed record ImportedDischargeMap(DischargeMap Map, string Sha256, string Source);

/// <summary>CSV format: comment metadata (# key=value), then opening_fraction,pressure_ratio,cd.</summary>
public static class DischargeMapFile
{
    public static ImportedDischargeMap Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var points = new List<DischargeMapPoint>();
        using var reader = new StringReader(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        string? line; int lineNumber = 0; bool header = false;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++; line = line.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#'))
            {
                int split = line.IndexOf('=');
                if (split > 1) metadata[line[1..split].Trim()] = line[(split + 1)..].Trim();
                continue;
            }
            var columns = line.Split(line.Contains(';') ? ';' : ',', StringSplitOptions.TrimEntries);
            if (columns.Length != 3) throw new InvalidDataException($"Línea {lineNumber}: se esperan apertura, relación de presión y Cd.");
            if (!double.TryParse(columns[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double opening) ||
                !double.TryParse(columns[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio) ||
                !double.TryParse(columns[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cd))
            {
                if (points.Count == 0 && !header && columns[0].Equals("opening_fraction", StringComparison.OrdinalIgnoreCase) &&
                    columns[1].Equals("pressure_ratio", StringComparison.OrdinalIgnoreCase) && columns[2].Equals("cd", StringComparison.OrdinalIgnoreCase))
                { header = true; continue; }
                throw new InvalidDataException($"Línea {lineNumber}: use números finitos con punto decimal.");
            }
            points.Add(new(opening, ratio, cd));
        }
        string Need(string key) => metadata.TryGetValue(key, out var value) && value.Length > 0
            ? value : throw new InvalidDataException($"Falta metadato obligatorio #{key}=...");
        var mapMetadata = new DischargeMapMetadata(Need("reference_area"), Need("pressure_convention"),
            Need("direction"), Need("units"), Need("source"), Need("compatible_model"), Need("data_kind"));
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        mapMetadata = mapMetadata with { DataSha256 = hash };
        try { return new(new DischargeMap(mapMetadata, points), hash, Path.GetFileName(path)); }
        catch (ArgumentException ex) { throw new InvalidDataException("Grilla Cd inválida: " + ex.Message, ex); }
    }
}
