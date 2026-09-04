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
    private const double RotationUnitScale = 60_000d;

    public DeckContextDocument Read(
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        ReadPackage(sourcePath, cancellationToken).Document;

    public OpenXmlDeckContextReadResult ReadPackage(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var sourceFileName = Path.GetFileName(sourcePath);

        if (!File.Exists(sourcePath))
        {
            return new OpenXmlDeckContextReadResult(
                FailedDocument(
                    sourceFileName,
                    "DCX-PACKAGE-NOT-FOUND",
                    "The source PPTX file does not exist."),
                []);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var presentationDocument = PresentationDocument.Open(sourcePath, false);
            return ReadPresentation(presentationDocument, sourceFileName, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPackageFailure(exception))
        {
            return new OpenXmlDeckContextReadResult(
                FailedDocument(
                    sourceFileName,
                    "DCX-PACKAGE-OPEN-FAILED",
                    $"The PPTX package could not be opened: {exception.Message}"),
                []);
        }
    }

    private static OpenXmlDeckContextReadResult ReadPresentation(
        PresentationDocument presentationDocument,
        string sourceFileName,
        CancellationToken cancellationToken)
    {
        var presentationPart = presentationDocument.PresentationPart;

        if (presentationPart?.Presentation is null)
        {
            return new OpenXmlDeckContextReadResult(
                FailedDocument(
                    sourceFileName,
                    "DCX-PRESENTATION-PART-MISSING",
                    "The PPTX package does not contain a readable presentation part."),
                []);
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
        var assets = new Dictionary<string, OpenXmlExtractedAsset>(StringComparer.Ordinal);

        for (var index = 0; index < slideIds.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            slides.Add(ReadSlide(
                presentationPart,
                slideIds[index],
                index + 1,
                sourceFileName,
                width,
                height,
                assets));
        }

        var status = AggregateDeckStatus(deckDiagnostics, slides);

        return new OpenXmlDeckContextReadResult(
            new DeckContextDocument(
                DeckContextDocument.CurrentSchemaVersion,
                new DeckMetadata(
                    sourceFileName,
                    presentationPart.Uri.OriginalString,
                    width,
                    height,
                    slideIds.Length),
                slides,
                status,
                deckDiagnostics),
            assets.Values
                .OrderBy(asset => asset.Kind)
                .ThenBy(asset => asset.PartUri, StringComparer.Ordinal)
                .ToArray());
    }

    private static SlideContext ReadSlide(
        PresentationPart presentationPart,
        P.SlideId slideId,
        int slideIndex,
        string sourceFileName,
        long? width,
        long? height,
        IDictionary<string, OpenXmlExtractedAsset> assets)
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
                width,
                height,
                assets);
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
        long? slideWidth,
        long? slideHeight,
        IDictionary<string, OpenXmlExtractedAsset> assets)
    {
        var shapeTree = slidePart.Slide?.CommonSlideData?.ShapeTree;

        if (shapeTree is null)
        {
            return [];
        }

        var elements = new List<SlideElementContext>();
        ReadElements(
            shapeTree,
            slidePart,
            sourceFileName,
            slideIndex,
            slidePart.Uri.OriginalString,
            null,
            GeometryCoordinateSpace.Slide,
            slideWidth,
            slideHeight,
            AffineTransform.Identity,
            [],
            assets,
            elements);

        return elements;
    }

    private static void ReadElements(
        OpenXmlCompositeElement container,
        SlidePart slidePart,
        string sourceFileName,
        int slideIndex,
        string slidePartUri,
        string? parentGroupId,
        GeometryCoordinateSpace coordinateSpace,
        long? slideWidth,
        long? slideHeight,
        AffineTransform? containerToSlide,
        IReadOnlyList<int> parentZOrderPath,
        IDictionary<string, OpenXmlExtractedAsset> assets,
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
                null,
                slideIndex,
                elementId,
                elementName);

            var diagnostics = new List<ExtractionDiagnostic>();
            var nativeGeometry = ReadNativeGeometry(sourceElement, coordinateSpace);
            var groupTransform = sourceElement is P.GroupShape groupShapeValue
                ? ReadGroupTransform(groupShapeValue)
                : null;
            var elementToSlide = sourceElement is P.GroupShape
                ? ComposeGroupOuterTransform(containerToSlide, nativeGeometry, groupTransform)
                : containerToSlide;
            var normalizedGeometry = NormalizeGeometry(
                nativeGeometry,
                elementToSlide,
                slideWidth,
                slideHeight,
                source,
                diagnostics);
            var zOrderPath = parentZOrderPath.Append(zOrder).ToArray();
            var text = ReadText(sourceElement);
            var table = kind == ElementKind.Table
                ? ReadTable(sourceElement, source, diagnostics)
                : null;
            var chartResult = kind == ElementKind.Chart && sourceElement is P.GraphicFrame chartFrame
                ? OpenXmlChartExtractor.Extract(chartFrame, slidePart, source, diagnostics)
                : null;
            var imageResult = kind == ElementKind.Picture && sourceElement is P.Picture picture
                ? OpenXmlImageExtractor.Extract(picture, slidePart, source, diagnostics)
                : null;
            var chart = chartResult?.Chart;
            var image = imageResult?.Image;
            var status = chartResult?.Status ?? imageResult?.Status ?? ExtractionStatus.Succeeded;

            AddAsset(assets, chartResult?.Asset);
            AddAsset(assets, imageResult?.Asset);

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
            else if (kind == ElementKind.GraphicFrame)
            {
                status = ExtractionStatus.Unsupported;
                diagnostics.Add(new ExtractionDiagnostic(
                    "DCX-GRAPHIC-FRAME-TYPE-UNSUPPORTED",
                    $"The graphic frame content '{ReadGraphicDataUri(sourceElement) ?? "unknown"}' is not supported.",
                    DiagnosticSeverity.Warning,
                    "SlideElementReader",
                    DiagnosticOutcome.Skipped,
                    source));
            }
            else if (nativeGeometry is null)
            {
                if (status == ExtractionStatus.Succeeded)
                {
                    status = ExtractionStatus.Partial;
                }

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

            if (kind == ElementKind.Group && groupTransform is null)
            {
                status = ExtractionStatus.Partial;
                diagnostics.Add(new ExtractionDiagnostic(
                    "DCX-GROUP-TRANSFORM-INCOMPLETE",
                    "The group does not declare a complete child coordinate transform; child slide geometry is unavailable.",
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
                table,
                chart,
                image,
                zOrderPath,
                groupTransform);
            elements.Add(element);

            if (sourceElement is P.GroupShape groupShape)
            {
                ReadElements(
                    groupShape,
                    slidePart,
                    sourceFileName,
                    slideIndex,
                    slidePartUri,
                    elementId,
                    GeometryCoordinateSpace.ParentGroup,
                    slideWidth,
                    slideHeight,
                    ComposeGroupChildTransform(containerToSlide, nativeGeometry, groupTransform),
                    zOrderPath,
                    assets,
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
            P.GraphicFrame graphicFrame when OpenXmlChartExtractor.IsChart(graphicFrame) => ElementKind.Chart,
            P.GraphicFrame => ElementKind.GraphicFrame,
            P.GroupShape => ElementKind.Group,
            P.ConnectionShape => ElementKind.Connector,
            _ => ElementKind.Unknown,
        };
    }

    private static string? ReadGraphicDataUri(OpenXmlElement element)
    {
        return element.Descendants()
            .FirstOrDefault(descendant => descendant.LocalName == "graphicData")?
            .GetAttributes()
            .FirstOrDefault(attribute => attribute.LocalName == "uri")
            .Value;
    }

    private static void AddAsset(
        IDictionary<string, OpenXmlExtractedAsset> assets,
        OpenXmlExtractedAsset? asset)
    {
        if (asset is null)
        {
            return;
        }

        var key = $"{asset.Kind}:{asset.PartUri}";
        assets.TryAdd(key, asset);
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

    private static GroupTransformContext? ReadGroupTransform(P.GroupShape groupShape)
    {
        var transform = groupShape.GroupShapeProperties?.TransformGroup;
        var childOffset = transform?.ChildOffset;
        var childExtents = transform?.ChildExtents;

        if (!TryReadLongAttribute(childOffset, "x", out var childX) ||
            !TryReadLongAttribute(childOffset, "y", out var childY) ||
            !TryReadLongAttribute(childExtents, "cx", out var childWidth) ||
            !TryReadLongAttribute(childExtents, "cy", out var childHeight) ||
            childWidth <= 0 || childHeight <= 0)
        {
            return null;
        }

        var rotation = ReadLongAttribute(transform, "rot");
        return new GroupTransformContext(
            childX,
            childY,
            childWidth,
            childHeight,
            rotation,
            rotation is null ? null : rotation.Value / RotationUnitScale,
            ReadBooleanAttribute(transform, "flipH"),
            ReadBooleanAttribute(transform, "flipV"));
    }

    private static AffineTransform? ComposeGroupOuterTransform(
        AffineTransform? containerToSlide,
        NativeGeometry? geometry,
        GroupTransformContext? groupTransform)
    {
        if (containerToSlide is null || geometry is null)
        {
            return containerToSlide;
        }

        return groupTransform is null
            ? containerToSlide
            : containerToSlide.Value.Compose(CreateGroupOuterTransform(geometry, groupTransform));
    }

    private static AffineTransform? ComposeGroupChildTransform(
        AffineTransform? containerToSlide,
        NativeGeometry? geometry,
        GroupTransformContext? groupTransform)
    {
        if (containerToSlide is null || geometry is null || groupTransform is null)
        {
            return null;
        }

        var scaleX = geometry.Width / (double)groupTransform.ChildExtentWidth;
        var scaleY = geometry.Height / (double)groupTransform.ChildExtentHeight;
        var childToGroup = new AffineTransform(
            scaleX,
            0,
            0,
            scaleY,
            geometry.X - (groupTransform.ChildOffsetX * scaleX),
            geometry.Y - (groupTransform.ChildOffsetY * scaleY));
        var groupOuter = CreateGroupOuterTransform(geometry, groupTransform);
        return containerToSlide.Value.Compose(groupOuter.Compose(childToGroup));
    }

    private static AffineTransform CreateGroupOuterTransform(
        NativeGeometry geometry,
        GroupTransformContext transform)
    {
        var flipX = transform.FlipHorizontal is true ? -1d : 1d;
        var flipY = transform.FlipVertical is true ? -1d : 1d;
        var radians = (transform.RotationDegrees ?? 0d) * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var centerX = geometry.X + (geometry.Width / 2d);
        var centerY = geometry.Y + (geometry.Height / 2d);
        var aroundOrigin = new AffineTransform(
            cosine * flipX,
            sine * flipX,
            -sine * flipY,
            cosine * flipY,
            0,
            0);

        return AffineTransform.Translation(centerX, centerY)
            .Compose(aroundOrigin)
            .Compose(AffineTransform.Translation(-centerX, -centerY));
    }

    private static NormalizedGeometry? NormalizeGeometry(
        NativeGeometry? nativeGeometry,
        AffineTransform? elementToSlide,
        long? slideWidth,
        long? slideHeight,
        SourceReference source,
        ICollection<ExtractionDiagnostic> diagnostics)
    {
        if (nativeGeometry is null)
        {
            return null;
        }

        if (elementToSlide is null || slideWidth is null or <= 0 || slideHeight is null or <= 0)
        {
            diagnostics.Add(new ExtractionDiagnostic(
                "DCX-GEOMETRY-NORMALIZATION-UNAVAILABLE",
                "Normalized geometry could not be calculated because the slide size or parent group transform is missing or invalid.",
                DiagnosticSeverity.Warning,
                "GeometryExtractor",
                DiagnosticOutcome.Partial,
                source));
            return null;
        }

        var bounds = elementToSlide.Value.TransformBounds(nativeGeometry);
        return new NormalizedGeometry(
            bounds.X / slideWidth.Value,
            bounds.Y / slideHeight.Value,
            bounds.Width / slideWidth.Value,
            bounds.Height / slideHeight.Value);
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
            "1" or "true" or "on" => true,
            "0" or "false" or "off" => false,
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

    private readonly record struct GeometryBounds(double X, double Y, double Width, double Height);

    private readonly record struct AffineTransform(
        double M11,
        double M12,
        double M21,
        double M22,
        double OffsetX,
        double OffsetY)
    {
        public static AffineTransform Identity { get; } = new(1, 0, 0, 1, 0, 0);

        public static AffineTransform Translation(double x, double y) => new(1, 0, 0, 1, x, y);

        public AffineTransform Compose(AffineTransform inner)
        {
            return new AffineTransform(
                (M11 * inner.M11) + (M21 * inner.M12),
                (M12 * inner.M11) + (M22 * inner.M12),
                (M11 * inner.M21) + (M21 * inner.M22),
                (M12 * inner.M21) + (M22 * inner.M22),
                (M11 * inner.OffsetX) + (M21 * inner.OffsetY) + OffsetX,
                (M12 * inner.OffsetX) + (M22 * inner.OffsetY) + OffsetY);
        }

        public GeometryBounds TransformBounds(NativeGeometry geometry)
        {
            var points = new[]
            {
                Transform(geometry.X, geometry.Y),
                Transform(geometry.X + geometry.Width, geometry.Y),
                Transform(geometry.X, geometry.Y + geometry.Height),
                Transform(geometry.X + geometry.Width, geometry.Y + geometry.Height),
            };
            var minimumX = points.Min(point => point.X);
            var maximumX = points.Max(point => point.X);
            var minimumY = points.Min(point => point.Y);
            var maximumY = points.Max(point => point.Y);
            return new GeometryBounds(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
        }

        private (double X, double Y) Transform(double x, double y)
        {
            return (
                (M11 * x) + (M21 * y) + OffsetX,
                (M12 * x) + (M22 * y) + OffsetY);
        }
    }
}
