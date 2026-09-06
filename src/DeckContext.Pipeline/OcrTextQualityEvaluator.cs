using System.Globalization;
using DeckContext.Domain.Model;

namespace DeckContext.Pipeline;

internal static class OcrTextQualityEvaluator
{
    internal const int MaximumStoredCharacters = 6000;

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
        var storedText = wasTruncated
            ? $"{normalized[..MaximumStoredCharacters].TrimEnd()}…"
            : normalized;
        var nonWhitespace = normalized.Count(character => !char.IsWhiteSpace(character));
        var meaningful = normalized.Count(char.IsLetterOrDigit);
        var meaningfulRatio = nonWhitespace == 0 ? 0 : (double)meaningful / nonWhitespace;
        var tokens = normalized.Split(
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
            singleCharacterRatio);
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

        var quality = meanConfidence >= 0.82 && meaningful >= 12 && meaningfulRatio >= 0.7 &&
                      singleCharacterRatio <= 0.25
            ? ImageTextQuality.High
            : ImageTextQuality.Medium;
        return new OcrTextEvaluation(
            storedText,
            new ImageTextAssessmentContext(
                meanConfidence,
                quality,
                true,
                null,
                wasTruncated));
    }

    private static string? RejectReason(
        double meanConfidence,
        int meaningfulCharacters,
        double meaningfulRatio,
        int tokenCount,
        double singleCharacterRatio)
    {
        if (meaningfulCharacters < 6)
        {
            return "Too few letters or digits were recognized to form useful context.";
        }

        if (meanConfidence < 0.5)
        {
            return "Mean OCR confidence was below 50%.";
        }

        if (meaningfulRatio < 0.55)
        {
            return "The result contained too much punctuation or symbol noise.";
        }

        if (tokenCount == 1 && meanConfidence < 0.85)
        {
            return "A single short label with limited confidence is likely to be a logo or icon artifact.";
        }

        if (tokenCount >= 8 && singleCharacterRatio > 0.45)
        {
            return "Too many isolated single-character tokens indicate visual noise rather than continuous text.";
        }

        if (meanConfidence < 0.6 && (tokenCount < 3 || singleCharacterRatio > 0.25))
        {
            return "The low-confidence result lacked enough continuous text structure.";
        }

        return null;
    }

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
