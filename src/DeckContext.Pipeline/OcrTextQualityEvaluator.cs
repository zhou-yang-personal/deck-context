using System.Globalization;
using DeckContext.Domain.Model;

namespace DeckContext.Pipeline;

internal static class OcrTextQualityEvaluator
{
    internal const int MaximumStoredCharacters = 6000;
    internal const int MaximumFilteredCharacters = 2000;

    public static OcrTextEvaluation Evaluate(string? text, double meanConfidence)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new OcrTextEvaluation(
                null,
                new ImageTextAssessmentContext(
                    meanConfidence,
                    ImageTextQuality.None,
                    false,
                    "No readable text was detected.",
                    false));
        }

        var normalized = Normalize(text);
        if (normalized is null)
        {
            return new OcrTextEvaluation(
                null,
                new ImageTextAssessmentContext(
                    meanConfidence,
                    ImageTextQuality.None,
                    false,
                    "No readable text remained after whitespace normalization.",
                    false));
        }

        var wasTruncated = normalized.Length > MaximumStoredCharacters;
        var storedText = Bound(normalized, MaximumStoredCharacters);
        var filteredText = BuildMarkdownText(normalized);

        if (filteredText is null)
        {
            return new OcrTextEvaluation(
                storedText,
                new ImageTextAssessmentContext(
                    meanConfidence,
                    ImageTextQuality.Low,
                    false,
                    "No OCR line met the minimum continuous-text structure.",
                    wasTruncated));
        }

        var nonWhitespace = filteredText.Count(character => !char.IsWhiteSpace(character));
        var meaningful = filteredText.Count(char.IsLetterOrDigit);
        var meaningfulRatio = nonWhitespace == 0 ? 0 : (double)meaningful / nonWhitespace;
        var tokens = filteredText.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var singleCharacterTokens = tokens.Count(token => token.Count(char.IsLetterOrDigit) <= 1);
        var singleCharacterRatio = tokens.Length == 0
            ? 1
            : (double)singleCharacterTokens / tokens.Length;

        var reason = RejectReason(
            meanConfidence,
            meaningful,
            meaningfulRatio,
            tokens.Length,
            singleCharacterRatio,
            filteredText.Count(IsCjk));
        if (reason is not null)
        {
            return new OcrTextEvaluation(
                storedText,
                new ImageTextAssessmentContext(
                    meanConfidence,
                    ImageTextQuality.Low,
                    false,
                    reason,
                    wasTruncated));
        }

        var markdownText = Bound(filteredText, MaximumFilteredCharacters);
        var quality = meanConfidence >= 0.82 && meaningful >= 20 && meaningfulRatio >= 0.72 &&
                      singleCharacterRatio <= 0.15
            ? ImageTextQuality.High
            : ImageTextQuality.Medium;
        return new OcrTextEvaluation(
            storedText,
            new ImageTextAssessmentContext(
                meanConfidence,
                quality,
                true,
                null,
                wasTruncated,
                markdownText));
    }

    private static string? RejectReason(
        double meanConfidence,
        int meaningfulCharacters,
        double meaningfulRatio,
        int tokenCount,
        double singleCharacterRatio,
        int cjkCharacters)
    {
        if (meanConfidence < 0.6)
        {
            return "Mean OCR confidence was below 60%.";
        }

        if (meaningfulCharacters < 12)
        {
            return "Too few letters or digits remained after line-level noise filtering.";
        }

        if (meaningfulRatio < 0.62)
        {
            return "The result contained too much punctuation or symbol noise.";
        }

        if (singleCharacterRatio > 0.25)
        {
            return "Too many isolated single-character tokens indicate visual noise rather than continuous text.";
        }

        if (meanConfidence < 0.75 &&
            (meaningfulCharacters < 24 || (tokenCount < 5 && cjkCharacters < 6)))
        {
            return "Medium-confidence OCR lacked enough continuous text to publish safely.";
        }

        return null;
    }

    private static string? BuildMarkdownText(string normalized)
    {
        var usefulLines = normalized
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsUsefulLine)
            .ToArray();
        return usefulLines.Length == 0 ? null : string.Join(Environment.NewLine, usefulLines);
    }

    private static bool IsUsefulLine(string line)
    {
        var nonWhitespace = line.Count(character => !char.IsWhiteSpace(character));
        var meaningful = line.Count(char.IsLetterOrDigit);
        var meaningfulRatio = nonWhitespace == 0 ? 0 : (double)meaningful / nonWhitespace;
        var cjkCharacters = line.Count(IsCjk);

        if (cjkCharacters >= 4)
        {
            return meaningfulRatio >= 0.62;
        }

        var tokens = line.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (meaningful < 12 || meaningfulRatio < 0.62 || tokens.Length < 3)
        {
            return false;
        }

        var tokenLengths = tokens
            .Select(token => token.Count(char.IsLetterOrDigit))
            .ToArray();
        if (tokens.Length == 3 &&
            (meaningful < 16 ||
             !tokens.Any(token => token.Any(char.IsDigit)) ||
             !tokens.Any(token => token.Count(char.IsLetter) >= 6)))
        {
            return false;
        }

        var singleCharacterRatio = (double)tokenLengths.Count(length => length <= 1) / tokenLengths.Length;
        var averageTokenLength = (double)tokenLengths.Sum() / tokenLengths.Length;
        return singleCharacterRatio <= 0.2 && averageTokenLength >= 2.5;
    }

    private static bool IsCjk(char character) =>
        character is >= '\u3400' and <= '\u4DBF' or
            >= '\u4E00' and <= '\u9FFF' or
            >= '\uF900' and <= '\uFAFF';

    private static string Bound(string value, int maximumCharacters) =>
        value.Length > maximumCharacters
            ? $"{value[..maximumCharacters].TrimEnd()}…"
            : value;

    private static string? Normalize(string value)
    {
        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(NormalizeLine)
            .Where(line => line.Length > 0)
            .ToArray();
        return lines.Length == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeLine(string line)
    {
        var builder = new System.Text.StringBuilder(line.Length);
        var previousWasWhitespace = false;

        foreach (var character in line.Trim())
        {
            if (char.GetUnicodeCategory(character) is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }
}

internal sealed record OcrTextEvaluation(
    string? StoredText,
    ImageTextAssessmentContext Assessment);
