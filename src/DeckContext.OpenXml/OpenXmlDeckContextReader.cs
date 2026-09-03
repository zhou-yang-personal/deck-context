using System.Globalization;
using System.Xml;
using DeckContext.Application.Contracts;
using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;

namespace DeckContext.OpenXml;

public sealed class OpenXmlDeckContextReader : IDeckContextReader
{
    private const string ExtractorName = "OpenXmlPackageReader";

    public DeckContextDocument Read(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var sourceFileName = Path.GetFileName(sourcePath);

        if (!File.Exists(sourcePath))
        {
            return FailedDocument(
                sourceFileName,
                "DCX-PACKAGE-NOT-FOUND",
                "The source PPTX file does not exist.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var presentationDocument = PresentationDocument.Open(sourcePath, false);
            return ReadPresentation(presentationDocument, sourceFileName, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPackageFailure(exception))
        {
            return FailedDocument(
                sourceFileName,
                "DCX-PACKAGE-OPEN-FAILED",
                $"The PPTX package could not be opened: {exception.Message}");
        }
    }

    private static DeckContextDocument ReadPresentation(
        PresentationDocument presentationDocument,
        string sourceFileName,
        CancellationToken cancellationToken)
    {
        var presentationPart = presentationDocument.PresentationPart;

        if (presentationPart?.Presentation is null)
        {
            return FailedDocument(
                sourceFileName,
                "DCX-PRESENTATION-PART-MISSING",
                "The PPTX package does not contain a readable presentation part.");
        }

        var deckDiagnostics = new List<ExtractionDiagnostic>();
        var slideSize = presentationPart.Presentation.SlideSize;
        var width = slideSize?.Cx?.Value;
        var height = slideSize?.Cy?.Value;

        if (width is null || height is null)
        {
            deckDiagnostics.Add(new ExtractionDiagnostic(
                "DCX-PRESENTATION-SLIDE-SIZE-MISSING",
                "The presentation does not declare a complete slide size.",
                DiagnosticSeverity.Warning,
                ExtractorName,
                DiagnosticOutcome.Partial,
                new SourceReference(sourceFileName, presentationPart.Uri.OriginalString)));
        }

        var slideIds = presentationPart.Presentation.SlideIdList?
            .Elements<P.SlideId>()
            .ToArray() ?? [];

        var slides = new List<SlideContext>(slideIds.Length);

        for (var index = 0; index < slideIds.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            slides.Add(ReadSlide(
                presentationPart,
                slideIds[index],
                index + 1,
                sourceFileName,
                width,
                height));
        }

        var status = AggregateDeckStatus(deckDiagnostics, slides);

        return new DeckContextDocument(
            DeckContextDocument.CurrentSchemaVersion,
            new DeckMetadata(
                sourceFileName,
                presentationPart.Uri.OriginalString,
                width,
                height,
                slideIds.Length),
            slides,
            status,
            deckDiagnostics);
    }

    private static SlideContext ReadSlide(
        PresentationPart presentationPart,
        P.SlideId slideId,
        int slideIndex,
        string sourceFileName,
        long? width,
        long? height)
    {
        var relationshipId = slideId.RelationshipId?.Value;
        var slideIdentity = slideId.Id?.Value.ToString(CultureInfo.InvariantCulture);
        var diagnostics = new List<ExtractionDiagnostic>();

        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            diagnostics.Add(CreateSlideRelationshipDiagnostic(
                sourceFileName,
                slideIndex,
                slideIdentity,
                relationshipId,
                "The slide entry does not declare a relationship id."));

            return FailedSlide(slideIndex, slideIdentity, relationshipId, width, height, diagnostics);
        }

        try
        {
            if (presentationPart.GetPartById(relationshipId) is not SlidePart slidePart)
            {
                diagnostics.Add(CreateSlideRelationshipDiagnostic(
                    sourceFileName,
                    slideIndex,
                    slideIdentity,
                    relationshipId,
                    "The slide relationship does not resolve to a slide part."));

                return FailedSlide(slideIndex, slideIdentity, relationshipId, width, height, diagnostics);
            }

            var elements = ReadTopLevelElements(
                slidePart,
                sourceFileName,
                slideIndex,
                relationshipId);
            var slideStatus = elements.Any(element => element.Status == ExtractionStatus.Unsupported)
                ? ExtractionStatus.Partial
                : ExtractionStatus.Succeeded;

            return new SlideContext(
                new SlideMetadata(
                    slideIndex,
                    slideIdentity,
                    relationshipId,
                    slidePart.Uri.OriginalString,
                    width,
                    height),
                elements,
                slideStatus,
                diagnostics);
        }
        catch (Exception exception) when (IsExpectedRelationshipFailure(exception))
        {
            diagnostics.Add(CreateSlideRelationshipDiagnostic(
                sourceFileName,
                slideIndex,
                slideIdentity,
                relationshipId,
                $"The slide relationship could not be resolved: {exception.Message}"));

            return FailedSlide(slideIndex, slideIdentity, relationshipId, width, height, diagnostics);
        }
    }

    private static IReadOnlyList<SlideElementContext> ReadTopLevelElements(
        SlidePart slidePart,
        string sourceFileName,
        int slideIndex,
        string relationshipId)
    {
        var shapeTree = slidePart.Slide?.CommonSlideData?.ShapeTree;

        if (shapeTree is null)
        {
            return [];
        }

        var sourceElements = shapeTree.ChildElements
            .Where(element => element is not P.NonVisualGroupShapeProperties)
            .Where(element => element is not P.GroupShapeProperties)
            .ToArray();

        var elements = new List<SlideElementContext>(sourceElements.Length);

        for (var zOrder = 0; zOrder < sourceElements.Length; zOrder++)
        {
            var sourceElement = sourceElements[zOrder];
            var kind = GetElementKind(sourceElement);
            var drawingProperties = GetNonVisualDrawingProperties(sourceElement);
            var elementId = drawingProperties?.Id?.Value.ToString(CultureInfo.InvariantCulture);
            var elementName = drawingProperties?.Name?.Value;
            var source = new SourceReference(
                sourceFileName,
                slidePart.Uri.OriginalString,
                relationshipId,
                slideIndex,
                elementId,
                elementName);

            IReadOnlyList<ExtractionDiagnostic> diagnostics = [];
            var status = ExtractionStatus.Succeeded;

            if (kind == ElementKind.Unknown)
            {
                status = ExtractionStatus.Unsupported;
                diagnostics =
                [
                    new ExtractionDiagnostic(
                        "DCX-ELEMENT-TYPE-UNSUPPORTED",
                        $"The top-level element type '{sourceElement.LocalName}' is not recognized.",
                        DiagnosticSeverity.Warning,
                        "SlideElementReader",
                        DiagnosticOutcome.Skipped,
                        source),
                ];
            }

            elements.Add(new SlideElementContext(
                new ElementIdentity(elementId, elementName),
                kind,
                source,
                zOrder,
                null,
                null,
                status,
                diagnostics));
        }

        return elements;
    }

    private static ElementKind GetElementKind(OpenXmlElement element)
    {
        return element switch
        {
            P.Shape => ElementKind.Shape,
            P.Picture => ElementKind.Picture,
            P.GraphicFrame => ElementKind.GraphicFrame,
            P.GroupShape => ElementKind.Group,
            P.ConnectionShape => ElementKind.Connector,
            _ => ElementKind.Unknown,
        };
    }

    private static P.NonVisualDrawingProperties? GetNonVisualDrawingProperties(OpenXmlElement element)
    {
        return element switch
        {
            P.Shape shape => shape.NonVisualShapeProperties?.NonVisualDrawingProperties,
            P.Picture picture => picture.NonVisualPictureProperties?.NonVisualDrawingProperties,
            P.GraphicFrame graphicFrame => graphicFrame.NonVisualGraphicFrameProperties?.NonVisualDrawingProperties,
            P.GroupShape groupShape => groupShape.NonVisualGroupShapeProperties?.NonVisualDrawingProperties,
            P.ConnectionShape connectionShape => connectionShape.NonVisualConnectionShapeProperties?.NonVisualDrawingProperties,
            _ => null,
        };
    }

    private static SlideContext FailedSlide(
        int slideIndex,
        string? slideId,
        string? relationshipId,
        long? width,
        long? height,
        IReadOnlyList<ExtractionDiagnostic> diagnostics)
    {
        return new SlideContext(
            new SlideMetadata(slideIndex, slideId, relationshipId, null, width, height),
            [],
            ExtractionStatus.Failed,
            diagnostics);
    }

    private static ExtractionDiagnostic CreateSlideRelationshipDiagnostic(
        string sourceFileName,
        int slideIndex,
        string? slideId,
        string? relationshipId,
        string message)
    {
        return new ExtractionDiagnostic(
            "DCX-SLIDE-RELATIONSHIP-FAILED",
            message,
            DiagnosticSeverity.Error,
            "SlideReader",
            DiagnosticOutcome.Skipped,
            new SourceReference(
                sourceFileName,
                "/ppt/presentation.xml",
                relationshipId,
                slideIndex,
                slideId));
    }

    private static ExtractionStatus AggregateDeckStatus(
        IReadOnlyCollection<ExtractionDiagnostic> deckDiagnostics,
        IReadOnlyCollection<SlideContext> slides)
    {
        if (slides.Any(slide => slide.Status is ExtractionStatus.Failed or ExtractionStatus.Partial) ||
            deckDiagnostics.Count > 0)
        {
            return ExtractionStatus.Partial;
        }

        return ExtractionStatus.Succeeded;
    }

    private static DeckContextDocument FailedDocument(
        string sourceFileName,
        string code,
        string message)
    {
        var source = new SourceReference(sourceFileName);
        var diagnostic = new ExtractionDiagnostic(
            code,
            message,
            DiagnosticSeverity.Error,
            ExtractorName,
            DiagnosticOutcome.Skipped,
            source);

        return new DeckContextDocument(
            DeckContextDocument.CurrentSchemaVersion,
            new DeckMetadata(sourceFileName, null, null, null, 0),
            [],
            ExtractionStatus.Failed,
            [diagnostic]);
    }

    private static bool IsExpectedPackageFailure(Exception exception)
    {
        return exception is OpenXmlPackageException
            or IOException
            or FileFormatException
            or UnauthorizedAccessException
            or XmlException
            or ArgumentException;
    }

    private static bool IsExpectedRelationshipFailure(Exception exception)
    {
        return exception is KeyNotFoundException
            or InvalidDataException
            or OpenXmlPackageException
            or ArgumentException;
    }
}
