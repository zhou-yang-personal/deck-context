using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;

namespace DeckContext.Domain.Model;

public sealed record WorkbookCellContext(
    string Reference,
    int RowIndex,
    int ColumnIndex,
    bool IsPresent,
    string? DataType,
    uint? StyleIndex,
    string? Formula,
    string? RawValue,
    string? ResolvedValue);

public sealed record WorkbookRangeContext(
    string Id,
    string Formula,
    string WorksheetName,
    string Address,
    IReadOnlyList<WorkbookCellContext> Cells);

public sealed record WorkbookWorksheetContext(
    string Name,
    string? SheetId,
    string? RelationshipId,
    string? PartUri,
    IReadOnlyList<string> ReferencedRangeIds);

public sealed record EmbeddedWorkbookContext(
    string RelationshipId,
    string ChartPartUri,
    string PartUri,
    string SuggestedFileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    ExtractionStatus Status,
    IReadOnlyList<WorkbookWorksheetContext> Worksheets,
    IReadOnlyList<WorkbookRangeContext> ReferencedRanges,
    IReadOnlyList<ExtractionDiagnostic> Diagnostics);
