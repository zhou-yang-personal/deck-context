using DeckContext.Domain.Model;

namespace DeckContext.Application.Contracts;

public interface IImageAssetExporter
{
    void Export(
        string sourcePath,
        ImageContext image,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
