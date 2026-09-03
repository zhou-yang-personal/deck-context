namespace DeckContext.Domain.Extraction;

public enum ExtractionStatus
{
    NotStarted,
    Running,
    Succeeded,
    Partial,
    Failed,
    Unsupported,
}
