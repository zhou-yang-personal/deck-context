using System.Security.Cryptography;
using DeckContext.Application.Contracts;
using DeckContext.Domain.Model;
using DocumentFormat.OpenXml.Packaging;

namespace DeckContext.OpenXml;

public sealed class OpenXmlEmbeddedWorkbookAssetExporter : IEmbeddedWorkbookAssetExporter
{
    public void Export(
        string sourcePath,
        EmbeddedWorkbookContext workbook,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();

        using var presentation = PresentationDocument.Open(sourcePath, false);
        var chartPart = presentation.PresentationPart?
            .SlideParts
            .SelectMany(slidePart => slidePart.ChartParts)
            .FirstOrDefault(part => string.Equals(
                part.Uri.OriginalString,
                workbook.ChartPartUri,
                StringComparison.Ordinal));

        if (chartPart is null)
        {
            throw new InvalidDataException(
                $"Chart part '{workbook.ChartPartUri}' no longer exists in the source presentation.");
        }

        OpenXmlPart linkedPart;

        try
        {
            linkedPart = chartPart.GetPartById(workbook.RelationshipId);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            throw new InvalidDataException(
                $"Embedded workbook relationship '{workbook.RelationshipId}' could not be resolved.",
                exception);
        }

        if (linkedPart is not EmbeddedPackagePart workbookPart)
        {
            throw new InvalidDataException(
                $"Relationship '{workbook.RelationshipId}' is not an embedded workbook package.");
        }

        byte[] bytes;

        using (var source = workbookPart.GetStream(FileMode.Open, FileAccess.Read))
        using (var buffer = new MemoryStream())
        {
            source.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (!string.Equals(sha256, workbook.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The embedded workbook changed after extraction; the asset was not exported with stale provenance.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));

        if (destinationDirectory is not null)
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.WriteAllBytes(destinationPath, bytes);
    }
}
