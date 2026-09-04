using System.Globalization;
using System.Text;
using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Model;

namespace DeckContext.Export;

public sealed class DeckContextMarkdownExporter
{
    public string Serialize(DeckContextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();
        builder.AppendLine($"# Deck Context: {EscapeInline(document.Deck.SourceFileName)}");
        builder.AppendLine();
        builder.AppendLine($"- Extraction status: `{document.Status}`");
        builder.AppendLine($"- Slides: {document.Deck.SlideCount}");

        if (document.Deck.SlideWidthEmu is not null && document.Deck.SlideHeightEmu is not null)
        {
            builder.AppendLine(
                $"- Slide canvas: {document.Deck.SlideWidthEmu} × {document.Deck.SlideHeightEmu} EMU");
        }

        builder.AppendLine($"- Schema: `{document.SchemaVersion}`");

        foreach (var slide in document.Slides.OrderBy(slide => slide.Metadata.Index))
        {
            WriteSlide(builder, slide);
        }

        var diagnostics = EnumerateDiagnostics(document).ToArray();
        builder.AppendLine();
        builder.AppendLine("## Extraction diagnostics");
        builder.AppendLine();

        if (diagnostics.Length == 0)
        {
            builder.AppendLine("No extraction diagnostics were reported.");
        }
        else
        {
            foreach (var diagnostic in diagnostics)
            {
                builder.AppendLine(
                    $"- `{diagnostic.Severity}` `{diagnostic.Code}` — {EscapeInline(diagnostic.Message)} " +
                    $"(extractor: `{diagnostic.Extractor}`, outcome: `{diagnostic.Outcome}`" +
                    FormatDiagnosticSource(diagnostic.Source) + ")");
            }
        }

        return Normalize(builder);
    }

    private static void WriteSlide(StringBuilder builder, SlideContext slide)
    {
        builder.AppendLine();
        builder.AppendLine($"## Slide {slide.Metadata.Index}");
        builder.AppendLine();
        builder.AppendLine($"- Status: `{slide.Status}`");
        builder.AppendLine($"- Source part: `{slide.Metadata.PartUri ?? "unknown"}`");

        var title = slide.Elements
            .OrderBy(ElementOrderPath, ZOrderPathComparer.Instance)
            .Where(element => element.Text is not null)
            .Select(element => PlainText(element.Text!))
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine($"- First text/title candidate: {EscapeInline(title)}");
        }

        builder.AppendLine();
        builder.AppendLine("### Objects in source order");

        if (slide.Elements.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No supported slide objects were found.");
            return;
        }

