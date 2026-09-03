using System.Security.Cryptography;
using DeckContext.Application.Contracts;
using DeckContext.Domain.Model;
using DocumentFormat.OpenXml.Packaging;

namespace DeckContext.OpenXml;

public sealed class OpenXmlImageAssetExporter : IImageAssetExporter
{
    public void Export(
        string sourcePath,
        ImageContext image,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (image.PartUri is null || image.Sha256 is null)
        {
            throw new InvalidDataException("The image does not contain an exportable internal media reference.");
        }

        using var presentation = PresentationDocument.Open(sourcePath, false);
        var imagePart = presentation.PresentationPart?
            .SlideParts
            .SelectMany(slidePart => slidePart.ImageParts)
            .FirstOrDefault(part => string.Equals(
                part.Uri.OriginalString,
                image.PartUri,
                StringComparison.Ordinal));

        if (imagePart is null)
        {
            throw new InvalidDataException(
                $"Image part '{image.PartUri}' no longer exists in the source presentation.");
        }

        byte[] bytes;

        using (var source = imagePart.GetStream(FileMode.Open, FileAccess.Read))
        using (var buffer = new MemoryStream())
        {
            source.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (!string.Equals(sha256, image.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The image changed after extraction; the asset was not exported with stale provenance.");
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
