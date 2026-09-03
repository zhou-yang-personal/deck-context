namespace DeckContext.Domain.Model;

public enum TextRunKind
{
    Text,
    Field,
    Break,
}

public sealed record TextColorContext(
    string Type,
    string Value);

public sealed record TextStyleContext(
    string? Language,
    string? LatinTypeface,
    string? EastAsianTypeface,
    double? FontSizePoints,
    bool? Bold,
    TextColorContext? Color);

public sealed record TextRunContext(
    TextRunKind Kind,
    string Text,
    TextStyleContext? DirectStyle);

public sealed record TextParagraphContext(
    int Index,
    int? Level,
    string? Alignment,
    TextStyleContext? DefaultStyle,
    IReadOnlyList<TextRunContext> Runs);

public sealed record TextContentContext(
    IReadOnlyList<TextParagraphContext> Paragraphs);
