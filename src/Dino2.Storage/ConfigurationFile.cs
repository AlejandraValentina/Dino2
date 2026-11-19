using System.Text.Json;
using System.Text.Json.Serialization;
using Dino2.Core;

namespace Dino2.Storage;

public static class ConfigurationFile
{
    private sealed record Document(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string Model,
        [property: JsonRequired] OttoConfiguration Configuration);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public static OttoConfiguration Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<Document>(stream, Options)
            ?? throw new InvalidDataException("Archivo de configuración vacío.");
        if (document.SchemaVersion != 1 || document.Model != OttoCycle.ModelId)
            throw new InvalidDataException("Versión de archivo o modelo incompatible.");
        if (document.Configuration is null)
            throw new InvalidDataException("Falta la configuración del motor.");
        document.Configuration.Validate();
        return document.Configuration;
    }

    public static void Save(string path, OttoConfiguration configuration)
    {
        configuration.Validate();
        var destination = Path.GetFullPath(path);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, new Document(1, OttoCycle.ModelId, configuration), Options);
                stream.Flush(flushToDisk: true);
            }
            // Same directory/volume: replace in one rename; do not truncate the existing file.
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
