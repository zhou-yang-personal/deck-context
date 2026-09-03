using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;

namespace DeckContext.Domain.Model;

public enum ElementKind
{
    Shape,
    Picture,
    GraphicFrame,
    Table,
    Chart,
    Group,
    Connector,
    Unknown,
}

public sealed record ElementIdentity(
    string? Id,
    string? Name);

public sealed record SlideElementContext(
    ElementIdentity Identity,
    ElementKind Kind,
    SourceReference Source,
    int ZOrder,
    NativeGeometry? NativeGeometry,
    NormalizedGeometry? NormalizedGeometry,
    ExtractionStatus Status,
    IReadOnlyList<ExtractionDiagnostic> Diagnostics,
    string? ParentGroupId = null,
    TextContentContext? Text = null,
    TableContext? Table = null,
    ChartContext? Chart = null);
