using System.Security.Cryptography;
using System.Text.Json;

namespace Dino2.Storage;

internal static class ProjectIntegrity
{
    internal static string Digest<T>(T document, JsonSerializerOptions options)
    {
        using var sha = SHA256.Create();
        using (var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
        {
            JsonSerializer.Serialize(stream, document, options);
            stream.FlushFinalBlock();
        }
        return Convert.ToHexString(sha.Hash!);
    }

    internal static void Check<T>(T payload, string? digest, bool required, JsonSerializerOptions options)
    {
        if (digest is null ? required : !string.Equals(digest, Digest(payload, options), StringComparison.Ordinal))
            throw new InvalidDataException("Los datos guardados deben corresponder al resumen SHA-256 del archivo.");
    }
}
