using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;

namespace DeckContext.Export;

public sealed record ExtractionReportSummary(
    int SlideCount,
    int ElementCount,
    int SucceededElementCount,
    int PartialElementCount,
    int FailedElementCount,
    int UnsupportedElementCount,
    int InformationCount,
    int WarningCount,
    int ErrorCount);

public sealed record ExtractionReportEntry(
    string Scope,
    string Code,
    string Message,
    DiagnosticSeverity Severity,
    string Extractor,
    DiagnosticOutcome Outcome,
    int? SlideIndex,
    string? ElementId,
    string? ElementName,
    string? PartUri,
    string? RelationshipId);

public sealed record ExtractionReport(
    string SchemaVersion,
    string SourceFileName,
    ExtractionStatus Status,
    ExtractionReportSummary Summary,
    IReadOnlyList<ExtractionReportEntry> Entries);

public sealed class ExtractionReportSerializer
{
    public string Serialize(DeckContextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var entries = new List<ExtractionReportEntry>();
        entries.AddRange(document.Diagnostics.Select(diagnostic => CreateEntry("deck", diagnostic)));

        foreach (var slide in document.Slides.OrderBy(slide => slide.Metadata.Index))
        {
            entries.AddRange(slide.Diagnostics.Select(diagnostic => CreateEntry("slide", diagnostic)));

            foreach (var element in slide.Elements.OrderBy(element => element.ZOrder))
            {
                entries.AddRange(element.Diagnostics.Select(diagnostic => CreateEntry("element", diagnostic)));
            }
        }

        var elements = document.Slides.SelectMany(slide => slide.Elements).ToArray();
        var report = new ExtractionReport(
            document.SchemaVersion,
            document.Deck.SourceFileName,
            document.Status,
            new ExtractionReportSummary(
                document.Slides.Count,
                elements.Length,
                elements.Count(element => element.Status == ExtractionStatus.Succeeded),
                elements.Count(element => element.Status == ExtractionStatus.Partial),
                elements.Count(element => element.Status == ExtractionStatus.Failed),
                elements.Count(element => element.Status == ExtractionStatus.Unsupported),
                entries.Count(entry => entry.Severity == DiagnosticSeverity.Information),
                entries.Count(entry => entry.Severity == DiagnosticSeverity.Warning),
                entries.Count(entry => entry.Severity == DiagnosticSeverity.Error)),
            entries);

        return DeterministicJson.Serialize(report);
    }

    private static ExtractionReportEntry CreateEntry(
        string scope,
        ExtractionDiagnostic diagnostic)
    {
        return new ExtractionReportEntry(
            scope,
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.Severity,
            diagnostic.Extractor,
            diagnostic.Outcome,
            diagnostic.Source?.SlideIndex,
            diagnostic.Source?.ElementId,
            diagnostic.Source?.ElementName,
            diagnostic.Source?.PartUri,
            diagnostic.Source?.RelationshipId);
    }
}
