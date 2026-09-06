using System.Text.Json;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;

namespace DeckContext.Export.Tests;

public sealed class DeckContextPackageExportTests
{
    [Fact]
    public void Markdown_is_deterministic_readable_and_preserves_structured_evidence()
    {
        var document = TestDocumentFactory.CreateRich();
        var exporter = new DeckContextMarkdownExporter();

        var first = exporter.Serialize(document);
        var second = exporter.Serialize(document);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", first, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", first, StringComparison.Ordinal);
        Assert.Contains("# Deck Context: sample.pptx", first, StringComparison.Ordinal);
        Assert.Contains("## Slide 1", first, StringComparison.Ordinal);
        Assert.Contains("Fiber growth is shifting", first, StringComparison.Ordinal);
        Assert.Contains("- Title: Fiber growth is shifting", first, StringComparison.Ordinal);
        Assert.Contains("- Summary:", first, StringComparison.Ordinal);
        Assert.Contains("Native table: 1 rows × 2 columns", first, StringComparison.Ordinal);
        Assert.Contains("Share \\| 35%", first, StringComparison.Ordinal);
        Assert.Contains("Native chart: `lineChart`", first, StringComparison.Ordinal);
        Assert.Contains("`range-001` → `Data!$B$2:$B$3`", first, StringComparison.Ordinal);
        Assert.DoesNotContain("| B3 | 15 | 15 | B2+3 |", first, StringComparison.Ordinal);
        Assert.Contains("Native alternative text: Market coverage map", first, StringComparison.Ordinal);
        Assert.Contains("Pixel content: not analyzed", first, StringComparison.Ordinal);
        Assert.Contains("DCX-ELEMENT-TYPE-UNSUPPORTED", first, StringComparison.Ordinal);
        Assert.Contains("source: slide 1, object `5`", first, StringComparison.Ordinal);
        Assert.DoesNotContain("Geometry (EMU)", first, StringComparison.Ordinal);
        Assert.DoesNotContain("SHA-256", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Extraction_report_flattens_diagnostics_and_counts_element_statuses()
    {
        var jsonText = new ExtractionReportSerializer().Serialize(TestDocumentFactory.CreateRich());

        using var json = JsonDocument.Parse(jsonText);
        var root = json.RootElement;
        Assert.Equal("partial", root.GetProperty("status").GetString());
        var summary = root.GetProperty("summary");
        Assert.Equal(5, summary.GetProperty("elementCount").GetInt32());
        Assert.Equal(4, summary.GetProperty("succeededElementCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("unsupportedElementCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("informationCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("warningCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("errorCount").GetInt32());
        Assert.Equal(2, root.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public void Markdown_prefers_a_semantic_title_over_a_date_and_hides_decorative_shapes()
    {
        var document = TestDocumentFactory.CreateRich();
        var originalSlide = document.Slides[0];
        var date = originalSlide.Elements[0] with
        {
            Identity = new ElementIdentity("date", "Text Box 4"),
            ZOrder = 0,
            ZOrderPath = [0],
            NormalizedGeometry = new NormalizedGeometry(0.57, 0.44, 0.29, 0.06),
            Text = Text("2025.2.28")
        };
        var title = originalSlide.Elements[0] with
        {
            Identity = new ElementIdentity("title", "Text Box 1"),
            ZOrder = 1,
            ZOrderPath = [1],
            NormalizedGeometry = new NormalizedGeometry(0.1, 0.15, 0.68, 0.24),
            Text = Text("Cable转光：“一国一策”作战路径规划汇报")
        };
        var decoration = originalSlide.Elements[0] with
        {
            Identity = new ElementIdentity("decoration", "Background Rectangle"),
            ZOrder = 2,
            ZOrderPath = [2],
            Text = null
        };
        var slide = originalSlide with
        {
            Elements = [date, title, decoration, .. originalSlide.Elements.Skip(1)]
        };

        var markdown = new DeckContextMarkdownExporter().Serialize(document with { Slides = [slide] });

        Assert.Contains("- Title: Cable转光：“一国一策”作战路径规划汇报", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Background Rectangle", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_formats_chart_percentages_without_floating_point_noise()
    {
        var document = TestDocumentFactory.CreateRich();
        var slide = document.Slides[0];
        var chartElement = slide.Elements.Single(element => element.Chart is not null);
        var chart = chartElement.Chart!;
        var plot = chart.Plots[0];
        var series = plot.Series[0];
        var values = series.Values! with
        {
            NumberFormatCode = "0.0%",
            Points = [new ChartDataPointContext(0, "-3.7999999999999999E-2", -0.038)]
        };
        var updatedSeries = series with { Values = values };
        var updatedChart = chart with
        {
            Plots = [plot with { Series = [updatedSeries] }]
        };
        var updatedElements = slide.Elements
            .Select(element => element == chartElement ? element with { Chart = updatedChart } : element)
            .ToArray();

        var markdown = new DeckContextMarkdownExporter().Serialize(
            document with { Slides = [slide with { Elements = updatedElements }] });

        Assert.Contains("Values: [-3.8%]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("-3.7999999999999999E-2", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_serialization_is_deterministic_and_traceable()
    {
        var manifest = new ContextPackageManifest(
            DeckContext.Domain.Model.DeckContextDocument.CurrentSchemaVersion,
            "sample.pptx",
            [
                new ContextPackageAsset(
                    ContextPackageAssetKind.ContextMarkdown,
                    "deck.context.md",
                    null,
                    null,
                    new string('a', 64),
                    120),
                new ContextPackageAsset(
                    ContextPackageAssetKind.EmbeddedWorkbook,
                    "workbooks/workbook1.xlsx",
                    "/ppt/embeddings/workbook1.xlsx",
                    "rId5",
                    new string('b', 64),
                    240),
            ]);
        var serializer = new ContextPackageManifestSerializer();

        var first = serializer.Serialize(manifest);
        var second = serializer.Serialize(manifest);

        Assert.Equal(first, second);
        using var json = JsonDocument.Parse(first);
        Assert.Equal("contextMarkdown", json.RootElement.GetProperty("assets")[0].GetProperty("kind").GetString());
        Assert.Equal("workbooks/workbook1.xlsx", json.RootElement
            .GetProperty("assets")[1]
            .GetProperty("relativePath")
            .GetString());
    }

    private static TextContentContext Text(string value) =>
        new([new TextParagraphContext(0, 0, "l", null, [new TextRunContext(TextRunKind.Text, value, null)])]);
}
