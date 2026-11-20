using System.Reflection;

namespace Dino2.Core;

public sealed record ResultProvenance(string Build, string SourceTree, string PhysicsModel, string NumericalRevision,
    DateTimeOffset CalculatedUtc, int ResultSchema)
{
    private static readonly Assembly Source = typeof(ResultProvenance).Assembly;
    private static readonly string Version = Source.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    private static readonly string Tree = Source.GetCustomAttributes<AssemblyMetadataAttribute>()
        .SingleOrDefault(a => a.Key == "Dino2TreeStatus")?.Value ?? "unknown";

    public static ResultProvenance Capture(string model, string revision) => new(Version, Tree, model, revision, DateTimeOffset.UtcNow, 1);
    public static string Describe(ResultProvenance? stamp) => stamp is null
        ? "Procedencia incompleta. Resultado histórico sin identificación de build."
        : $"Compilación {stamp.Build} · árbol {stamp.SourceTree} · {stamp.NumericalRevision} · {stamp.CalculatedUtc:u}.";
}
