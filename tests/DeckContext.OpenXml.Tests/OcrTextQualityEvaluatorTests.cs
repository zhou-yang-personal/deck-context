using DeckContext.Domain.Model;
using DeckContext.Pipeline;

namespace DeckContext.OpenXml.Tests;

public sealed class OcrTextQualityEvaluatorTests
{
    [Theory]
    [InlineData("a", 0.94)]
    [InlineData("»", 0.99)]
    [InlineData("personal", 0.75)]
    [InlineData("A / y / | / E / 4 / NN / el", 0.47)]
    public void Evaluate_suppresses_short_or_noisy_results(string text, double confidence)
    {
        var result = OcrTextQualityEvaluator.Evaluate(text, confidence);

        Assert.False(result.Assessment.IncludeInMarkdown);
        Assert.Equal(ImageTextQuality.Low, result.Assessment.Quality);
        Assert.Equal(text, result.StoredText);
        Assert.False(string.IsNullOrWhiteSpace(result.Assessment.Reason));
    }

    [Fact]
    public void Evaluate_keeps_readable_multilingual_text_and_removes_blank_lines()
    {
        const string text = "Internet Fibra 850 Mbps\r\n\r\n价格每月 59.50\r\nPrecio regular 119";

        var result = OcrTextQualityEvaluator.Evaluate(text, 0.91);

        Assert.True(result.Assessment.IncludeInMarkdown);
        Assert.Equal(ImageTextQuality.High, result.Assessment.Quality);
        var storedText = Assert.IsType<string>(result.StoredText);
        Assert.Equal(
            $"Internet Fibra 850 Mbps{Environment.NewLine}价格每月 59.50{Environment.NewLine}Precio regular 119",
            storedText);
        Assert.DoesNotContain($"{Environment.NewLine}{Environment.NewLine}", storedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_retains_a_bounded_raw_result_when_low_confidence_text_is_suppressed()
    {
        var text = string.Join(' ', Enumerable.Repeat("geographic-place-name", 500));

        var result = OcrTextQualityEvaluator.Evaluate(text, 0.49);

        Assert.False(result.Assessment.IncludeInMarkdown);
        Assert.True(result.Assessment.WasTruncated);
        var storedText = Assert.IsType<string>(result.StoredText);
        Assert.True(storedText.Length <= OcrTextQualityEvaluator.MaximumStoredCharacters + 1);
    }

    [Fact]
    public void Evaluate_reports_no_text_without_claiming_an_interpretation()
    {
        var result = OcrTextQualityEvaluator.Evaluate(" \r\n ", 0);

        Assert.Null(result.StoredText);
        Assert.Equal(ImageTextQuality.None, result.Assessment.Quality);
        Assert.False(result.Assessment.IncludeInMarkdown);
    }
}
