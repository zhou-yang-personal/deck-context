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
    [InlineData("movistor", 0.88)]
    [InlineData("CALL\n..\nat", 0.84)]
    [InlineData("CALL\nwf\n2!", 0.61)]
    [InlineData("..\nE\n.\n238 21.36%\nmain", 0.67)]
    [InlineData("4 M Main", 0.68)]
    [InlineData("> Totalplay", 0.83)]
    [InlineData("de\na\nFTTH", 0.81)]
    public void Evaluate_suppresses_short_or_noisy_results(string text, double confidence)
    {
        var result = OcrTextQualityEvaluator.Evaluate(text, confidence);

        Assert.False(result.Assessment.IncludeInMarkdown);
        Assert.Equal(ImageTextQuality.Low, result.Assessment.Quality);
        Assert.Equal(text.Replace("\n", Environment.NewLine, StringComparison.Ordinal), result.StoredText);
        Assert.Null(result.Assessment.MarkdownText);
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
        Assert.Equal(storedText, result.Assessment.MarkdownText);
        Assert.DoesNotContain($"{Environment.NewLine}{Environment.NewLine}", storedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_keeps_useful_lines_and_drops_short_map_or_icon_fragments()
    {
        const string text =
            "win\nEL INTERNET DE LOS WINNERS\nA\n..\n100% Fibra Optica 850 Mbps\n2!";

        var result = OcrTextQualityEvaluator.Evaluate(text, 0.91);

        Assert.True(result.Assessment.IncludeInMarkdown);
        Assert.Equal(
            $"EL INTERNET DE LOS WINNERS{Environment.NewLine}100% Fibra Optica 850 Mbps",
            result.Assessment.MarkdownText);
        Assert.Contains("win", result.StoredText, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_keeps_substantial_chinese_ui_text_at_medium_confidence()
    {
        const string text =
            "欢迎回家!\n云相册\n云视频套餐管理\n我的家\n客厅摄像机\n室内摄像头传输加密\nA\n|";

        var result = OcrTextQualityEvaluator.Evaluate(text, 0.67);

        Assert.True(result.Assessment.IncludeInMarkdown);
        Assert.Equal(ImageTextQuality.Medium, result.Assessment.Quality);
        var markdownText = Assert.IsType<string>(result.Assessment.MarkdownText);
        Assert.Contains("欢迎回家", markdownText, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Environment.NewLine}A", markdownText, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_suppresses_map_place_name_fragments_without_continuous_text()
    {
        const string text =
            "VICENTE LOPEZ\nVilla Ballester\nGeneral San Martin\nDock Sud\nLa Tablada\nMain 241 21.58%";

        var result = OcrTextQualityEvaluator.Evaluate(text, 0.75);

        Assert.False(result.Assessment.IncludeInMarkdown);
        Assert.Null(result.Assessment.MarkdownText);
        Assert.NotNull(result.StoredText);
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