        foreach (var element in slide.Elements.OrderBy(ElementOrderPath, ZOrderPathComparer.Instance))
        {
            WriteElement(builder, element);
        }
    }

    private static void WriteElement(StringBuilder builder, SlideElementContext element)
    {
        builder.AppendLine();
        var orderLabel = string.Join(".", ElementOrderPath(element).Select(position => position + 1));
        builder.AppendLine(
            $"#### {orderLabel}. {element.Kind} — {EscapeInline(element.Identity.Name ?? "Unnamed object")}");
        builder.AppendLine();
        builder.AppendLine(
            $"- Source: slide {element.Source.SlideIndex?.ToString(CultureInfo.InvariantCulture) ?? "?"}, " +
            $"object id `{element.Identity.Id ?? "unknown"}`, status `{element.Status}`");

        if (element.ParentGroupId is not null)
        {
            builder.AppendLine($"- Parent group id: `{element.ParentGroupId}`");
        }

        WriteGeometry(builder, element);

        if (element.GroupTransform is not null)
        {
            var transform = element.GroupTransform;
            builder.AppendLine(
                $"- Group child coordinates: offset=({transform.ChildOffsetX}, {transform.ChildOffsetY}), " +
                $"extent=({transform.ChildExtentWidth}, {transform.ChildExtentHeight}), " +
                $"rotation={Format(transform.RotationDegrees ?? 0d)}°, " +
                $"flipH={transform.FlipHorizontal is true}, flipV={transform.FlipVertical is true}");
        }

        if (element.Text is not null)
        {
            WriteText(builder, element.Text);
        }

        if (element.Table is not null)
        {
            WriteTable(builder, element.Table);
        }

        if (element.Chart is not null)
        {
            WriteChart(builder, element.Chart);
        }

        if (element.Image is not null)
        {
            WriteImage(builder, element.Image);
        }
    }

    private static void WriteGeometry(StringBuilder builder, SlideElementContext element)
    {
        if (element.NativeGeometry is not null)
        {
            var geometry = element.NativeGeometry;
            builder.AppendLine(
                $"- Geometry (EMU): x={geometry.X}, y={geometry.Y}, width={geometry.Width}, " +
                $"height={geometry.Height}, space=`{geometry.CoordinateSpace}`");
        }

        if (element.NormalizedGeometry is not null)
        {
            var geometry = element.NormalizedGeometry;
            builder.AppendLine(
                $"- Normalized geometry: x={Format(geometry.X)}, y={Format(geometry.Y)}, " +
                $"width={Format(geometry.Width)}, height={Format(geometry.Height)}");
        }
    }

    private static void WriteText(StringBuilder builder, TextContentContext text)
    {
        builder.AppendLine("- Text:");

        foreach (var paragraph in text.Paragraphs.OrderBy(paragraph => paragraph.Index))
        {
            var paragraphText = string.Concat(paragraph.Runs.Select(run => run.Text));

            if (!string.IsNullOrEmpty(paragraphText))
            {
                builder.AppendLine(
                    $"  - P{paragraph.Index + 1}" +
                    (paragraph.Level is null ? string.Empty : $" (level {paragraph.Level})") +
                    $": {EscapeInline(paragraphText)}");
            }
        }
    }

    private static void WriteTable(StringBuilder builder, TableContext table)
    {
        builder.AppendLine($"- Native table: {table.RowCount} rows × {table.ColumnCount} columns");
        builder.AppendLine();

        if (table.ColumnCount == 0)
        {
            return;
        }

        builder.Append('|');

        for (var column = 0; column < table.ColumnCount; column++)
        {
            builder.Append($" Column {column + 1} |");
        }

        builder.AppendLine();
        builder.Append('|');

        for (var column = 0; column < table.ColumnCount; column++)
        {
            builder.Append(" --- |");
        }

        builder.AppendLine();

        foreach (var row in table.Rows.OrderBy(row => row.Index))
        {
            builder.Append('|');

            foreach (var cell in row.Cells.OrderBy(cell => cell.ColumnIndex))
            {
                var value = PlainText(cell.Text);

                if (cell.IsHorizontalMergeContinuation || cell.IsVerticalMergeContinuation)
                {
                    value = "[merged continuation]";
                }
                else if (cell.RowSpan > 1 || cell.ColumnSpan > 1)
                {
                    value = $"{value} [span {cell.RowSpan}×{cell.ColumnSpan}]";
                }

                builder.Append($" {EscapeTable(value)} |");
            }

            builder.AppendLine();
        }
    }

    private static void WriteChart(StringBuilder builder, ChartContext chart)
    {
        builder.AppendLine(
            $"- Native chart: `{string.Join(", ", chart.Plots.Select(plot => plot.Type))}`; " +
            $"title: {EscapeInline(chart.Title ?? "none")}; part: `{chart.PartUri}`");

        foreach (var plot in chart.Plots)
        {
            foreach (var series in plot.Series.OrderBy(series => series.Order))
            {
                builder.AppendLine(
                    $"  - Series {series.Index}: {EscapeInline(series.Name ?? "unnamed")}" +
                    FormatFormula("name", series.NameFormula, series.NameWorkbookRangeId));
                WriteDataSource(builder, "Categories", series.Categories);
                WriteDataSource(builder, "Values", series.Values);
                WriteDataSource(builder, "Bubble sizes", series.BubbleSizes);
            }
        }

        if (chart.EmbeddedWorkbook is not null)
        {
            var workbook = chart.EmbeddedWorkbook;
            builder.AppendLine(
                $"- Embedded workbook: `{workbook.PartUri}`; relationship `{workbook.RelationshipId}`; " +
                $"SHA-256 `{workbook.Sha256}`; status `{workbook.Status}`");

            foreach (var range in workbook.ReferencedRanges)
            {
                builder.AppendLine(
                    $"  - `{range.Id}` → `{range.Formula}` ({range.WorksheetName}!{range.Address})");
                builder.AppendLine();
                builder.AppendLine("    | Cell | Raw value | Resolved value | Formula |");
                builder.AppendLine("    | --- | --- | --- | --- |");

                foreach (var cell in range.Cells)
                {
                    builder.AppendLine(
                        $"    | {EscapeTable(cell.Reference)} | {EscapeTable(cell.RawValue)} | " +
                        $"{EscapeTable(cell.ResolvedValue)} | {EscapeTable(cell.Formula)} |");
                }
            }
        }
    }

    private static void WriteDataSource(
        StringBuilder builder,
        string label,
        ChartDataSourceContext? source)
    {
        if (source is null)
        {
            return;
        }

        var values = string.Join(", ", source.Points.Select(point => point.Value ?? "<missing>"));
        builder.AppendLine(
            $"    - {label}: [{EscapeInline(values)}]" +
            FormatFormula(null, source.Formula, source.WorkbookRangeId));
    }

    private static void WriteImage(StringBuilder builder, ImageContext image)
    {
        builder.AppendLine(
            $"- Image media: `{image.PartUri ?? image.ExternalUri ?? "unresolved"}`; " +
            $"relationship `{image.RelationshipId ?? "unknown"}`; status `{image.Status}`");

        if (image.Sha256 is not null)
        {
            builder.AppendLine(
                $"- Image asset: `{image.SuggestedFileName}`; `{image.ContentType}`; " +
                $"{image.SizeBytes} bytes; SHA-256 `{image.Sha256}`");
        }

        if (image.AlternativeText is not null)
        {
            builder.AppendLine($"- Native alternative text: {EscapeInline(image.AlternativeText)}");
        }

        if (image.Crop is not null)
        {
            builder.AppendLine(
                $"- Crop fractions: left={Format(image.Crop.LeftFraction)}, top={Format(image.Crop.TopFraction)}, " +
                $"right={Format(image.Crop.RightFraction)}, bottom={Format(image.Crop.BottomFraction)}");
        }

        builder.AppendLine(image.Interpretation.Status == ImageContentInterpretationStatus.NotConfigured
            ? "- Pixel content: not analyzed (no OCR/Vision provider configured)."
            : $"- Pixel content status: `{image.Interpretation.Status}`; provider: `{image.Interpretation.ProviderId}`");
    }

    private static string FormatFormula(string? label, string? formula, string? rangeId)
    {
        if (formula is null)
        {
            return string.Empty;
        }

        var prefix = label is null ? string.Empty : $"; {label} ";
        return $"{prefix} source formula `{formula}`" +
               (rangeId is null ? string.Empty : $" → `{rangeId}`");
    }

    private static string PlainText(TextContentContext text)
    {
        return string.Join(
            " / ",
            text.Paragraphs
                .OrderBy(paragraph => paragraph.Index)
                .Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text)))
                .Where(value => !string.IsNullOrEmpty(value)));
    }

    private static IEnumerable<ExtractionDiagnostic> EnumerateDiagnostics(DeckContextDocument document)
    {
        foreach (var diagnostic in document.Diagnostics)
        {
            yield return diagnostic;
        }

        foreach (var slide in document.Slides.OrderBy(slide => slide.Metadata.Index))
        {
            foreach (var diagnostic in slide.Diagnostics)
            {
                yield return diagnostic;
            }

            foreach (var element in slide.Elements.OrderBy(ElementOrderPath, ZOrderPathComparer.Instance))
            {
                foreach (var diagnostic in element.Diagnostics)
                {
                    yield return diagnostic;
                }
            }
        }
    }

    private static string EscapeInline(string value)
    {
        return value.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " / ", StringComparison.Ordinal)
            .Replace("`", "'", StringComparison.Ordinal);
    }

    private static IReadOnlyList<int> ElementOrderPath(SlideElementContext element) =>
        element.ZOrderPath ?? [element.ZOrder];

    private static string FormatDiagnosticSource(SourceReference? source)
    {
        if (source is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        if (source.SlideIndex is not null)
        {
            parts.Add($"slide {source.SlideIndex.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (source.ElementId is not null)
        {
            parts.Add($"object `{EscapeInline(source.ElementId)}`");
        }

        if (source.PartUri is not null)
        {
            parts.Add($"part `{EscapeInline(source.PartUri)}`");
        }

        if (source.RelationshipId is not null)
        {
            parts.Add($"relationship `{EscapeInline(source.RelationshipId)}`");
        }

        return parts.Count == 0 ? string.Empty : $", source: {string.Join(", ", parts)}";
    }

    private static string EscapeTable(string? value)
    {
        return EscapeInline(value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string Format(double value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string Normalize(StringBuilder builder)
    {
        return $"{builder.ToString().TrimEnd().Replace("\r\n", "\n", StringComparison.Ordinal)}\n";
    }

    private sealed class ZOrderPathComparer : IComparer<IReadOnlyList<int>>
    {
        public static ZOrderPathComparer Instance { get; } = new();

        public int Compare(IReadOnlyList<int>? left, IReadOnlyList<int>? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var sharedLength = Math.Min(left.Count, right.Count);

            for (var index = 0; index < sharedLength; index++)
            {
                var comparison = left[index].CompareTo(right[index]);

                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return left.Count.CompareTo(right.Count);
        }
    }
}
