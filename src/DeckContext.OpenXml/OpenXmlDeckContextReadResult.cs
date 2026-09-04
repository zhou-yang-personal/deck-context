using DeckContext.Domain.Model;

namespace DeckContext.OpenXml;

public enum OpenXmlExtractedAssetKind
{
    EmbeddedWorkbook,
    Image,
}

public sealed record OpenXmlExtractedAsset(
    OpenXmlExtractedAssetKind Kind,
    string PartUri,
    string Sha256,
    long SizeBytes,
    ReadOnlyMemory<byte> Content);

public sealed record OpenXmlDeckContextReadResult(
    DeckContextDocument Document,
    IReadOnlyList<OpenXmlExtractedAsset> Assets);
