using DeckContext.Domain.Model;

namespace DeckContext.Application.Contracts;

public interface IEmbeddedWorkbookAssetExporter
{
    void Export(
        string sourcePath,
        EmbeddedWorkbookContext workbook,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
