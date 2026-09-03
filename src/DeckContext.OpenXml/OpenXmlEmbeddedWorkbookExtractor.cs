using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;
using DocumentFormat.OpenXml.Packaging;

namespace DeckContext.OpenXml;

internal sealed record EmbeddedWorkbookExtractionResult(
    ChartContext Chart,
    ExtractionStatus Status);

internal static partial class OpenXmlEmbeddedWorkbookExtractor
{
    private const string ExtractorName = "EmbeddedWorkbookExtractor";
    private const int MaximumRangeCellCount = 100_000;

    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public static EmbeddedWorkbookExtractionResult Extract(
        ChartPart chartPart,
        ChartContext chart,
        SourceReference source,
        ICollection<ExtractionDiagnostic> elementDiagnostics)
    {
        if (string.IsNullOrWhiteSpace(chart.ExternalDataRelationshipId))
        {
            return new EmbeddedWorkbookExtractionResult(chart, ExtractionStatus.Succeeded);
        }

        var relationshipId = chart.ExternalDataRelationshipId!;
        var workbookDiagnostics = new List<ExtractionDiagnostic>();

        try
        {
            var externalRelationship = chartPart.ExternalRelationships
                .FirstOrDefault(relationship => relationship.Id == relationshipId);

            if (externalRelationship is not null)
            {
                AddDiagnostic(
                    workbookDiagnostics,
                    elementDiagnostics,
                    "DCX-WORKBOOK-EXTERNAL-UNSUPPORTED",
                    $"Chart data relationship '{relationshipId}' points outside the PPTX package and was not fetched.",
                    DiagnosticSeverity.Warning,
                    DiagnosticOutcome.Partial,
                    source);
                return new EmbeddedWorkbookExtractionResult(chart, ExtractionStatus.Partial);
            }

            OpenXmlPart linkedPart;

            try
            {
                linkedPart = chartPart.GetPartById(relationshipId);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
            {
                AddDiagnostic(
                    workbookDiagnostics,
                    elementDiagnostics,
                    "DCX-WORKBOOK-RELATIONSHIP-FAILED",
                    $"The embedded workbook relationship could not be resolved: {exception.Message}",
                    DiagnosticSeverity.Error,
                    DiagnosticOutcome.Partial,
                    source);
                return new EmbeddedWorkbookExtractionResult(chart, ExtractionStatus.Partial);
            }

            if (linkedPart is not EmbeddedPackagePart workbookPart)
            {
                AddDiagnostic(
                    workbookDiagnostics,
                    elementDiagnostics,
                    "DCX-WORKBOOK-RELATIONSHIP-FAILED",
                    "The chart data relationship does not resolve to an embedded package part.",
                    DiagnosticSeverity.Error,
                    DiagnosticOutcome.Partial,
                    source);
                return new EmbeddedWorkbookExtractionResult(chart, ExtractionStatus.Partial);
            }

            byte[] bytes;

            using (var stream = workbookPart.GetStream(FileMode.Open, FileAccess.Read))
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                bytes = buffer.ToArray();
            }

            var formulaBindings = CollectFormulaBindings(chart);
            ParsedWorkbook parsed;

            try
            {
                parsed = ParseWorkbook(
                    bytes,
                    formulaBindings,
                    source,
                    workbookDiagnostics,
                    elementDiagnostics);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or System.Xml.XmlException)
            {
                AddDiagnostic(
                    workbookDiagnostics,
                    elementDiagnostics,
                    "DCX-WORKBOOK-READ-FAILED",
                    $"The embedded workbook could not be read: {exception.Message}",
                    DiagnosticSeverity.Error,
                    DiagnosticOutcome.Partial,
                    source);
                var failedWorkbook = new EmbeddedWorkbookContext(
                    relationshipId,
                    chartPart.Uri.OriginalString,
                    workbookPart.Uri.OriginalString,
                    CreateSuggestedFileName(chartPart.Uri.OriginalString),
                    workbookPart.ContentType,
                    bytes.LongLength,
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    ExtractionStatus.Failed,
                    [],
                    [],
                    workbookDiagnostics.ToArray());
                return new EmbeddedWorkbookExtractionResult(
                    chart with { EmbeddedWorkbook = failedWorkbook },
                    ExtractionStatus.Partial);
            }

            var workbookStatus = workbookDiagnostics.Count == 0
                ? ExtractionStatus.Succeeded
                : ExtractionStatus.Partial;
            var workbook = new EmbeddedWorkbookContext(
                relationshipId,
                chartPart.Uri.OriginalString,
                workbookPart.Uri.OriginalString,
                CreateSuggestedFileName(chartPart.Uri.OriginalString),
                workbookPart.ContentType,
                bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                workbookStatus,
                parsed.Worksheets,
                parsed.Ranges,
                workbookDiagnostics);
            var boundChart = BindRanges(chart, parsed.RangeIdsByFormula) with
            {
                EmbeddedWorkbook = workbook,
            };

            ValidateCaches(boundChart, workbook, source, workbookDiagnostics, elementDiagnostics);

            if (workbookDiagnostics.Count > 0 && workbook.Status == ExtractionStatus.Succeeded)
            {
                workbook = workbook with
                {
                    Status = ExtractionStatus.Partial,
                    Diagnostics = workbookDiagnostics.ToArray(),
                };
                boundChart = boundChart with { EmbeddedWorkbook = workbook };
            }

            return new EmbeddedWorkbookExtractionResult(
                boundChart,
                workbookDiagnostics.Count == 0 ? ExtractionStatus.Succeeded : ExtractionStatus.Partial);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            AddDiagnostic(
                workbookDiagnostics,
                elementDiagnostics,
                "DCX-WORKBOOK-READ-FAILED",
                $"The embedded workbook could not be read: {exception.Message}",
                DiagnosticSeverity.Error,
                DiagnosticOutcome.Partial,
                source);
            return new EmbeddedWorkbookExtractionResult(chart, ExtractionStatus.Partial);
        }
    }

