using System.Globalization;
using System.Security.Cryptography;
using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;

namespace DeckContext.OpenXml;

internal sealed record ImageExtractionResult(
    ImageContext Image,
    ExtractionStatus Status,
    OpenXmlExtractedAsset? Asset = null);

internal static class OpenXmlImageExtractor
{
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string ExtractorName = "ImageExtractor";
    private const double CropUnitScale = 100_000d;
    private const double RotationUnitScale = 60_000d;

    public static ImageExtractionResult Extract(
        P.Picture picture,
        SlidePart slidePart,
        SourceReference source,
        ICollection<ExtractionDiagnostic> diagnostics)
    {
        var drawingProperties = picture.NonVisualPictureProperties?.NonVisualDrawingProperties;
        var blip = picture.Descendants()
            .FirstOrDefault(element =>
                element.LocalName == "blip" && element.NamespaceUri == DrawingNamespace);
        var relationshipId = ReadStringAttribute(blip, "embed") ?? ReadStringAttribute(blip, "link");
        var crop = ReadCrop(picture);
        var transform = ReadTransform(picture);
        var interpretation = new ImageContentInterpretationContext(
            ImageContentInterpretationStatus.NotConfigured,
            null,
            null,
            null);

        diagnostics.Add(new ExtractionDiagnostic(
            "DCX-IMAGE-TEXT-PROVIDER-NOT-CONFIGURED",
            "Image pixels were not interpreted because no OCR/Vision provider is configured.",
            DiagnosticSeverity.Information,
            ExtractorName,
            DiagnosticOutcome.None,
            source));

        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            diagnostics.Add(CreateDiagnostic(
                "DCX-IMAGE-RELATIONSHIP-MISSING",
                "The picture does not declare an embedded or linked media relationship.",
                DiagnosticSeverity.Error,
                DiagnosticOutcome.Skipped,
                source));
            var missingImage = CreateImage(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                drawingProperties?.Description?.Value,
                drawingProperties?.Title?.Value,
                crop,
                transform,
                interpretation,
                ExtractionStatus.Failed);
            return new ImageExtractionResult(missingImage, ExtractionStatus.Failed);
        }

        source = source with { RelationshipId = relationshipId };

        var externalRelationship = slidePart.ExternalRelationships
            .FirstOrDefault(relationship => relationship.Id == relationshipId);

        if (externalRelationship is not null)
        {
            diagnostics.Add(CreateDiagnostic(
                "DCX-IMAGE-EXTERNAL-UNSUPPORTED",
                $"Picture relationship '{relationshipId}' points outside the PPTX package and was not fetched.",
                DiagnosticSeverity.Warning,
                DiagnosticOutcome.Partial,
                source));
            var externalImage = CreateImage(
                relationshipId,
                null,
                externalRelationship.Uri.OriginalString,
                null,
                null,
                null,
                null,
                drawingProperties?.Description?.Value,
                drawingProperties?.Title?.Value,
                crop,
                transform,
                interpretation,
                ExtractionStatus.Partial);
            return new ImageExtractionResult(externalImage, ExtractionStatus.Partial);
        }

        try
        {
            var linkedPart = slidePart.GetPartById(relationshipId);

            if (linkedPart is not ImagePart imagePart)
            {
                diagnostics.Add(CreateDiagnostic(
                    "DCX-IMAGE-RELATIONSHIP-FAILED",
                    "The picture relationship does not resolve to an image part.",
                    DiagnosticSeverity.Error,
                    DiagnosticOutcome.Skipped,
                    source));
                var failedImage = CreateImage(
                    relationshipId,
                    linkedPart.Uri.OriginalString,
                    null,
                    linkedPart.ContentType,
                    null,
                    null,
                    null,
                    drawingProperties?.Description?.Value,
                    drawingProperties?.Title?.Value,
                    crop,
                    transform,
                    interpretation,
                    ExtractionStatus.Failed);
                return new ImageExtractionResult(failedImage, ExtractionStatus.Failed);
            }

            byte[] bytes;

            using (var stream = imagePart.GetStream(FileMode.Open, FileAccess.Read))
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                bytes = buffer.ToArray();
            }

