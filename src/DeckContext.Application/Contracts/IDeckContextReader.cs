using DeckContext.Domain.Model;

namespace DeckContext.Application.Contracts;

public interface IDeckContextReader
{
    DeckContextDocument Read(string sourcePath, CancellationToken cancellationToken = default);
}
