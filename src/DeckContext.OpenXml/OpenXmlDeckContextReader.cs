using System.Globalization;
using System.Xml;
using DeckContext.Application.Contracts;
using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
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
                relationshipId,
                width,
                height);
            var slideStatus = elements.Any(element =>
                element.Status is ExtractionStatus.Partial or ExtractionStatus.Failed or ExtractionStatus.Unsupported)
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
        string relationshipId,
        long? slideWidth,
        long? slideHeight)
    {
        var shapeTree = slidePart.Slide?.CommonSlideData?.ShapeTree;

        if (shapeTree is null)
        {
            return [];
        }

        var elements = new List<SlideElementContext>();
        ReadElements(
            shapeTree,
            sourceFileName,
            slideIndex,
            relationshipId,
            slidePart.Uri.OriginalString,
            null,
            GeometryCoordinateSpace.Slide,
            slideWidth,
            slideHeight,
            elements);

        return elements;
    }

    private static void ReadElements(
        OpenXmlCompositeElement container,
        string sourceFileName,
        int slideIndex,
        string relationshipId,
        string slidePartUri,
        string? parentGroupId,
        GeometryCoordinateSpace coordinateSpace,
        long? slideWidth,
        long? slideHeight,
        ICollection<SlideElementContext> elements)
    {
        var sourceElements = container.ChildElements
            .Where(IsSlideElement)
            .ToArray();

        for (var zOrder = 0; zOrder < sourceElements.Length; zOrder++)
        {
            var sourceElement = sourceElements[zOrder];
            var kind = GetElementKind(sourceElement);
            var drawingProperties = GetNonVisualDrawingProperties(sourceElement);
            var elementId = drawingProperties?.Id?.Value.ToString(CultureInfo.InvariantCulture);
            var elementName = drawingProperties?.Name?.Value;
            var source = new SourceReference(
                sourceFileName,
                slidePartUri,
                relationshipId,
                slideIndex,
                elementId,
                elementName);

            var diagnostics = new List<ExtractionDiagnostic>();
            var status = ExtractionStatus.Succeeded;
            var nativeGeometry = ReadNativeGeometry(sourceElement, coordinateSpace);
            var normalizedGeometry = coordinateSpace == GeometryCoordinateSpace.Slide
                ? NormalizeGeometry(nativeGeometry, slideWidth, slideHeight, source, diagnostics)
                : null;
            var text = ReadText(sourceElement);
            var table = kind == ElementKind.Table
                ? ReadTable(sourceElement, source, diagnostics)
                : null;

            if (kind == ElementKind.Unknown)
            {
                status = ExtractionStatus.Unsupported;
                diagnostics.Add(new ExtractionDiagnostic(
                    "DCX-ELEMENT-TYPE-UNSUPPORTED",
                    $"The slide element type '{sourceElement.LocalName}' is not recognized.",
                    DiagnosticSeverity.Warning,
                    "SlideElementReader",
                    DiagnosticOutcome.Skipped,
                    source));
            }
            else if (nativeGeometry is null)
            {
                status = ExtractionStatus.Partial;
                diagnostics.Add(new ExtractionDiagnostic(
                    "DCX-GEOMETRY-NOT-DIRECT",
                    "The element does not contain a directly declared transform; geometry was not inferred.",
                    DiagnosticSeverity.Warning,
                    "GeometryExtractor",
                    DiagnosticOutcome.Partial,
                    source));
            }

            if (kind == ElementKind.Group && string.IsNullOrWhiteSpace(elementId))
            {
                status = ExtractionStatus.Partial;
                diagnostics.Add(new ExtractionDiagnostic(
                    "DCX-GROUP-ID-MISSING",
                    "The group has no source object id, so child elements cannot reference a parent group id.",
                    DiagnosticSeverity.Warning,
                    "GeometryExtractor",
                    DiagnosticOutcome.Partial,
                    source));
            }

            if (status == ExtractionStatus.Succeeded &&
                diagnostics.Any(diagnostic => diagnostic.Outcome == DiagnosticOutcome.Partial))
            {
                status = ExtractionStatus.Partial;
            }

            var element = new SlideElementContext(
                new ElementIdentity(elementId, elementName),
                kind,
                source,
                zOrder,
                nativeGeometry,
                normalizedGeometry,
                status,
                diagnostics,
                parentGroupId,
                text,
                table);
            elements.Add(element);

            if (sourceElement is P.GroupShape groupShape)
            {
                ReadElements(
                    groupShape,
                    sourceFileName,
                    slideIndex,
                    relationshipId,
                    slidePartUri,
                    elementId,
                    GeometryCoordinateSpace.ParentGroup,
                    slideWidth,
                    slideHeight,
                    elements);
            }
        }
    }

    private static bool IsSlideElement(OpenXmlElement element)
    {
        return element is not P.NonVisualGroupShapeProperties
            and not P.GroupShapeProperties;
    }

    private static ElementKind GetElementKind(OpenXmlElement element)
    {
        return element switch
        {
            P.Shape => ElementKind.Shape,
            P.Picture => ElementKind.Picture,
            P.GraphicFrame graphicFrame when FindTable(graphicFrame) is not null => ElementKind.Table,
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

    private static NativeGeometry? ReadNativeGeometry(
        OpenXmlElement element,
        GeometryCoordinateSpace coordinateSpace)
    {
        OpenXmlCompositeElement? transform = element switch
        {
            P.Shape shape => shape.ShapeProperties?.Transform2D,
            P.Picture picture => picture.ShapeProperties?.Transform2D,
            P.GraphicFrame graphicFrame => graphicFrame.Transform,
            P.GroupShape groupShape => groupShape.GroupShapeProperties?.TransformGroup,
            P.ConnectionShape connectionShape => connectionShape.ShapeProperties?.Transform2D,
            _ => null,
        };

        if (transform is null)
        {
            return null;
        }

        var offset = transform.ChildElements.FirstOrDefault(child => child.LocalName == "off");
        var extents = transform.ChildElements.FirstOrDefault(child => child.LocalName == "ext");

        if (!TryReadLongAttribute(offset, "x", out var x) ||
            !TryReadLongAttribute(offset, "y", out var y) ||
            !TryReadLongAttribute(extents, "cx", out var width) ||
            !TryReadLongAttribute(extents, "cy", out var height))
        {
            return null;
        }

        return new NativeGeometry(x, y, width, height, coordinateSpace);
    }

    private static NormalizedGeometry? NormalizeGeometry(
        NativeGeometry? nativeGeometry,
        long? slideWidth,
        long? slideHeight,
        SourceReference source,
        ICollection<ExtractionDiagnostic> diagnostics)
    {
        if (nativeGeometry is null)
        {
            return null;
        }

        if (slideWidth is null or <= 0 || slideHeight is null or <= 0)
        {
            diagnostics.Add(new ExtractionDiagnostic(
                "DCX-GEOMETRY-NORMALIZATION-UNAVAILABLE",
                "Normalized geometry could not be calculated because the presentation slide size is missing or invalid.",
                DiagnosticSeverity.Warning,
                "GeometryExtractor",
                DiagnosticOutcome.Partial,
                source));
            return null;
        }

        return new NormalizedGeometry(
            nativeGeometry.X / (double)slideWidth.Value,
            nativeGeometry.Y / (double)slideHeight.Value,
            nativeGeometry.Width / (double)slideWidth.Value,
            nativeGeometry.Height / (double)slideHeight.Value);
    }

    private static TextContentContext? ReadText(OpenXmlElement element)
    {
        if (element is not P.Shape { TextBody: { } textBody })
        {
            return null;
        }

        return ReadTextBody(textBody);
    }

    private static TextContentContext ReadTextBody(OpenXmlCompositeElement textBody)
    {
        var paragraphs = textBody.Elements<A.Paragraph>()
            .Select(ReadParagraph)
            .ToArray();

        return new TextContentContext(paragraphs);
    }

    private static TableContext? ReadTable(
        OpenXmlElement element,
        SourceReference source,
        ICollection<ExtractionDiagnostic> diagnostics)
    {
        if (element is not P.GraphicFrame graphicFrame || FindTable(graphicFrame) is not { } table)
        {
            return null;
        }

        var columnCount = table.TableGrid?.Elements<A.GridColumn>().Count() ?? 0;
        var sourceRows = table.Elements<A.TableRow>().ToArray();
        var rows = new List<TableRowContext>(sourceRows.Length);

        for (var rowIndex = 0; rowIndex < sourceRows.Length; rowIndex++)
        {
            var sourceRow = sourceRows[rowIndex];
            var sourceCells = sourceRow.Elements<A.TableCell>().ToArray();
            var cells = new List<TableCellContext>(sourceCells.Length);

            if (columnCount > 0 && sourceCells.Length != columnCount)
            {
                diagnostics.Add(new ExtractionDiagnostic(
                    "DCX-TABLE-COLUMN-COUNT-MISMATCH",
                    $"Table row {rowIndex} contains {sourceCells.Length} cells while the table grid declares {columnCount} columns.",
                    DiagnosticSeverity.Warning,
                    "TableExtractor",
                    DiagnosticOutcome.Partial,
                    source));
            }

            for (var columnIndex = 0; columnIndex < sourceCells.Length; columnIndex++)
            {
                var sourceCell = sourceCells[columnIndex];
                cells.Add(new TableCellContext(
                    rowIndex,
                    columnIndex,
                    ReadIntAttribute(sourceCell, "rowSpan") ?? 1,
                    ReadIntAttribute(sourceCell, "gridSpan") ?? 1,
                    ReadBooleanAttribute(sourceCell, "hMerge") ?? false,
                    ReadBooleanAttribute(sourceCell, "vMerge") ?? false,
                    sourceCell.TextBody is { } textBody
                        ? ReadTextBody(textBody)
                        : new TextContentContext([]),
                    ReadTableCellFill(sourceCell.TableCellProperties)));
            }

            rows.Add(new TableRowContext(
                rowIndex,
                ReadLongAttribute(sourceRow, "h"),
                cells));
        }

        if (columnCount == 0)
        {
            diagnostics.Add(new ExtractionDiagnostic(
                "DCX-TABLE-GRID-MISSING",
                "The native table does not declare a table grid; the column count was not inferred.",
                DiagnosticSeverity.Warning,
                "TableExtractor",
                DiagnosticOutcome.Partial,
                source));
        }

        return new TableContext(sourceRows.Length, columnCount, rows);
    }

    private static A.Table? FindTable(P.GraphicFrame graphicFrame)
    {
        return graphicFrame.Descendants<A.Table>().FirstOrDefault();
    }

    private static TableCellFillContext? ReadTableCellFill(OpenXmlCompositeElement? properties)
    {
        if (properties is null)
        {
            return null;
        }

        var solidFill = properties.ChildElements.FirstOrDefault(child => child.LocalName == "solidFill");
        var color = solidFill?.ChildElements.FirstOrDefault();

        if (color is null)
        {
            return null;
        }

        var value = color.LocalName == "sysClr"
            ? ReadStringAttribute(color, "lastClr")
            : ReadStringAttribute(color, "val");

        return string.IsNullOrWhiteSpace(value)
            ? null
            : new TableCellFillContext(color.LocalName, value);
    }

    private static TextParagraphContext ReadParagraph(A.Paragraph paragraph, int index)
    {
        var paragraphProperties = paragraph.ParagraphProperties;
        var runs = new List<TextRunContext>();

        foreach (var child in paragraph.ChildElements)
        {
            switch (child)
            {
                case A.Run run:
                    runs.Add(new TextRunContext(
                        TextRunKind.Text,
                        run.Text?.Text ?? string.Empty,
                        ReadTextStyle(run.RunProperties)));
                    break;
                case A.Field field:
                    runs.Add(new TextRunContext(
                        TextRunKind.Field,
                        field.Text?.Text ?? string.Empty,
                        ReadTextStyle(field.RunProperties)));
                    break;
                case A.Break textBreak:
                    runs.Add(new TextRunContext(
                        TextRunKind.Break,
                        "\n",
                        ReadTextStyle(textBreak.RunProperties)));
                    break;
            }
        }

        return new TextParagraphContext(
            index,
            ReadIntAttribute(paragraphProperties, "lvl"),
            ReadStringAttribute(paragraphProperties, "algn"),
            ReadTextStyle(paragraphProperties?.GetFirstChild<A.DefaultRunProperties>()),
            runs);
    }

    private static TextStyleContext? ReadTextStyle(OpenXmlCompositeElement? properties)
    {
        if (properties is null)
        {
            return null;
        }

        var language = ReadStringAttribute(properties, "lang");
        var latinTypeface = ReadStringAttribute(
            properties.ChildElements.FirstOrDefault(child => child.LocalName == "latin"),
            "typeface");
        var eastAsianTypeface = ReadStringAttribute(
            properties.ChildElements.FirstOrDefault(child => child.LocalName == "ea"),
            "typeface");
        double? fontSize = ReadIntAttribute(properties, "sz") is { } size
            ? size / 100d
            : null;
        var bold = ReadBooleanAttribute(properties, "b");
        var color = ReadTextColor(properties);

        if (language is null &&
            latinTypeface is null &&
            eastAsianTypeface is null &&
            fontSize is null &&
            bold is null &&
            color is null)
        {
            return null;
        }

        return new TextStyleContext(
            language,
            latinTypeface,
            eastAsianTypeface,
            fontSize,
            bold,
            color);
    }

    private static TextColorContext? ReadTextColor(OpenXmlCompositeElement properties)
    {
        var solidFill = properties.ChildElements.FirstOrDefault(child => child.LocalName == "solidFill");
        var color = solidFill?.ChildElements.FirstOrDefault();

        if (color is null)
        {
            return null;
        }

        var value = color.LocalName == "sysClr"
            ? ReadStringAttribute(color, "lastClr")
            : ReadStringAttribute(color, "val");

        return string.IsNullOrWhiteSpace(value)
            ? null
            : new TextColorContext(color.LocalName, value);
    }

    private static bool TryReadLongAttribute(
        OpenXmlElement? element,
        string attributeName,
        out long value)
    {
        return long.TryParse(
            ReadStringAttribute(element, attributeName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static int? ReadIntAttribute(OpenXmlElement? element, string attributeName)
    {
        return int.TryParse(
            ReadStringAttribute(element, attributeName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static long? ReadLongAttribute(OpenXmlElement? element, string attributeName)
    {
        return long.TryParse(
            ReadStringAttribute(element, attributeName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static bool? ReadBooleanAttribute(OpenXmlElement? element, string attributeName)
    {
        return ReadStringAttribute(element, attributeName) switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null,
        };
    }

    private static string? ReadStringAttribute(OpenXmlElement? element, string attributeName)
    {
        if (element is null)
        {
            return null;
        }

        var value = element.GetAttributes()
            .FirstOrDefault(attribute => attribute.LocalName == attributeName)
            .Value;

        return string.IsNullOrWhiteSpace(value) ? null : value;
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
