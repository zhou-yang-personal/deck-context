namespace DeckContext.Domain.Model;

public sealed record SourceReference(
    string SourceFileName,
    string? PartUri = null,
    string? RelationshipId = null,
    int? SlideIndex = null,
    string? ElementId = null,
    string? ElementName = null);
