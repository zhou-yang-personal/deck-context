using DeckContext.Domain.Model;

namespace DeckContext.Domain.Diagnostics;

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public enum DiagnosticOutcome
{
    None,
    Skipped,
    Partial,
    Recovered,
}

public sealed record ExtractionDiagnostic(
    string Code,
    string Message,
    DiagnosticSeverity Severity,
    string Extractor,
    DiagnosticOutcome Outcome,
    SourceReference? Source = null);
