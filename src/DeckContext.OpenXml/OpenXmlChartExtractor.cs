using System.Globalization;
using System.Xml;
using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;

namespace DeckContext.OpenXml;

internal sealed record ChartExtractionResult(
    ChartContext? Chart,
    ExtractionStatus Status);

internal static class OpenXmlChartExtractor
{
    private const string ChartNamespace = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string ExtractorName = "ChartExtractor";

    private static readonly HashSet<string> SupportedChartTypes = new(StringComparer.Ordinal)
    {
        "area3DChart",
        "areaChart",
        "bar3DChart",
        "barChart",
        "bubbleChart",
        "doughnutChart",
        "line3DChart",
        "lineChart",
        "ofPieChart",
        "pie3DChart",
        "pieChart",
        "radarChart",
        "scatterChart",
        "stockChart",
    };

    private static readonly HashSet<string> AxisTypes = new(StringComparer.Ordinal)
    {
        "catAx",
        "dateAx",
        "serAx",
        "valAx",
    };

    public static bool IsChart(P.GraphicFrame graphicFrame)
    {
        return FindChartReference(graphicFrame) is not null;
    }

    public static ChartExtractionResult Extract(
        P.GraphicFrame graphicFrame,
        SlidePart slidePart,
        SourceReference source,
        ICollection<ExtractionDiagnostic> diagnostics)
    {
        var chartReference = FindChartReference(graphicFrame);
        var relationshipId = ReadStringAttribute(chartReference, "id");

        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            diagnostics.Add(CreateDiagnostic(
                "DCX-CHART-RELATIONSHIP-MISSING",
                "The chart reference does not declare a relationship id.",
                DiagnosticSeverity.Error,
                DiagnosticOutcome.Skipped,
                source));
            return new ChartExtractionResult(null, ExtractionStatus.Failed);
        }

        try
        {
            OpenXmlPart linkedPart;

            try
            {
                linkedPart = slidePart.GetPartById(relationshipId);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
            {
                diagnostics.Add(CreateDiagnostic(
                    "DCX-CHART-RELATIONSHIP-FAILED",
                    $"The chart relationship could not be resolved: {exception.Message}",
                    DiagnosticSeverity.Error,
                    DiagnosticOutcome.Skipped,
                    source));
                return new ChartExtractionResult(null, ExtractionStatus.Failed);
            }

            if (linkedPart is not ChartPart chartPart)
            {
                diagnostics.Add(CreateDiagnostic(
                    "DCX-CHART-RELATIONSHIP-FAILED",
                    "The chart relationship does not resolve to a chart part.",
                    DiagnosticSeverity.Error,
                    DiagnosticOutcome.Skipped,
                    source));
                return new ChartExtractionResult(null, ExtractionStatus.Failed);
            }

            var chartSpace = chartPart.ChartSpace;

            if (chartSpace is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    "DCX-CHART-ROOT-MISSING",
                    "The chart part does not contain a readable chart space.",
                    DiagnosticSeverity.Error,
                    DiagnosticOutcome.Skipped,
                    source));
                return new ChartExtractionResult(null, ExtractionStatus.Failed);
            }

            var chartRoot = FindDirectChild(chartSpace, "chart");
            var plotArea = FindDirectChild(chartRoot, "plotArea");
            var plotElements = plotArea?.ChildElements
                .Where(IsChartTypeElement)
                .ToArray() ?? [];

