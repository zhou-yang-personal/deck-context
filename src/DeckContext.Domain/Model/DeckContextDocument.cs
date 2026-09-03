using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;

namespace DeckContext.Domain.Model;

public sealed record DeckMetadata(
    string SourceFileName,
    string? PresentationPartUri,
    long? SlideWidthEmu,
    long? SlideHeightEmu,
    int SlideCount);

public sealed record SlideMetadata(
    int Index,
    string? SlideId,
    string? RelationshipId,
    string? PartUri,
    long? WidthEmu,
    long? HeightEmu);

public sealed record SlideContext(
    SlideMetadata Metadata,
    IReadOnlyList<SlideElementContext> Elements,
    ExtractionStatus Status,
    IReadOnlyList<ExtractionDiagnostic> Diagnostics);

public sealed record DeckContextDocument(
    string SchemaVersion,
    DeckMetadata Deck,
    IReadOnlyList<SlideContext> Slides,
    ExtractionStatus Status,
    IReadOnlyList<ExtractionDiagnostic> Diagnostics)
{
    public const string CurrentSchemaVersion = "0.1";
}