    private static ParsedWorkbook ParseWorkbook(
        byte[] bytes,
        IReadOnlyList<string> formulas,
        SourceReference source,
        ICollection<ExtractionDiagnostic> workbookDiagnostics,
        ICollection<ExtractionDiagnostic> elementDiagnostics)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var workbookDocument = LoadXml(archive, "xl/workbook.xml");
        var relationshipDocument = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var sharedStrings = ReadSharedStrings(archive);
        var relationshipTargets = relationshipDocument
            .Root?
            .Elements(PackageRelationshipNamespace + "Relationship")
            .Where(element => element.Attribute("Id") is not null && element.Attribute("Target") is not null)
            .ToDictionary(
                element => element.Attribute("Id")!.Value,
                element => ResolvePartPath("xl/workbook.xml", element.Attribute("Target")!.Value),
                StringComparer.Ordinal) ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var sheetDefinitions = workbookDocument
            .Descendants(SpreadsheetNamespace + "sheet")
            .Select(sheet => new SheetDefinition(
                sheet.Attribute("name")?.Value ?? string.Empty,
                sheet.Attribute("sheetId")?.Value,
                sheet.Attribute(OfficeRelationshipNamespace + "id")?.Value))
            .ToArray();
        var sheetCells = new Dictionary<string, SheetData>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in sheetDefinitions)
        {
            if (string.IsNullOrWhiteSpace(sheet.RelationshipId) ||
                !relationshipTargets.TryGetValue(sheet.RelationshipId, out var partPath))
            {
                continue;
            }

            var worksheetDocument = LoadXml(archive, partPath);
            sheetCells[sheet.Name] = new SheetData(
                partPath,
                ReadCells(worksheetDocument, sharedStrings));
        }

        var ranges = new List<WorkbookRangeContext>();
        var rangeIdsByFormula = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var formula in formulas)
        {
            if (!TryParseFormula(formula, out var reference))
            {
                AddDiagnostic(
                    workbookDiagnostics,
                    elementDiagnostics,
                    "DCX-WORKBOOK-FORMULA-UNSUPPORTED",
                    $"Workbook formula '{formula}' is not a supported single-sheet A1 range.",
                    DiagnosticSeverity.Warning,
                    DiagnosticOutcome.Partial,
                    source);
                continue;
            }

            if (!sheetCells.TryGetValue(reference.WorksheetName, out var sheetData))
            {
                AddDiagnostic(
                    workbookDiagnostics,
                    elementDiagnostics,
                    "DCX-WORKBOOK-SHEET-MISSING",
                    $"Workbook formula '{formula}' references missing worksheet '{reference.WorksheetName}'.",
                    DiagnosticSeverity.Warning,
                    DiagnosticOutcome.Partial,
                    source);
                continue;
            }

            var cellCount = (long)(reference.EndRow - reference.StartRow + 1) *
                            (reference.EndColumn - reference.StartColumn + 1);

            if (cellCount > MaximumRangeCellCount)
            {
                AddDiagnostic(
                    workbookDiagnostics,
                    elementDiagnostics,
                    "DCX-WORKBOOK-RANGE-TOO-LARGE",
                    $"Workbook formula '{formula}' expands to {cellCount} cells and was not materialized.",
                    DiagnosticSeverity.Warning,
                    DiagnosticOutcome.Partial,
                    source);
                continue;
            }

            var rangeId = $"range-{ranges.Count + 1:D3}";
            var cells = new List<WorkbookCellContext>((int)cellCount);

            for (var row = reference.StartRow; row <= reference.EndRow; row++)
            {
                for (var column = reference.StartColumn; column <= reference.EndColumn; column++)
                {
                    var cellReference = $"{ColumnName(column)}{row}";
                    cells.Add(sheetData.Cells.TryGetValue(cellReference, out var cell)
                        ? cell
                        : new WorkbookCellContext(
                            cellReference,
                            row,
                            column,
                            false,
                            null,
                            null,
                            null,
                            null,
                            null));
                }
            }

            ranges.Add(new WorkbookRangeContext(
                rangeId,
                formula,
                reference.WorksheetName,
                reference.Address,
                cells));
            rangeIdsByFormula[formula] = rangeId;
        }

        var worksheets = sheetDefinitions
            .Select(sheet => new WorkbookWorksheetContext(
                sheet.Name,
                sheet.SheetId,
                sheet.RelationshipId,
                sheetCells.TryGetValue(sheet.Name, out var data) ? $"/{data.PartPath}" : null,
                ranges
                    .Where(range => range.WorksheetName.Equals(sheet.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(range => range.Id)
                    .ToArray()))
            .ToArray();

        return new ParsedWorkbook(worksheets, ranges, rangeIdsByFormula);
    }

    private static IReadOnlyDictionary<string, WorkbookCellContext> ReadCells(
        XDocument worksheet,
        IReadOnlyList<string> sharedStrings)
    {
        var cells = new Dictionary<string, WorkbookCellContext>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in worksheet.Descendants(SpreadsheetNamespace + "c"))
        {
            var reference = cell.Attribute("r")?.Value;

            if (string.IsNullOrWhiteSpace(reference) || !TryParseCellReference(reference, out var row, out var column))
            {
                continue;
            }

            var dataType = cell.Attribute("t")?.Value;
            var rawValue = cell.Element(SpreadsheetNamespace + "v")?.Value;
            var formula = cell.Element(SpreadsheetNamespace + "f")?.Value;
            var resolvedValue = dataType switch
            {
                "s" when int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                         index >= 0 && index < sharedStrings.Count => sharedStrings[index],
                "inlineStr" => string.Concat(cell
                    .Descendants(SpreadsheetNamespace + "t")
                    .Select(text => text.Value)),
                "b" when rawValue == "1" => "true",
                "b" when rawValue == "0" => "false",
                _ => rawValue,
            };
            uint? styleIndex = uint.TryParse(
                cell.Attribute("s")?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedStyleIndex)
                ? parsedStyleIndex
                : null;

            cells[reference] = new WorkbookCellContext(
                reference,
                row,
                column,
                true,
                dataType,
                styleIndex,
                string.IsNullOrWhiteSpace(formula) ? null : formula,
                rawValue,
                resolvedValue);
        }

        return cells;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");

        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        return document
            .Descendants(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item
                .Descendants(SpreadsheetNamespace + "t")
                .Select(text => text.Value)))
            .ToArray();
    }

    private static IReadOnlyList<string> CollectFormulaBindings(ChartContext chart)
    {
        var formulas = new List<string>();

        foreach (var series in chart.Plots.SelectMany(plot => plot.Series))
        {
            AddFormula(formulas, series.NameFormula);
            AddFormula(formulas, series.Categories?.Formula);
            AddFormula(formulas, series.Values?.Formula);
        }

        return formulas;
    }

    private static ChartContext BindRanges(
        ChartContext chart,
        IReadOnlyDictionary<string, string> rangeIdsByFormula)
    {
        return chart with
        {
            Plots = chart.Plots
                .Select(plot => plot with
                {
                    Series = plot.Series
                        .Select(series => series with
                        {
                            NameWorkbookRangeId = FindRangeId(series.NameFormula, rangeIdsByFormula),
                            Categories = BindDataSource(series.Categories, rangeIdsByFormula),
                            Values = BindDataSource(series.Values, rangeIdsByFormula),
                        })
                        .ToArray(),
                })
                .ToArray(),
        };
    }

    private static ChartDataSourceContext? BindDataSource(
        ChartDataSourceContext? source,
        IReadOnlyDictionary<string, string> rangeIdsByFormula)
    {
        return source is null
            ? null
            : source with { WorkbookRangeId = FindRangeId(source.Formula, rangeIdsByFormula) };
    }

    private static void ValidateCaches(
        ChartContext chart,
        EmbeddedWorkbookContext workbook,
        SourceReference source,
        ICollection<ExtractionDiagnostic> workbookDiagnostics,
        ICollection<ExtractionDiagnostic> elementDiagnostics)
    {
        var ranges = workbook.ReferencedRanges.ToDictionary(range => range.Id, StringComparer.Ordinal);

        foreach (var series in chart.Plots.SelectMany(plot => plot.Series))
        {
            ValidateValue(
                series.Name,
                series.NameWorkbookRangeId,
                0,
                $"series {series.Index} name",
                ranges,
                source,
                workbookDiagnostics,
                elementDiagnostics);
            ValidateDataSource(
                series.Categories,
                $"series {series.Index} categories",
                ranges,
                source,
                workbookDiagnostics,
                elementDiagnostics);
            ValidateDataSource(
                series.Values,
                $"series {series.Index} values",
                ranges,
                source,
                workbookDiagnostics,
                elementDiagnostics);
        }
    }

    private static void ValidateDataSource(
        ChartDataSourceContext? dataSource,
        string label,
        IReadOnlyDictionary<string, WorkbookRangeContext> ranges,
        SourceReference source,
        ICollection<ExtractionDiagnostic> workbookDiagnostics,
        ICollection<ExtractionDiagnostic> elementDiagnostics)
    {
        if (dataSource?.WorkbookRangeId is null ||
            !ranges.TryGetValue(dataSource.WorkbookRangeId, out var range))
        {
            return;
        }

        foreach (var point in dataSource.Points)
        {
            ValidateValue(
                point.Value,
                dataSource.WorkbookRangeId,
                point.Index,
                $"{label} point {point.Index}",
                ranges,
                source,
                workbookDiagnostics,
                elementDiagnostics,
                point.NumericValue);
        }
    }

    private static void ValidateValue(
        string? cachedValue,
        string? rangeId,
        int offset,
        string label,
        IReadOnlyDictionary<string, WorkbookRangeContext> ranges,
        SourceReference source,
        ICollection<ExtractionDiagnostic> workbookDiagnostics,
        ICollection<ExtractionDiagnostic> elementDiagnostics,
        double? cachedNumericValue = null)
    {
        if (cachedValue is null || rangeId is null ||
            !ranges.TryGetValue(rangeId, out var range) || offset < 0 || offset >= range.Cells.Count)
        {
            return;
        }

        var cell = range.Cells[offset];
        var cellValue = cell.ResolvedValue;
        var equivalent = cachedNumericValue is not null &&
                         double.TryParse(cellValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var cellNumber)
            ? Math.Abs(cachedNumericValue.Value - cellNumber) <= 1e-9
            : string.Equals(cachedValue, cellValue, StringComparison.Ordinal);

        if (!equivalent)
        {
            AddDiagnostic(
                workbookDiagnostics,
                elementDiagnostics,
                "DCX-WORKBOOK-CACHE-MISMATCH",
                $"Chart cache for {label} is '{cachedValue}', but workbook cell {range.WorksheetName}!{cell.Reference} is '{cellValue ?? "<missing>"}'.",
                DiagnosticSeverity.Warning,
                DiagnosticOutcome.Partial,
                source);
        }
    }

    private static bool TryParseFormula(string formula, out RangeReference reference)
    {
        var match = FormulaRegex().Match(formula.Trim());

        if (!match.Success ||
            !TryParseCellReference(match.Groups["start"].Value, out var startRow, out var startColumn) ||
            !TryParseCellReference(match.Groups["end"].Success
                ? match.Groups["end"].Value
                : match.Groups["start"].Value, out var endRow, out var endColumn))
        {
            reference = default!;
            return false;
        }

        var worksheetName = match.Groups["quotedSheet"].Success
            ? match.Groups["quotedSheet"].Value.Replace("''", "'", StringComparison.Ordinal)
            : match.Groups["sheet"].Value;
        var normalizedStart = $"{ColumnName(startColumn)}{startRow}";
        var normalizedEnd = $"{ColumnName(endColumn)}{endRow}";

        reference = new RangeReference(
            worksheetName,
            Math.Min(startRow, endRow),
            Math.Max(startRow, endRow),
            Math.Min(startColumn, endColumn),
            Math.Max(startColumn, endColumn),
            normalizedStart == normalizedEnd ? normalizedStart : $"{normalizedStart}:{normalizedEnd}");
        return true;
    }

    private static bool TryParseCellReference(string value, out int row, out int column)
    {
        var match = CellRegex().Match(value);

        if (!match.Success ||
            !int.TryParse(match.Groups["row"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out row))
        {
            row = 0;
            column = 0;
            return false;
        }

        column = 0;

        foreach (var character in match.Groups["column"].Value.ToUpperInvariant())
        {
            column = checked(column * 26 + character - 'A' + 1);
        }

        return row > 0 && column > 0;
    }

    private static string ColumnName(int column)
    {
        var characters = new Stack<char>();

        while (column > 0)
        {
            column--;
            characters.Push((char)('A' + column % 26));
            column /= 26;
        }

        return new string(characters.ToArray());
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ??
                    throw new InvalidDataException($"The embedded workbook is missing required part '{path}'.");
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static string ResolvePartPath(string ownerPartPath, string target)
    {
        if (target.StartsWith("/", StringComparison.Ordinal))
        {
            return target.TrimStart('/');
        }

        var ownerDirectory = ownerPartPath[..(ownerPartPath.LastIndexOf('/') + 1)];
        var resolved = new Uri(new Uri($"http://package/{ownerDirectory}"), target);
        return resolved.AbsolutePath.TrimStart('/');
    }

    private static string CreateSuggestedFileName(string chartPartUri)
    {
        var chartName = Path.GetFileNameWithoutExtension(chartPartUri);
        return $"{chartName}-workbook.xlsx";
    }

    private static string? FindRangeId(
        string? formula,
        IReadOnlyDictionary<string, string> rangeIdsByFormula)
    {
        return formula is not null && rangeIdsByFormula.TryGetValue(formula, out var rangeId)
            ? rangeId
            : null;
    }

    private static void AddFormula(ICollection<string> formulas, string? formula)
    {
        if (!string.IsNullOrWhiteSpace(formula) && !formulas.Contains(formula, StringComparer.Ordinal))
        {
            formulas.Add(formula);
        }
    }

    private static void AddDiagnostic(
        ICollection<ExtractionDiagnostic> workbookDiagnostics,
        ICollection<ExtractionDiagnostic> elementDiagnostics,
        string code,
        string message,
        DiagnosticSeverity severity,
        DiagnosticOutcome outcome,
        SourceReference source)
    {
        var diagnostic = new ExtractionDiagnostic(
            code,
            message,
            severity,
            ExtractorName,
            outcome,
            source);
        workbookDiagnostics.Add(diagnostic);
        elementDiagnostics.Add(diagnostic);
    }

    [GeneratedRegex(
        "^(?:\\[[^\\]]+\\])?(?:'(?<quotedSheet>(?:[^']|'')+)'|(?<sheet>[^!]+))!(?<start>\\$?[A-Za-z]{1,3}\\$?\\d+)(?::(?<end>\\$?[A-Za-z]{1,3}\\$?\\d+))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex FormulaRegex();

    [GeneratedRegex("^\\$?(?<column>[A-Za-z]{1,3})\\$?(?<row>\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex CellRegex();

    private sealed record SheetDefinition(string Name, string? SheetId, string? RelationshipId);

    private sealed record SheetData(
        string PartPath,
        IReadOnlyDictionary<string, WorkbookCellContext> Cells);

    private sealed record RangeReference(
        string WorksheetName,
        int StartRow,
        int EndRow,
        int StartColumn,
        int EndColumn,
        string Address);

    private sealed record ParsedWorkbook(
        IReadOnlyList<WorkbookWorksheetContext> Worksheets,
        IReadOnlyList<WorkbookRangeContext> Ranges,
        IReadOnlyDictionary<string, string> RangeIdsByFormula);
}