            if (plotElements.Length == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    "DCX-CHART-PLOT-MISSING",
                    "The chart does not contain a recognizable plot definition.",
                    DiagnosticSeverity.Warning,
                    DiagnosticOutcome.Partial,
                    source));
            }

            var status = ExtractionStatus.Succeeded;
            var plots = new List<ChartPlotContext>(plotElements.Length);

            foreach (var plotElement in plotElements)
            {
                var isSupported = SupportedChartTypes.Contains(plotElement.LocalName);

                if (!isSupported)
                {
                    diagnostics.Add(CreateDiagnostic(
                        "DCX-CHART-TYPE-UNSUPPORTED",
                        $"The native chart type '{plotElement.LocalName}' is not supported.",
                        DiagnosticSeverity.Warning,
                        DiagnosticOutcome.Partial,
                        source));
                    status = status == ExtractionStatus.Succeeded
                        ? ExtractionStatus.Unsupported
                        : status;
                }

                var series = ReadSeries(plotElement, source, diagnostics, ref status);

                if (isSupported && series.Count == 0)
                {
                    diagnostics.Add(CreateDiagnostic(
                        "DCX-CHART-SERIES-MISSING",
                        $"The native chart plot '{plotElement.LocalName}' does not contain a series.",
                        DiagnosticSeverity.Warning,
                        DiagnosticOutcome.Partial,
                        source));
                    status = ExtractionStatus.Partial;
                }

                plots.Add(new ChartPlotContext(
                    plotElement.LocalName,
                    series,
                    ReadDataLabels(plotElement)));
            }

            if (plotElements.Length == 0)
            {
                status = ExtractionStatus.Partial;
            }
            else if (status == ExtractionStatus.Unsupported &&
                     plotElements.Any(element => SupportedChartTypes.Contains(element.LocalName)))
            {
                status = ExtractionStatus.Partial;
            }

            var allSeries = plots.SelectMany(plot => plot.Series).ToArray();
            var externalData = FindDirectChild(chartSpace, "externalData");
            var chart = new ChartContext(
                relationshipId,
                chartPart.Uri.OriginalString,
                ReadChartText(FindDirectChild(chartRoot, "title")),
                plots,
                ReadLegend(chartRoot, allSeries),
                ReadAxes(plotArea),
                ReadStringAttribute(externalData, "id"),
                ReadBooleanValue(FindDirectChild(externalData, "autoUpdate")));

            var workbookResult = OpenXmlEmbeddedWorkbookExtractor.Extract(
                chartPart,
                chart,
                source,
                diagnostics);
            chart = workbookResult.Chart;

            if (workbookResult.Status == ExtractionStatus.Partial)
            {
                status = ExtractionStatus.Partial;
            }

            if (status == ExtractionStatus.Succeeded &&
                diagnostics.Any(diagnostic => diagnostic.Outcome == DiagnosticOutcome.Partial))
            {
                status = ExtractionStatus.Partial;
            }

            return new ChartExtractionResult(chart, status);
        }
        catch (Exception exception) when (IsExpectedChartFailure(exception))
        {
            diagnostics.Add(CreateDiagnostic(
                "DCX-CHART-READ-FAILED",
                $"The chart could not be read: {exception.Message}",
                DiagnosticSeverity.Error,
                DiagnosticOutcome.Skipped,
                source));
            return new ChartExtractionResult(null, ExtractionStatus.Failed);
        }
    }

    private static IReadOnlyList<ChartSeriesContext> ReadSeries(
        OpenXmlElement plotElement,
        SourceReference source,
        ICollection<ExtractionDiagnostic> diagnostics,
        ref ExtractionStatus status)
    {
        var sourceSeries = plotElement.ChildElements
            .Where(child => child.LocalName == "ser")
            .ToArray();
        var series = new List<ChartSeriesContext>(sourceSeries.Length);

        for (var position = 0; position < sourceSeries.Length; position++)
        {
            var sourceItem = sourceSeries[position];
            var index = ReadIntValue(FindDirectChild(sourceItem, "idx")) ?? position;
            var order = ReadIntValue(FindDirectChild(sourceItem, "order")) ?? position;
            var nameContainer = FindDirectChild(sourceItem, "tx");
            var categories = ReadDataSource(
                FindDirectChild(sourceItem, "cat") ?? FindDirectChild(sourceItem, "xVal"));
            var values = ReadDataSource(
                FindDirectChild(sourceItem, "val") ?? FindDirectChild(sourceItem, "yVal"));

            if (values is null || values.Points.Count == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    "DCX-CHART-SERIES-VALUES-MISSING",
                    $"Chart series {index} does not contain cached or literal values.",
                    DiagnosticSeverity.Warning,
                    DiagnosticOutcome.Partial,
                    source));
                status = status == ExtractionStatus.Unsupported
                    ? status
                    : ExtractionStatus.Partial;
            }

            if (categories is { Points.Count: > 0 } &&
                values is { Points.Count: > 0 } &&
                categories.Points.Count != values.Points.Count)
            {
                diagnostics.Add(CreateDiagnostic(
                    "DCX-CHART-POINT-COUNT-MISMATCH",
                    $"Chart series {index} contains {categories.Points.Count} category points and {values.Points.Count} value points.",
                    DiagnosticSeverity.Warning,
                    DiagnosticOutcome.Partial,
                    source));
                status = status == ExtractionStatus.Unsupported
                    ? status
                    : ExtractionStatus.Partial;
            }

            series.Add(new ChartSeriesContext(
                index,
                order,
                ReadSeriesName(nameContainer),
                ReadFormula(nameContainer),
                categories,
                values));
        }

        return series;
    }

    private static ChartDataSourceContext? ReadDataSource(OpenXmlElement? source)
    {
        if (source is null)
        {
            return null;
        }

        var formula = ReadFormula(source);
        var multiLevelCache = source.Descendants()
            .FirstOrDefault(element => element.LocalName == "multiLvlStrCache");

        if (multiLevelCache is not null)
        {
            return new ChartDataSourceContext(
                formula,
                null,
                ReadMultiLevelPoints(multiLevelCache));
        }

        var cache = source.Descendants()
            .FirstOrDefault(element => element.LocalName is "strCache" or "numCache" or "strLit" or "numLit");

        if (cache is null)
        {
            return new ChartDataSourceContext(formula, null, []);
        }

        var formatCode = cache.Descendants()
            .FirstOrDefault(element => element.LocalName == "formatCode")?
            .InnerText;
        var points = cache.ChildElements
            .Where(child => child.LocalName == "pt")
            .Select((point, position) => CreatePoint(point, position))
            .OrderBy(point => point.Index)
            .ToArray();

        return new ChartDataSourceContext(
            formula,
            NullIfWhiteSpace(formatCode),
            points);
    }

    private static IReadOnlyList<ChartDataPointContext> ReadMultiLevelPoints(OpenXmlElement cache)
    {
        var valuesByIndex = new SortedDictionary<int, List<string>>();

        foreach (var level in cache.ChildElements.Where(child => child.LocalName == "lvl"))
        {
            var levelPoints = level.ChildElements.Where(child => child.LocalName == "pt");

            foreach (var point in levelPoints)
            {
                var index = ReadIntAttribute(point, "idx") ?? valuesByIndex.Count;
                var value = NullIfWhiteSpace(FindDirectChild(point, "v")?.InnerText);

                if (value is null)
                {
                    continue;
                }

                if (!valuesByIndex.TryGetValue(index, out var values))
                {
                    values = [];
                    valuesByIndex[index] = values;
                }

                values.Add(value);
            }
        }

        return valuesByIndex
            .Select(pair => new ChartDataPointContext(
                pair.Key,
                string.Join(" / ", pair.Value),
                null))
            .ToArray();
    }

    private static ChartDataPointContext CreatePoint(OpenXmlElement point, int fallbackIndex)
    {
        var value = NullIfWhiteSpace(FindDirectChild(point, "v")?.InnerText);
        return new ChartDataPointContext(
            ReadIntAttribute(point, "idx") ?? fallbackIndex,
            value,
            TryReadDouble(value));
    }

    private static ChartLegendContext ReadLegend(
        OpenXmlElement? chartRoot,
        IReadOnlyList<ChartSeriesContext> series)
    {
        var legend = FindDirectChild(chartRoot, "legend");

        if (legend is null)
        {
            return new ChartLegendContext(false, null, null, []);
        }

        var hiddenIndexes = legend.ChildElements
            .Where(child => child.LocalName == "legendEntry")
            .Where(entry => ReadBooleanValue(FindDirectChild(entry, "delete")) is true)
            .Select(entry => ReadIntValue(FindDirectChild(entry, "idx")))
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .ToHashSet();
        var entries = series
            .Select(item => new ChartLegendEntryContext(
                item.Index,
                item.Name,
                !hiddenIndexes.Contains(item.Index)))
            .ToArray();

        return new ChartLegendContext(
            true,
            ReadValue(FindDirectChild(legend, "legendPos")),
            ReadBooleanValue(FindDirectChild(legend, "overlay")),
            entries);
    }

    private static IReadOnlyList<ChartAxisContext> ReadAxes(OpenXmlElement? plotArea)
    {
        if (plotArea is null)
        {
            return [];
        }

        return plotArea.ChildElements
            .Where(element => AxisTypes.Contains(element.LocalName))
            .Select(axis =>
            {
                var scaling = FindDirectChild(axis, "scaling");
                var numberFormat = FindDirectChild(axis, "numFmt");

                return new ChartAxisContext(
                    axis.LocalName,
                    ReadValue(FindDirectChild(axis, "axId")),
                    ReadValue(FindDirectChild(axis, "axPos")),
                    ReadValue(FindDirectChild(axis, "crossAx")),
                    ReadValue(FindDirectChild(axis, "crosses")),
                    ReadValue(FindDirectChild(axis, "crossBetween")),
                    ReadChartText(FindDirectChild(axis, "title")),
                    ReadStringAttribute(numberFormat, "formatCode"),
                    ReadBooleanAttribute(numberFormat, "sourceLinked"),
                    ReadDoubleValue(FindDirectChild(scaling, "min")),
                    ReadDoubleValue(FindDirectChild(scaling, "max")),
                    ReadDoubleValue(FindDirectChild(axis, "majorUnit")),
                    ReadDoubleValue(FindDirectChild(axis, "minorUnit")));
            })
            .ToArray();
    }

    private static ChartDataLabelsContext ReadDataLabels(OpenXmlElement plotElement)
    {
        var labels = FindDirectChild(plotElement, "dLbls");

        return labels is null
            ? new ChartDataLabelsContext(false, null, null, null, null, null, null, null, null, null)
            : new ChartDataLabelsContext(
                true,
                ReadValue(FindDirectChild(labels, "dLblPos")),
                ReadStringAttribute(FindDirectChild(labels, "numFmt"), "formatCode"),
                ReadBooleanAttribute(FindDirectChild(labels, "numFmt"), "sourceLinked"),
                NullIfWhiteSpace(FindDirectChild(labels, "separator")?.InnerText),
                ReadBooleanValue(FindDirectChild(labels, "showLegendKey")),
                ReadBooleanValue(FindDirectChild(labels, "showVal")),
                ReadBooleanValue(FindDirectChild(labels, "showCatName")),
                ReadBooleanValue(FindDirectChild(labels, "showSerName")),
                ReadBooleanValue(FindDirectChild(labels, "showPercent")));
    }

    private static string? ReadSeriesName(OpenXmlElement? nameContainer)
    {
        if (nameContainer is null)
        {
            return null;
        }

        var directValue = FindDirectChild(nameContainer, "v")?.InnerText;

        if (!string.IsNullOrWhiteSpace(directValue))
        {
            return directValue;
        }

        return nameContainer.Descendants()
            .Where(element => element.LocalName == "pt")
            .Select(point => FindDirectChild(point, "v")?.InnerText)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? ReadChartText(OpenXmlElement? container)
    {
        if (container is null)
        {
            return null;
        }

        var paragraphs = container.Descendants()
            .Where(element => element.LocalName == "p" && element.NamespaceUri == DrawingNamespace)
            .Select(paragraph => string.Concat(
                paragraph.Descendants()
                    .Where(element => element.LocalName == "t" && element.NamespaceUri == DrawingNamespace)
                    .Select(element => element.InnerText)))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        if (paragraphs.Length > 0)
        {
            return string.Join("\n", paragraphs);
        }

        return container.Descendants()
            .Where(element => element.LocalName == "v" && element.NamespaceUri == ChartNamespace)
            .Select(element => NullIfWhiteSpace(element.InnerText))
            .FirstOrDefault(value => value is not null);
    }

    private static string? ReadFormula(OpenXmlElement? container)
    {
        return container?.Descendants()
            .Where(element => element.LocalName == "f" && element.NamespaceUri == ChartNamespace)
            .Select(element => NullIfWhiteSpace(element.InnerText))
            .FirstOrDefault(value => value is not null);
    }

    private static OpenXmlElement? FindChartReference(P.GraphicFrame graphicFrame)
    {
        return graphicFrame.Descendants()
            .FirstOrDefault(element =>
                element.LocalName == "chart" &&
                element.NamespaceUri == ChartNamespace);
    }

    private static OpenXmlElement? FindDirectChild(OpenXmlElement? parent, string localName)
    {
        return parent?.ChildElements.FirstOrDefault(child => child.LocalName == localName);
    }

    private static bool IsChartTypeElement(OpenXmlElement element)
    {
        return element.NamespaceUri == ChartNamespace &&
               element.LocalName.EndsWith("Chart", StringComparison.Ordinal);
    }

    private static int? ReadIntValue(OpenXmlElement? element)
    {
        return int.TryParse(
            ReadValue(element),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static double? ReadDoubleValue(OpenXmlElement? element)
    {
        return TryReadDouble(ReadValue(element));
    }

    private static string? ReadValue(OpenXmlElement? element)
    {
        return ReadStringAttribute(element, "val");
    }

    private static bool? ReadBooleanValue(OpenXmlElement? element)
    {
        return ReadBooleanAttribute(element, "val");
    }

    private static int? ReadIntAttribute(OpenXmlElement? element, string attributeName)
    {
        return int.TryParse(
            ReadStringAttribute(element, attributeName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static bool? ReadBooleanAttribute(OpenXmlElement? element, string attributeName)
    {
        return ReadStringAttribute(element, attributeName) switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null,
        };
    }

    private static string? ReadStringAttribute(OpenXmlElement? element, string attributeName)
    {
        if (element is null)
        {
            return null;
        }

        var value = element.GetAttributes()
            .FirstOrDefault(attribute => attribute.LocalName == attributeName)
            .Value;
        return NullIfWhiteSpace(value);
    }

    private static double? TryReadDouble(string? value)
    {
        return double.TryParse(
            value,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var numericValue)
            ? numericValue
            : null;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static ExtractionDiagnostic CreateDiagnostic(
        string code,
        string message,
        DiagnosticSeverity severity,
        DiagnosticOutcome outcome,
        SourceReference source)
    {
        return new ExtractionDiagnostic(
            code,
            message,
            severity,
            ExtractorName,
            outcome,
            source);
    }

    private static bool IsExpectedChartFailure(Exception exception)
    {
        return exception is InvalidDataException
            or OpenXmlPackageException
            or IOException
            or XmlException
            or ArgumentException;
    }
}
