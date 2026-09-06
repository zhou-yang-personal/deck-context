using System.Globalization;
using System.Text;
using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Model;

namespace DeckContext.Export;

public sealed class DeckContextMarkdownExporter
{
    private const int MaximumMarkdownOcrCharacters = 2000;

    public string Serialize(DeckContextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();
        builder.AppendLine($"# Deck Context: {EscapeInline(document.Deck.SourceFileName)}");
        builder.AppendLine();
        builder.AppendLine($"- Extraction status: `{document.Status}`");
        builder.AppendLine($"- Slides: {document.Deck.SlideCount}");
        builder.AppendLine($"- Schema: `{document.SchemaVersion}`");
        WriteExtractionSummary(builder, document);

        var emittedImageInterpretations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slide in document.Slides.OrderBy(slide => slide.Metadata.Index))
        {
            WriteSlide(builder, slide, emittedImageInterpretations);
        }

        var diagnostics = EnumerateDiagnostics(document).ToArray();
        builder.AppendLine();
        builder.AppendLine("## Extraction diagnostics");
        builder.AppendLine();

        var visibleDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Code != "DCX-IMAGE-TEXT-PROVIDER-NOT-CONFIGURED")
            .ToArray();
        var unconfiguredImages = diagnostics
            .Where(diagnostic => diagnostic.Code == "DCX-IMAGE-TEXT-PROVIDER-NOT-CONFIGURED")
            .ToArray();

        if (visibleDiagnostics.Length == 0 && unconfiguredImages.Length == 0)
        {
            builder.AppendLine("No extraction diagnostics were reported.");
        }
        else
        {
            if (unconfiguredImages.Length > 0)
            {
                var imageCount = document.Slides
                    .SelectMany(slide => slide.Elements)
                    .Count(element => element.Image is not null);
                builder.AppendLine(
                    $"- `Information` `DCX-IMAGE-TEXT-PROVIDER-NOT-CONFIGURED` — " +
                    $"{imageCount} image placement(s) were extracted without OCR/Vision pixel interpretation.");
            }

            foreach (var diagnostic in visibleDiagnostics)
            {
                builder.AppendLine(
                    $"- `{diagnostic.Severity}` `{diagnostic.Code}` — {EscapeInline(diagnostic.Message)} " +
                    $"(extractor: `{diagnostic.Extractor}`, outcome: `{diagnostic.Outcome}`" +
                    FormatDiagnosticSource(diagnostic.Source) + ")");
            }
        }

        return Normalize(builder);
    }

    private static void WriteSlide(
        StringBuilder builder,
        SlideContext slide,
        ISet<string> emittedImageInterpretations)
    {
        builder.AppendLine();
        builder.AppendLine($"## Slide {slide.Metadata.Index}");
        builder.AppendLine();
        builder.AppendLine($"- Status: `{slide.Status}`");
        builder.AppendLine($"- Source part: `{slide.Metadata.PartUri ?? "unknown"}`");

        var title = SelectTitle(slide);

        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine($"- Title: {EscapeInline(title)}");
        }

        builder.AppendLine();
        builder.AppendLine("### Extracted content");

        var visibleElements = slide.Elements
            .Where(ShouldIncludeInContext)
            .OrderBy(ElementOrderPath, ZOrderPathComparer.Instance)
            .ToArray();

        if (visibleElements.Length == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No semantic slide content was found.");
            return;
        }

        foreach (var element in visibleElements)
        {
            WriteElement(builder, element, emittedImageInterpretations);
        }
    }

    private static void WriteElement(
        StringBuilder builder,
        SlideElementContext element,
        ISet<string> emittedImageInterpretations)
    {
        builder.AppendLine();
        var orderLabel = string.Join(".", ElementOrderPath(element).Select(position => position + 1));
        builder.AppendLine(
            $"#### {orderLabel}. {element.Kind} — {EscapeInline(element.Identity.Name ?? "Unnamed object")}");
        builder.AppendLine();

        if (element.Status != DeckContext.Domain.Extraction.ExtractionStatus.Succeeded)
        {
            builder.AppendLine($"- Extraction status: `{element.Status}`; source object `{element.Identity.Id ?? "unknown"}`");
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
            WriteImage(builder, element.Image, emittedImageInterpretations);
        }
    }

    private static void WriteText(StringBuilder builder, TextContentContext text)
    {
        var paragraphs = text.Paragraphs
            .OrderBy(paragraph => paragraph.Index)
            .Select(paragraph => new
            {
                paragraph.Index,
                paragraph.Level,
                Text = string.Concat(paragraph.Runs.Select(run => run.Text))
            })
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.Text))
            .ToArray();

        if (paragraphs.Length == 0)
        {
            return;
        }

        builder.AppendLine("- Text:");

        foreach (var paragraph in paragraphs)
        {
            builder.AppendLine(
                $"  - {EscapeInline(paragraph.Text)}" +
                (paragraph.Level is > 0 ? $" (level {paragraph.Level})" : string.Empty));
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
                $"status `{workbook.Status}`");

            foreach (var range in workbook.ReferencedRanges)
            {
                builder.AppendLine(
                    $"  - `{range.Id}` → `{range.Formula}` ({range.WorksheetName}!{range.Address})");
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

        var values = string.Join(", ", source.Points.Select(point => FormatChartPoint(point, source.NumberFormatCode)));
        builder.AppendLine(
            $"    - {label}: [{EscapeInline(values)}]" +
            FormatFormula(null, source.Formula, source.WorkbookRangeId));
    }

    private static void WriteImage(
        StringBuilder builder,
        ImageContext image,
        ISet<string> emittedImageInterpretations)
    {
        builder.AppendLine(
            $"- Image media: `{image.PartUri ?? image.ExternalUri ?? "unresolved"}`; " +
            $"relationship `{image.RelationshipId ?? "unknown"}`; status `{image.Status}`");

        if (image.Sha256 is not null)
        {
            builder.AppendLine($"- Image asset: `images/{image.SuggestedFileName}`");
        }

        if (image.AlternativeText is not null)
        {
            builder.AppendLine($"- Native alternative text: {EscapeInline(image.AlternativeText)}");
        }

        if (image.Interpretation.Status == ImageContentInterpretationStatus.Succeeded)
        {
            if (IsTesseract(image.Interpretation.ProviderId))
            {
                WriteOcrInterpretation(builder, image, emittedImageInterpretations);
                return;
            }

            builder.AppendLine($"- Pixel interpretation provider: `{image.Interpretation.ProviderId}`");

            if (!string.IsNullOrWhiteSpace(image.Interpretation.Text))
            {
                builder.AppendLine("- Recognized text:");
                builder.AppendLine($"  {EscapeInline(image.Interpretation.Text)}");
            }

            if (!string.IsNullOrWhiteSpace(image.Interpretation.Description))
            {
                builder.AppendLine("- Visual description:");
                builder.AppendLine($"  {EscapeInline(image.Interpretation.Description)}");
            }
        }
        else if (image.Interpretation.Status == ImageContentInterpretationStatus.Failed)
        {
            builder.AppendLine(
                $"- Pixel interpretation failed via `{image.Interpretation.ProviderId}`: " +
                EscapeInline(image.Interpretation.Description ?? "No provider error message was returned."));
        }
        else
        {
            builder.AppendLine("- Pixel content: not analyzed.");
        }
    }

    private static void WriteOcrInterpretation(
        StringBuilder builder,
        ImageContext image,
        ISet<string> emittedImageInterpretations)
    {
        var interpretation = image.Interpretation;
        var assessment = interpretation.TextAssessment;
        builder.AppendLine($"- OCR provider: `{interpretation.ProviderId}`");

        if (assessment is not null)
        {
            builder.AppendLine(
                $"- OCR quality: `{assessment.Quality}`; mean confidence " +
                (assessment.MeanConfidence is null
                    ? "unknown"
                    : $"{assessment.MeanConfidence.Value * 100:0.#}%"));
        }

        if (string.IsNullOrWhiteSpace(interpretation.Text))
        {
            builder.AppendLine("- OCR result: no reliable text detected.");
            return;
        }

        if (assessment is { IncludeInMarkdown: false })
        {
            builder.AppendLine(
                "- OCR result: low-quality text omitted from Markdown; " +
                "the bounded raw OCR result remains in `deck.context.json`." +
                (string.IsNullOrWhiteSpace(assessment.Reason)
                    ? string.Empty
                    : $" Reason: {EscapeInline(assessment.Reason)}"));
            return;
        }

        var interpretationKey = ImageInterpretationKey(image);
        if (!emittedImageInterpretations.Add(interpretationKey))
        {
            builder.AppendLine("- OCR result: same image and crop as an earlier placement; transcription omitted here.");
            return;
        }

        var markdownText = interpretation.Text;
        var truncatedForMarkdown = markdownText.Length > MaximumMarkdownOcrCharacters;
        if (truncatedForMarkdown)
        {
            markdownText = $"{markdownText[..MaximumMarkdownOcrCharacters].TrimEnd()}…";
        }

        builder.AppendLine(truncatedForMarkdown
            ? "- Recognized text (truncated for Markdown; fuller OCR is in `deck.context.json`):"
            : "- Recognized text:");
        builder.AppendLine($"  {EscapeInline(markdownText)}");
    }

    private static bool IsTesseract(string? providerId) =>
        providerId?.StartsWith("tesseract-local:", StringComparison.Ordinal) == true;

    private static string ImageInterpretationKey(ImageContext image)
    {
        var identity = image.Sha256 ?? image.PartUri ?? image.ExternalUri ?? image.SuggestedFileName ?? "unresolved";
        return image.Crop is null
            ? identity
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{identity}:{image.Crop.LeftRaw}:{image.Crop.TopRaw}:{image.Crop.RightRaw}:{image.Crop.BottomRaw}");
    }

    private static void WriteExtractionSummary(StringBuilder builder, DeckContextDocument document)
    {
        var elements = document.Slides.SelectMany(slide => slide.Elements).ToArray();
        var partialSlides = document.Slides.Count(slide =>
            slide.Status == DeckContext.Domain.Extraction.ExtractionStatus.Partial);
        var failedSlides = document.Slides.Count(slide =>
            slide.Status == DeckContext.Domain.Extraction.ExtractionStatus.Failed);
        var imageElements = elements.Where(element => element.Image is not null).ToArray();
        var analyzedImages = imageElements.Count(element =>
            element.Image!.Interpretation.Status == ImageContentInterpretationStatus.Succeeded);
        var usableTextImages = imageElements.Count(element =>
            element.Image!.Interpretation.Status == ImageContentInterpretationStatus.Succeeded &&
            !string.IsNullOrWhiteSpace(element.Image.Interpretation.Text) &&
            element.Image.Interpretation.TextAssessment?.IncludeInMarkdown != false);

        builder.AppendLine(
            $"- Summary: {document.Slides.Count - partialSlides - failedSlides} succeeded, " +
            $"{partialSlides} partial, {failedSlides} failed slide(s); " +
            $"{elements.Count(element => element.Chart is not null)} chart(s); " +
            $"{elements.Count(element => element.Table is not null)} table(s); " +
            $"{imageElements.Length} image placement(s), {analyzedImages} analyzed, " +
            $"{usableTextImages} with usable text");
    }

    private static string? SelectTitle(SlideContext slide)
    {
        return slide.Elements
            .Where(element => element.Text is not null)
            .Select(element => new
            {
                Element = element,
                Text = PlainText(element.Text!),
                Score = TitleScore(element)
            })
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Text))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => ElementOrderPath(candidate.Element), ZOrderPathComparer.Instance)
            .Select(candidate => candidate.Text)
            .FirstOrDefault();
    }

    private static int TitleScore(SlideElementContext element)
    {
        var score = 0;
        var name = element.Identity.Name ?? string.Empty;
        var text = element.Text is null ? string.Empty : PlainText(element.Text);

        if (name.Contains("subtitle", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("副标题", StringComparison.Ordinal))
        {
            score += 80;
        }
        else if (name.Contains("title", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("标题", StringComparison.Ordinal))
        {
            score += 100;
        }

        if (element.NormalizedGeometry is { } geometry)
        {
            if (geometry.Y <= 0.2)
            {
                score += 40;
            }

            if (geometry.Width >= 0.5)
            {
                score += 20;
            }
        }

        var maximumFontSize = element.Text is null
            ? 0
            : element.Text.Paragraphs
                .SelectMany(paragraph => paragraph.Runs)
                .Select(run => run.DirectStyle?.FontSizePoints)
                .Where(size => size is not null)
                .Select(size => size!.Value)
                .DefaultIfEmpty()
                .Max();
        if (maximumFontSize >= 24)
        {
            score += 20;
        }

        if (text.Length is >= 4 and <= 160)
        {
            score += 10;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            score -= 50;
        }

        return score;
    }

    private static bool ShouldIncludeInContext(SlideElementContext element)
    {
        if (element.Table is not null || element.Chart is not null || element.Image is not null)
        {
            return true;
        }

        if (element.Text is not null && !string.IsNullOrWhiteSpace(PlainText(element.Text)))
        {
            return true;
        }

        return element.Diagnostics.Any(diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);
    }

    private static string FormatChartPoint(ChartDataPointContext point, string? numberFormatCode)
    {
        if (point.NumericValue is null)
        {
            return point.Value ?? "<missing>";
        }

        var numericValue = point.NumericValue.Value;
        if (!string.IsNullOrWhiteSpace(numberFormatCode) &&
            numberFormatCode.Contains('%'))
        {
            return $"{FormatNumber(numericValue * 100)}%";
        }

        return FormatNumber(numericValue);
    }

    private static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("0.###############", CultureInfo.InvariantCulture);
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
