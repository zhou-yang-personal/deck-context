using DeckContext.Domain.Model;

namespace DeckContext.Application.Contracts;

public sealed record ImageTextRequest(
    string ContentType,
    string PartUri,
    ReadOnlyMemory<byte> Content,
    SourceReference Source);

public interface IImageTextProvider
{
    string ProviderId { get; }

    Task<ImageContentInterpretationContext> AnalyzeAsync(
        ImageTextRequest request,
        CancellationToken cancellationToken = default);
}