            var extension = Path.GetExtension(imagePart.Uri.OriginalString);
            var image = CreateImage(
                relationshipId,
                imagePart.Uri.OriginalString,
                null,
                imagePart.ContentType,
                string.IsNullOrWhiteSpace(extension) ? null : extension,
                Path.GetFileName(imagePart.Uri.OriginalString),
                bytes,
                drawingProperties?.Description?.Value,
                drawingProperties?.Title?.Value,
                crop,
                transform,
                interpretation,
                ExtractionStatus.Succeeded);
            return new ImageExtractionResult(
                image,
                ExtractionStatus.Succeeded,
                new OpenXmlExtractedAsset(
                    OpenXmlExtractedAssetKind.Image,
                    imagePart.Uri.OriginalString,
                    image.Sha256!,
                    bytes.LongLength,
                    bytes));
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException or IOException)
        {
            diagnostics.Add(CreateDiagnostic(
                "DCX-IMAGE-RELATIONSHIP-FAILED",
                $"The picture media relationship could not be resolved or read: {exception.Message}",
                DiagnosticSeverity.Error,
                DiagnosticOutcome.Skipped,
                source));
            var failedImage = CreateImage(
                relationshipId,
                null,
                null,
                null,
                null,
                null,
                null,
                drawingProperties?.Description?.Value,
                drawingProperties?.Title?.Value,
                crop,
                transform,
                interpretation,
                ExtractionStatus.Failed);
            return new ImageExtractionResult(failedImage, ExtractionStatus.Failed);
        }
    }

    private static ImageContext CreateImage(
        string? relationshipId,
        string? partUri,
        string? externalUri,
        string? contentType,
        string? fileExtension,
        string? suggestedFileName,
        byte[]? bytes,
        string? alternativeText,
        string? title,
        ImageCropContext? crop,
        ImageTransformContext transform,
        ImageContentInterpretationContext interpretation,
        ExtractionStatus status)
    {
        return new ImageContext(
            relationshipId,
            partUri,
            externalUri,
            contentType,
            fileExtension,
            suggestedFileName,
            bytes?.LongLength,
            bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            NullIfWhiteSpace(alternativeText),
            NullIfWhiteSpace(title),
            crop,
            transform,
            interpretation,
            status);
    }

    private static ImageCropContext? ReadCrop(P.Picture picture)
    {
        var sourceRectangle = picture.Descendants()
            .FirstOrDefault(element =>
                element.LocalName == "srcRect" && element.NamespaceUri == DrawingNamespace);

        if (sourceRectangle is null)
        {
            return null;
        }

        var left = ReadIntAttribute(sourceRectangle, "l") ?? 0;
        var top = ReadIntAttribute(sourceRectangle, "t") ?? 0;
        var right = ReadIntAttribute(sourceRectangle, "r") ?? 0;
        var bottom = ReadIntAttribute(sourceRectangle, "b") ?? 0;

        return new ImageCropContext(
            left,
            top,
            right,
            bottom,
            left / CropUnitScale,
            top / CropUnitScale,
            right / CropUnitScale,
            bottom / CropUnitScale);
    }

    private static ImageTransformContext ReadTransform(P.Picture picture)
    {
        var transform = picture.ShapeProperties?.Transform2D;
        var rotation = ReadLongAttribute(transform, "rot");

        return new ImageTransformContext(
            rotation,
            rotation / RotationUnitScale,
            ReadBooleanAttribute(transform, "flipH"),
            ReadBooleanAttribute(transform, "flipV"));
    }

    private static string? ReadStringAttribute(OpenXmlElement? element, string localName)
    {
        return element?
            .GetAttributes()
            .FirstOrDefault(attribute => attribute.LocalName == localName)
            .Value;
    }

    private static int? ReadIntAttribute(OpenXmlElement? element, string localName)
    {
        return int.TryParse(
            ReadStringAttribute(element, localName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static long? ReadLongAttribute(OpenXmlElement? element, string localName)
    {
        return long.TryParse(
            ReadStringAttribute(element, localName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static bool? ReadBooleanAttribute(OpenXmlElement? element, string localName)
    {
        return ReadStringAttribute(element, localName)?.ToLowerInvariant() switch
        {
            "1" or "true" or "on" => true,
            "0" or "false" or "off" => false,
            _ => null,
        };
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static ExtractionDiagnostic CreateDiagnostic(
        string code,
        string message,
        DiagnosticSeverity severity,
        DiagnosticOutcome outcome,
        SourceReference source)
    {
        return new ExtractionDiagnostic(
            code,
            message,
            severity,
            ExtractorName,
            outcome,
            source);
    }
}
