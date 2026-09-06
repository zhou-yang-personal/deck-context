using System.Text.Json;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;

namespace DeckContext.Export.Tests;

public sealed class DeckContextJsonSerializerTests
{
    [Fact]
    public void Serialize_is_deterministic_and_uses_the_stable_context_contract()
    {
        var document = new DeckContextDocument(
            DeckContextDocument.CurrentSchemaVersion,
            new DeckMetadata("sample.pptx", "/ppt/presentation.xml", 100, 50, 1),
            [
                new SlideContext(
                    new SlideMetadata(1, "256", "rId1", "/ppt/slides/slide1.xml", 100, 50),
                    [],
                    ExtractionStatus.Succeeded,
                    []),
            ],
            ExtractionStatus.Succeeded,
            []);
        var serializer = new DeckContextJsonSerializer();

        var first = serializer.Serialize(document);
        var second = serializer.Serialize(document);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", first, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", first, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(first);
        var root = json.RootElement;
        Assert.Equal("0.4", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("sample.pptx", root.GetProperty("deck").GetProperty("sourceFileName").GetString());
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        Assert.Equal(1, root.GetProperty("slides").GetArrayLength());
    }

    [Fact]
    public void Serialize_preserves_the_native_chart_contract_deterministically()
    {
        var labels = new ChartDataLabelsContext(
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        var chart = new ChartContext(
            "rId5",
            "/ppt/charts/chart1.xml",
            "Revenue",
            [
                new ChartPlotContext(
                    "lineChart",
                    [
                        new ChartSeriesContext(
                            0,
                            0,
                            "Actual",
                            "Data!$B$1",
                            new ChartDataSourceContext(
                                "Data!$A$2:$A$3",
                                null,
                                [new ChartDataPointContext(0, "2025", 2025d)]),
                            new ChartDataSourceContext(
                                "Data!$B$2:$B$3",
                                "$0.0",
                                [new ChartDataPointContext(0, "12.5", 12.5d)])),
                    ],
                    labels),
            ],
            new ChartLegendContext(
                true,
                "r",
                false,
                [new ChartLegendEntryContext(0, "Actual", true)]),
            [],
            null,
            null);
        var element = new SlideElementContext(
            new ElementIdentity("9", "Revenue Chart"),
            ElementKind.Chart,
            new SourceReference("sample.pptx", "/ppt/slides/slide1.xml", "rId1", 1, "9", "Revenue Chart"),
            0,
            null,
            null,
            ExtractionStatus.Succeeded,
            [],
            Chart: chart);
        var document = new DeckContextDocument(
            DeckContextDocument.CurrentSchemaVersion,
            new DeckMetadata("sample.pptx", "/ppt/presentation.xml", 100, 50, 1),
            [
                new SlideContext(
                    new SlideMetadata(1, "256", "rId1", "/ppt/slides/slide1.xml", 100, 50),
                    [element],
                    ExtractionStatus.Succeeded,
                    []),
            ],
            ExtractionStatus.Succeeded,
            []);
        var serializer = new DeckContextJsonSerializer();

        var first = serializer.Serialize(document);
        var second = serializer.Serialize(document);

        Assert.Equal(first, second);
        using var json = JsonDocument.Parse(first);
        var serializedElement = json.RootElement
            .GetProperty("slides")[0]
            .GetProperty("elements")[0];
        Assert.Equal("chart", serializedElement.GetProperty("kind").GetString());
        var serializedChart = serializedElement.GetProperty("chart");
        Assert.Equal("rId5", serializedChart.GetProperty("relationshipId").GetString());
        Assert.Equal("lineChart", serializedChart.GetProperty("plots")[0].GetProperty("type").GetString());
        Assert.Equal("Data!$B$2:$B$3", serializedChart
            .GetProperty("plots")[0]
            .GetProperty("series")[0]
            .GetProperty("values")
            .GetProperty("formula")
            .GetString());
    }

    [Fact]
    public void Serialize_explicitly_preserves_image_provenance_and_not_configured_interpretation()
    {
        var image = new ImageContext(
            "rId2",
            "/ppt/media/image1.png",
            null,
            "image/png",
            ".png",
            "image1.png",
            128,
            new string('a', 64),
            "Market map",
            "Coverage",
            new ImageCropContext(1000, 0, 0, 0, 0.01d, 0d, 0d, 0d),
            new ImageTransformContext(null, null, false, false),
            new ImageContentInterpretationContext(
                ImageContentInterpretationStatus.NotConfigured,
                null,
                null,
                null),
            ExtractionStatus.Succeeded);
        var element = new SlideElementContext(
            new ElementIdentity("10", "Market Map"),
            ElementKind.Picture,
            new SourceReference("sample.pptx", "/ppt/slides/slide1.xml", "rId1", 1, "10", "Market Map"),
            0,
            null,
            null,
            ExtractionStatus.Succeeded,
            [],
            Image: image);
        var document = new DeckContextDocument(
            DeckContextDocument.CurrentSchemaVersion,
            new DeckMetadata("sample.pptx", "/ppt/presentation.xml", 100, 50, 1),
            [
                new SlideContext(
                    new SlideMetadata(1, "256", "rId1", "/ppt/slides/slide1.xml", 100, 50),
                    [element],
                    ExtractionStatus.Succeeded,
                    []),
            ],
            ExtractionStatus.Succeeded,
            []);

        var jsonText = new DeckContextJsonSerializer().Serialize(document);

        using var json = JsonDocument.Parse(jsonText);
        var serializedImage = json.RootElement
            .GetProperty("slides")[0]
            .GetProperty("elements")[0]
            .GetProperty("image");
        Assert.Equal("/ppt/media/image1.png", serializedImage.GetProperty("partUri").GetString());
        Assert.Equal("market map", serializedImage.GetProperty("alternativeText").GetString()?.ToLowerInvariant());
        Assert.Equal("notConfigured", serializedImage
            .GetProperty("interpretation")
            .GetProperty("status")
            .GetString());
        Assert.False(serializedImage.GetProperty("interpretation").TryGetProperty("text", out _));
    }

    [Fact]
    public void Serialize_preserves_filtered_raw_ocr_and_its_quality_assessment()
    {
        var document = TestDocumentFactory.CreateRich();
        var slide = document.Slides[0];
        var imageElement = slide.Elements.Single(element => element.Image is not null);
        var filteredInterpretation = new ImageContentInterpretationContext(
            ImageContentInterpretationStatus.Succeeded,
            "tesseract-local:chi_sim+eng+spa",
            "A / y / | / NN",
            "Low-quality OCR text was retained only in JSON.",
            new ImageTextAssessmentContext(
                0.47,
                ImageTextQuality.Low,
                false,
                "Mean OCR confidence was below 50%.",
                false));
        var updatedImageElement = imageElement with
        {
            Image = imageElement.Image! with { Interpretation = filteredInterpretation }
        };
        var updatedElements = slide.Elements
            .Select(element => element == imageElement ? updatedImageElement : element)
            .ToArray();

        var jsonText = new DeckContextJsonSerializer().Serialize(
            document with { Slides = [slide with { Elements = updatedElements }] });

        using var json = JsonDocument.Parse(jsonText);
        var interpretation = json.RootElement
            .GetProperty("slides")[0]
            .GetProperty("elements")
            .EnumerateArray()
            .Single(element => element.TryGetProperty("image", out _))
            .GetProperty("image")
            .GetProperty("interpretation");
        Assert.Equal("A / y / | / NN", interpretation.GetProperty("text").GetString());
        Assert.Equal("low", interpretation.GetProperty("textAssessment").GetProperty("quality").GetString());
        Assert.False(interpretation.GetProperty("textAssessment").GetProperty("includeInMarkdown").GetBoolean());
    }

    [Fact]
    public void Serialize_preserves_the_filtered_markdown_text_separately_from_raw_ocr()
    {
        var document = TestDocumentFactory.CreateRich();
        var slide = document.Slides[0];
        var imageElement = slide.Elements.Single(element => element.Image is not null);
        var interpretation = new ImageContentInterpretationContext(
            ImageContentInterpretationStatus.Succeeded,
            "tesseract-local:chi_sim+eng+spa",
            "win\nEL INTERNET DE LOS WINNERS\nA",
            null,
            new ImageTextAssessmentContext(
                0.91,
                ImageTextQuality.High,
                true,
                null,
                false,
                "EL INTERNET DE LOS WINNERS"));
        var updatedElements = slide.Elements
            .Select(element => element == imageElement
                ? element with { Image = element.Image! with { Interpretation = interpretation } }
                : element)
            .ToArray();

        var jsonText = new DeckContextJsonSerializer().Serialize(
            document with { Slides = [slide with { Elements = updatedElements }] });

        using var json = JsonDocument.Parse(jsonText);
        var serializedInterpretation = json.RootElement
            .GetProperty("slides")[0]
            .GetProperty("elements")
            .EnumerateArray()
            .Single(element => element.TryGetProperty("image", out _))
            .GetProperty("image")
            .GetProperty("interpretation");
        Assert.Contains("win", serializedInterpretation.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(
            "EL INTERNET DE LOS WINNERS",
            serializedInterpretation.GetProperty("textAssessment").GetProperty("markdownText").GetString());
    }
}
