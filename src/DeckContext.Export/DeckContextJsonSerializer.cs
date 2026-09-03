using DeckContext.Domain.Model;

namespace DeckContext.Export;

public sealed class DeckContextJsonSerializer
{
    public string Serialize(DeckContextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return DeterministicJson.Serialize(document);
    }
}
