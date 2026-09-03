using System.Text.Json;
using DeckContext.Domain.Extraction;

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
        Assert.Contains("Native table: 1 rows × 2 columns", first, StringComparison.Ordinal);
        Assert.Contains("Share \\| 35%", first, StringComparison.Ordinal);
        Assert.Contains("Native chart: `lineChart`", first, StringComparison.Ordinal);
        Assert.Contains("`range-001` → `Data!$B$2:$B$3`", first, StringComparison.Ordinal);
        Assert.Contains("| B3 | 15 | 15 | B2+3 |", first, StringComparison.Ordinal);
        Assert.Contains("Native alternative text: Market coverage map", first, StringComparison.Ordinal);
        Assert.Contains("Pixel content: not analyzed", first, StringComparison.Ordinal);
        Assert.Contains("DCX-ELEMENT-TYPE-UNSUPPORTED", first, StringComparison.Ordinal);
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
    public void Manifest_serialization_is_deterministic_and_traceable()
    {
        var manifest = new ContextPackageManifest(
            "0.1",
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
}
