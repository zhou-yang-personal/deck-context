namespace DeckContext.Export;

public enum ContextPackageAssetKind
{
    ContextMarkdown,
    ContextJson,
    ExtractionReport,
    EmbeddedWorkbook,
    Image,
}

public sealed record ContextPackageAsset(
    ContextPackageAssetKind Kind,
    string RelativePath,
    string? SourcePartUri,
    string? RelationshipId,
    string Sha256,
    long SizeBytes);

public sealed record ContextPackageManifest(
    string SchemaVersion,
    string SourceFileName,
    IReadOnlyList<ContextPackageAsset> Assets);

public sealed class ContextPackageManifestSerializer
{
    public string Serialize(ContextPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return DeterministicJson.Serialize(manifest);
    }
}
