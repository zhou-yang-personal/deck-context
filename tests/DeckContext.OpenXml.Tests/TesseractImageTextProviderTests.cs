using DeckContext.Application.Contracts;
using DeckContext.Domain.Model;
using DeckContext.Pipeline;

namespace DeckContext.OpenXml.Tests;

public sealed class TesseractImageTextProviderTests
{
    [Fact]
    public async Task Analyze_reports_missing_bundled_language_data_without_throwing()
    {
        using var directory = new TemporaryDirectory();
        using var provider = new TesseractImageTextProvider(directory.Path);

        var result = await provider.AnalyzeAsync(
            Request([1, 2, 3]),
            TestContext.Current.CancellationToken);

        Assert.Equal(ImageContentInterpretationStatus.Failed, result.Status);
        Assert.StartsWith("tesseract-local:", result.ProviderId);
        Assert.Contains("chi_sim.traineddata", result.Description, StringComparison.Ordinal);
        Assert.Contains("eng.traineddata", result.Description, StringComparison.Ordinal);
        Assert.Contains("spa.traineddata", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_recognizes_text_with_the_packaged_offline_engine()
    {
        var dataDirectory = RequireEnvironmentPath("DECKCONTEXT_TEST_OCR_DATA");
        var imagePath = RequireEnvironmentPath("DECKCONTEXT_TEST_OCR_IMAGE");
        using var provider = new TesseractImageTextProvider(dataDirectory, "eng");

        var result = await provider.AnalyzeAsync(
            Request(await File.ReadAllBytesAsync(imagePath, TestContext.Current.CancellationToken)),
            TestContext.Current.CancellationToken);

        Assert.Equal(ImageContentInterpretationStatus.Succeeded, result.Status);
        Assert.Contains("This is a lot of 12 point text", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Offline OCR", result.Description, StringComparison.Ordinal);
        var assessment = Assert.IsType<ImageTextAssessmentContext>(result.TextAssessment);
        Assert.True(assessment.IncludeInMarkdown);
        Assert.True(assessment.MeanConfidence > 0);
    }

    private static ImageTextRequest Request(byte[] content) =>
        new(
            "image/tiff",
            "/ppt/media/phototest.tif",
            content,
            new SourceReference("sample.pptx", SlideIndex: 1, ElementId: "4"));

    private static string RequireEnvironmentPath(string variableName)
    {
        var path = Environment.GetEnvironmentVariable(variableName);
        Assert.False(string.IsNullOrWhiteSpace(path), $"{variableName} must be set for the OCR integration test.");
        Assert.True(File.Exists(path) || Directory.Exists(path), $"{variableName} does not exist: {path}");
        return path!;
    }
}
