namespace DeckContext.Domain.Model;

public sealed record ChartDataPointContext(
    int Index,
    string? Value,
    double? NumericValue);

public sealed record ChartDataSourceContext(
    string? Formula,
    string? NumberFormatCode,
    IReadOnlyList<ChartDataPointContext> Points,
    string? WorkbookRangeId = null);

public sealed record ChartSeriesContext(
    int Index,
    int Order,
    string? Name,
    string? NameFormula,
    ChartDataSourceContext? Categories,
    ChartDataSourceContext? Values,
    string? NameWorkbookRangeId = null,
    ChartDataSourceContext? BubbleSizes = null);

public sealed record ChartPlotContext(
    string Type,
    IReadOnlyList<ChartSeriesContext> Series,
    ChartDataLabelsContext DataLabels);

public sealed record ChartLegendEntryContext(
    int SeriesIndex,
    string? Label,
    bool IsVisible);

public sealed record ChartLegendContext(
    bool IsPresent,
    string? Position,
    bool? Overlay,
    IReadOnlyList<ChartLegendEntryContext> Entries);

public sealed record ChartAxisContext(
    string Type,
    string? Id,
    string? Position,
    string? CrossAxisId,
    string? Crosses,
    string? CrossBetween,
    string? Title,
    string? NumberFormatCode,
    bool? NumberFormatLinkedToSource,
    double? Minimum,
    double? Maximum,
    double? MajorUnit,
    double? MinorUnit);

public sealed record ChartDataLabelsContext(
    bool IsPresent,
    string? Position,
    string? NumberFormatCode,
    bool? NumberFormatLinkedToSource,
    string? Separator,
    bool? ShowLegendKey,
    bool? ShowValue,
    bool? ShowCategoryName,
    bool? ShowSeriesName,
    bool? ShowPercentage);

public sealed record ChartContext(
    string RelationshipId,
    string PartUri,
    string? Title,
    IReadOnlyList<ChartPlotContext> Plots,
    ChartLegendContext Legend,
    IReadOnlyList<ChartAxisContext> Axes,
    string? ExternalDataRelationshipId,
    bool? ExternalDataAutoUpdate,
    EmbeddedWorkbookContext? EmbeddedWorkbook = null);
