namespace DeckContext.Domain.Model;

public sealed record TableCellFillContext(
    string Type,
    string Value);

public sealed record TableCellContext(
    int RowIndex,
    int ColumnIndex,
    int RowSpan,
    int ColumnSpan,
    bool IsHorizontalMergeContinuation,
    bool IsVerticalMergeContinuation,
    TextContentContext Text,
    TableCellFillContext? DirectFill);

public sealed record TableRowContext(
    int Index,
    long? HeightEmu,
    IReadOnlyList<TableCellContext> Cells);

public sealed record TableContext(
    int RowCount,
    int ColumnCount,
    IReadOnlyList<TableRowContext> Rows);
