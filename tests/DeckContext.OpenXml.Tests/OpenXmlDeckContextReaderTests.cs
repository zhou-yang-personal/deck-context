using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;
using DeckContext.Export;
using System.IO.Compression;
using System.Security.Cryptography;

namespace DeckContext.OpenXml.Tests;

public sealed class OpenXmlDeckContextReaderTests
{
    [Fact]
    public void Read_extracts_slide_identity_size_order_and_top_level_elements()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateBasic(directory.Path);
        var reader = new OpenXmlDeckContextReader();

        var document = reader.Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Succeeded, document.Status);
        Assert.Equal("presentation-basic.pptx", document.Deck.SourceFileName);
        Assert.Equal("/ppt/presentation.xml", document.Deck.PresentationPartUri);
        Assert.Equal(12_192_000L, document.Deck.SlideWidthEmu);
        Assert.Equal(6_858_000L, document.Deck.SlideHeightEmu);
        Assert.Equal(2, document.Deck.SlideCount);
        Assert.Equal(2, document.Slides.Count);

        var firstSlide = document.Slides[0];
        Assert.Equal(1, firstSlide.Metadata.Index);
        Assert.Equal("256", firstSlide.Metadata.SlideId);
        Assert.Equal("rId1", firstSlide.Metadata.RelationshipId);
        Assert.Equal("/ppt/slides/slide1.xml", firstSlide.Metadata.PartUri);
        var firstElement = Assert.Single(firstSlide.Elements);
        Assert.Equal(ElementKind.Shape, firstElement.Kind);
        Assert.Equal("2", firstElement.Identity.Id);
        Assert.Equal("Title 1", firstElement.Identity.Name);
        Assert.Equal(0, firstElement.ZOrder);
        Assert.Equal(ExtractionStatus.Succeeded, firstElement.Status);
        var nativeGeometry = Assert.IsType<NativeGeometry>(firstElement.NativeGeometry);
        Assert.Equal(1_000_000L, nativeGeometry.X);
        Assert.Equal(GeometryCoordinateSpace.Slide, nativeGeometry.CoordinateSpace);
        var normalizedGeometry = Assert.IsType<NormalizedGeometry>(firstElement.NormalizedGeometry);
        Assert.Equal(
            1_000_000d / 12_192_000d,
            normalizedGeometry.X,
            precision: 8);

        var text = Assert.IsType<TextContentContext>(firstElement.Text);
        var paragraph = Assert.Single(text.Paragraphs);
        Assert.Equal(0, paragraph.Level);
        Assert.Equal("l", paragraph.Alignment);
        Assert.Equal(18d, paragraph.DefaultStyle?.FontSizePoints);
        Assert.Equal("Arial", paragraph.DefaultStyle?.LatinTypeface);
        var run = Assert.Single(paragraph.Runs);
        Assert.Equal(TextRunKind.Text, run.Kind);
        Assert.Equal("Slide One", run.Text);
        Assert.Equal("en-US", run.DirectStyle?.Language);
        Assert.Equal(24d, run.DirectStyle?.FontSizePoints);
        Assert.True(run.DirectStyle?.Bold is true);
        Assert.Equal("srgbClr", run.DirectStyle?.Color?.Type);
        Assert.Equal("D60000", run.DirectStyle?.Color?.Value);

        var secondElement = Assert.Single(document.Slides[1].Elements);
        Assert.Equal(ElementKind.Connector, secondElement.Kind);
        Assert.Equal("3", secondElement.Identity.Id);
        Assert.Equal("Connector 1", secondElement.Identity.Name);
    }

    [Fact]
    public void Read_returns_failed_document_and_diagnostic_for_malformed_package()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateMalformed(directory.Path);
        var reader = new OpenXmlDeckContextReader();

        var document = reader.Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Failed, document.Status);
        Assert.Empty(document.Slides);
        var diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("DCX-PACKAGE-OPEN-FAILED", diagnostic.Code);
        Assert.Equal("OpenXmlPackageReader", diagnostic.Extractor);
    }

    [Fact]
    public void Read_degrades_only_the_unresolved_slide_relationship()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateMissingSlideRelationship(directory.Path);
        var reader = new OpenXmlDeckContextReader();

        var document = reader.Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Partial, document.Status);
        var slide = Assert.Single(document.Slides);
        Assert.Equal(ExtractionStatus.Failed, slide.Status);
        Assert.Empty(slide.Elements);
        var diagnostic = Assert.Single(slide.Diagnostics);
        Assert.Equal("DCX-SLIDE-RELATIONSHIP-FAILED", diagnostic.Code);
        Assert.Equal(1, diagnostic.Source?.SlideIndex);
        Assert.Equal("rId1", diagnostic.Source?.RelationshipId);
    }

    [Fact]
    public void Read_preserves_group_parentage_and_local_coordinate_space()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateGroup(directory.Path);
        var reader = new OpenXmlDeckContextReader();

        var document = reader.Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Succeeded, document.Status);
        var slide = Assert.Single(document.Slides);
        Assert.Equal(3, slide.Elements.Count);

        var precedingShape = slide.Elements[0];
        Assert.Equal("9", precedingShape.Identity.Id);
        Assert.Equal<int>([0], Assert.IsAssignableFrom<IReadOnlyList<int>>(precedingShape.ZOrderPath));

        var group = slide.Elements[1];
        Assert.Equal(ElementKind.Group, group.Kind);
        Assert.Equal("10", group.Identity.Id);
        Assert.Null(group.ParentGroupId);
        Assert.Equal(GeometryCoordinateSpace.Slide, group.NativeGeometry?.CoordinateSpace);
        Assert.NotNull(group.NormalizedGeometry);
        Assert.Equal<int>([1], Assert.IsAssignableFrom<IReadOnlyList<int>>(group.ZOrderPath));
        var groupTransform = Assert.IsType<GroupTransformContext>(group.GroupTransform);
        Assert.Equal(2_000_000L, groupTransform.ChildExtentWidth);
        Assert.Equal(1_000_000L, groupTransform.ChildExtentHeight);

        var child = slide.Elements[2];
        Assert.Equal(ElementKind.Shape, child.Kind);
        Assert.Equal("11", child.Identity.Id);
        Assert.Equal("10", child.ParentGroupId);
        Assert.Equal(GeometryCoordinateSpace.ParentGroup, child.NativeGeometry?.CoordinateSpace);
        Assert.Equal<int>([1, 0], Assert.IsAssignableFrom<IReadOnlyList<int>>(child.ZOrderPath));
        var normalized = Assert.IsType<NormalizedGeometry>(child.NormalizedGeometry);
        Assert.Equal(2_500_000d / 12_192_000d, normalized.X, precision: 8);
        Assert.Equal(1_600_000d / 6_858_000d, normalized.Y, precision: 8);
        Assert.Equal(4_000_000d / 12_192_000d, normalized.Width, precision: 8);
        Assert.Equal(1_000_000d / 6_858_000d, normalized.Height, precision: 8);
        Assert.Equal("Grouped evidence", child.Text?.Paragraphs[0].Runs[0].Text);

        var markdown = new DeckContextMarkdownExporter().Serialize(document);
        Assert.True(
            markdown.IndexOf("Before Group", StringComparison.Ordinal) <
            markdown.IndexOf("Evidence Group", StringComparison.Ordinal));
        Assert.True(
            markdown.IndexOf("Evidence Group", StringComparison.Ordinal) <
            markdown.IndexOf("Grouped Text", StringComparison.Ordinal));
        Assert.Contains("#### 2.1. Shape", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_marks_unhandled_graphic_frame_content_as_unsupported()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateUnsupportedGraphicFrame(directory.Path);

        var document = new OpenXmlDeckContextReader().Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Partial, document.Status);
        var element = Assert.Single(Assert.Single(document.Slides).Elements);
        Assert.Equal(ElementKind.GraphicFrame, element.Kind);
        Assert.Equal(ExtractionStatus.Unsupported, element.Status);
        var diagnostic = Assert.Single(element.Diagnostics);
        Assert.Equal("DCX-GRAPHIC-FRAME-TYPE-UNSUPPORTED", diagnostic.Code);
        Assert.Contains("/diagram", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_preserves_native_table_structure_merges_text_and_direct_fill()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateTable(directory.Path);
        var reader = new OpenXmlDeckContextReader();

        var document = reader.Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Succeeded, document.Status);
        var slide = Assert.Single(document.Slides);
        var element = Assert.Single(slide.Elements);
        Assert.Equal(ElementKind.Table, element.Kind);
        Assert.Equal("20", element.Identity.Id);
        Assert.Equal("Plan Comparison", element.Identity.Name);

        var table = Assert.IsType<TableContext>(element.Table);
        Assert.Equal(2, table.RowCount);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(2, table.Rows.Count);
        Assert.All(table.Rows, row => Assert.Equal(3, row.Cells.Count));
        Assert.Equal(1_200_000L, table.Rows[0].HeightEmu);

        var mergeRoot = table.Rows[0].Cells[0];
        Assert.Equal(2, mergeRoot.ColumnSpan);
        Assert.False(mergeRoot.IsHorizontalMergeContinuation);
        Assert.Equal("Combined Header", mergeRoot.Text.Paragraphs[0].Runs[0].Text);
        Assert.True(mergeRoot.Text.Paragraphs[0].Runs[0].DirectStyle?.Bold is true);
        Assert.Equal("srgbClr", mergeRoot.DirectFill?.Type);
        Assert.Equal("D9EAF7", mergeRoot.DirectFill?.Value);

        var mergeContinuation = table.Rows[0].Cells[1];
        Assert.True(mergeContinuation.IsHorizontalMergeContinuation);
        Assert.Equal("$35", table.Rows[1].Cells[2].Text.Paragraphs[0].Runs[0].Text);
    }

    [Fact]
    public void Read_extracts_native_chart_structure_cached_data_formulas_and_presentation_state()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateChart(directory.Path);
        var reader = new OpenXmlDeckContextReader();

        var document = reader.Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Succeeded, document.Status);
        var slide = Assert.Single(document.Slides);
        var element = Assert.Single(slide.Elements);
        Assert.Equal(ElementKind.Chart, element.Kind);
        Assert.Equal("30", element.Identity.Id);
        Assert.Equal("Subscriber Growth", element.Identity.Name);
        Assert.Equal(ExtractionStatus.Succeeded, element.Status);

        var chart = Assert.IsType<ChartContext>(element.Chart);
        Assert.Equal("rId1", chart.RelationshipId);
        Assert.Equal("/ppt/charts/chart1.xml", chart.PartUri);
        Assert.Equal("Subscriber Growth", chart.Title);

        var plot = Assert.Single(chart.Plots);
        Assert.Equal("barChart", plot.Type);
        Assert.Equal(2, plot.Series.Count);
        Assert.True(plot.DataLabels.IsPresent);
        Assert.Equal("outEnd", plot.DataLabels.Position);
        Assert.Equal("#,##0", plot.DataLabels.NumberFormatCode);
        Assert.True(plot.DataLabels.NumberFormatLinkedToSource is true);
        Assert.Equal(";", plot.DataLabels.Separator);
        Assert.True(plot.DataLabels.ShowValue is true);
        Assert.True(plot.DataLabels.ShowCategoryName is false);

        var firstSeries = plot.Series[0];
        Assert.Equal(0, firstSeries.Index);
        Assert.Equal(0, firstSeries.Order);
        Assert.Equal("Operator A", firstSeries.Name);
        Assert.Equal("Data!$B$1", firstSeries.NameFormula);
        var categories = Assert.IsType<ChartDataSourceContext>(firstSeries.Categories);
        Assert.Equal("Data!$A$2:$A$4", categories.Formula);
        Assert.Equal<string?>(["2024", "2025", "2026"], categories.Points.Select(point => point.Value));
        var values = Assert.IsType<ChartDataSourceContext>(firstSeries.Values);
        Assert.Equal("Data!$B$2:$B$4", values.Formula);
        Assert.Equal("#,##0", values.NumberFormatCode);
        Assert.Equal<double?>([100d, 125.5d, 150d], values.Points.Select(point => point.NumericValue));

        Assert.True(chart.Legend.IsPresent);
        Assert.Equal("r", chart.Legend.Position);
        Assert.True(chart.Legend.Overlay is false);
        Assert.Equal(2, chart.Legend.Entries.Count);
        Assert.True(chart.Legend.Entries[0].IsVisible);
        Assert.False(chart.Legend.Entries[1].IsVisible);

        Assert.Equal(2, chart.Axes.Count);
        var valueAxis = Assert.Single(chart.Axes, axis => axis.Type == "valAx");
        Assert.Equal("1002", valueAxis.Id);
        Assert.Equal("1001", valueAxis.CrossAxisId);
        Assert.Equal("Subscribers", valueAxis.Title);
        Assert.Equal("#,##0", valueAxis.NumberFormatCode);
        Assert.True(valueAxis.NumberFormatLinkedToSource is false);
        Assert.Equal(0d, valueAxis.Minimum);
        Assert.Equal(200d, valueAxis.Maximum);
        Assert.Equal(50d, valueAxis.MajorUnit);
        Assert.Equal(10d, valueAxis.MinorUnit);
    }

    [Fact]
    public void Read_marks_only_an_unsupported_chart_variant_as_unsupported()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateUnsupportedChart(directory.Path);
        var reader = new OpenXmlDeckContextReader();

        var document = reader.Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Partial, document.Status);
        var slide = Assert.Single(document.Slides);
        Assert.Equal(ExtractionStatus.Partial, slide.Status);
        var element = Assert.Single(slide.Elements);
        Assert.Equal(ElementKind.Chart, element.Kind);
        Assert.Equal(ExtractionStatus.Unsupported, element.Status);
        Assert.Equal("surface3DChart", Assert.Single(element.Chart?.Plots ?? []).Type);
        var diagnostic = Assert.Single(element.Diagnostics);
        Assert.Equal("DCX-CHART-TYPE-UNSUPPORTED", diagnostic.Code);
        Assert.Equal("ChartExtractor", diagnostic.Extractor);
        Assert.Equal(DiagnosticOutcome.Partial, diagnostic.Outcome);
        Assert.NotNull(element.Chart);
    }

    [Fact]
    public void Read_preserves_bubble_chart_size_values()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateBubbleChart(directory.Path);

        var document = new OpenXmlDeckContextReader().Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Succeeded, document.Status);
        var chart = Assert.IsType<ChartContext>(Assert.Single(Assert.Single(document.Slides).Elements).Chart);
        var series = Assert.Single(Assert.Single(chart.Plots).Series);
        Assert.Equal<double?>([10d, 20d], series.Categories?.Points.Select(point => point.NumericValue));
        Assert.Equal<double?>([30d, 40d], series.Values?.Points.Select(point => point.NumericValue));
        Assert.Equal<double?>([5d, 9d], series.BubbleSizes?.Points.Select(point => point.NumericValue));
    }

    [Fact]
    public void Read_degrades_only_a_chart_with_an_unresolved_relationship()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateMissingChartRelationship(directory.Path);
        var reader = new OpenXmlDeckContextReader();

        var document = reader.Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Partial, document.Status);
        var slide = Assert.Single(document.Slides);
        Assert.Equal(ExtractionStatus.Partial, slide.Status);
        var element = Assert.Single(slide.Elements);
        Assert.Equal(ElementKind.Chart, element.Kind);
        Assert.Equal(ExtractionStatus.Failed, element.Status);
        Assert.Null(element.Chart);
        var diagnostic = Assert.Single(element.Diagnostics);
        Assert.Equal("DCX-CHART-RELATIONSHIP-FAILED", diagnostic.Code);
        Assert.Equal(DiagnosticOutcome.Skipped, diagnostic.Outcome);
        Assert.Equal("/ppt/slides/slide1.xml", diagnostic.Source?.PartUri);
        Assert.Equal("rId1", diagnostic.Source?.RelationshipId);
    }

    [Fact]
    public void Read_traces_chart_formulas_to_embedded_workbook_ranges_and_cells()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateEmbeddedWorkbookChart(directory.Path);
        var reader = new OpenXmlDeckContextReader();

        var document = reader.Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Succeeded, document.Status);
        var element = Assert.Single(Assert.Single(document.Slides).Elements);
        Assert.Equal(ExtractionStatus.Succeeded, element.Status);
        var chart = Assert.IsType<ChartContext>(element.Chart);
        Assert.Equal("rId1", chart.ExternalDataRelationshipId);
        Assert.True(chart.ExternalDataAutoUpdate is false);

        var workbook = Assert.IsType<EmbeddedWorkbookContext>(chart.EmbeddedWorkbook);
        Assert.Equal(ExtractionStatus.Succeeded, workbook.Status);
        Assert.Equal("rId1", workbook.RelationshipId);
        Assert.Equal("/ppt/charts/chart1.xml", workbook.ChartPartUri);
        Assert.Equal("/ppt/embeddings/workbook1.xlsx", workbook.PartUri);
        Assert.Equal("chart1-workbook.xlsx", workbook.SuggestedFileName);
        Assert.True(workbook.SizeBytes > 0);
        Assert.Matches("^[0-9a-f]{64}$", workbook.Sha256);
        Assert.Empty(workbook.Diagnostics);

        var worksheet = Assert.Single(workbook.Worksheets);
        Assert.Equal("Data", worksheet.Name);
        Assert.Equal("1", worksheet.SheetId);
        Assert.Equal("rId1", worksheet.RelationshipId);
        Assert.Equal("/xl/worksheets/sheet1.xml", worksheet.PartUri);
        Assert.Equal(3, worksheet.ReferencedRangeIds.Count);
        Assert.Equal(3, workbook.ReferencedRanges.Count);

        var series = Assert.Single(chart.Plots[0].Series, item => item.Index == 0);
        Assert.Equal("range-001", series.NameWorkbookRangeId);
        Assert.Equal("range-002", series.Categories?.WorkbookRangeId);
        Assert.Equal("range-003", series.Values?.WorkbookRangeId);

        var nameRange = Assert.Single(workbook.ReferencedRanges, range => range.Id == "range-001");
        Assert.Equal("Data", nameRange.WorksheetName);
        Assert.Equal("B1", nameRange.Address);
        Assert.Equal("Operator A", Assert.Single(nameRange.Cells).ResolvedValue);

        var categoryRange = Assert.Single(workbook.ReferencedRanges, range => range.Id == "range-002");
        Assert.Equal("A2:A4", categoryRange.Address);
        Assert.Equal<string?>(["2024", "2025", "2026"], categoryRange.Cells.Select(cell => cell.ResolvedValue));

        var valueRange = Assert.Single(workbook.ReferencedRanges, range => range.Id == "range-003");
        Assert.Equal("B2:B4", valueRange.Address);
        Assert.Equal<string?>(["100", "125.5", "150"], valueRange.Cells.Select(cell => cell.RawValue));
        Assert.Equal("SUM(B2:B3)+24.5", valueRange.Cells[2].Formula);
        Assert.True(valueRange.Cells[2].IsPresent);
    }

    [Fact]
    public void Export_copies_the_exact_traced_embedded_workbook_asset()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateEmbeddedWorkbookChart(directory.Path);
        var document = new OpenXmlDeckContextReader().Read(path, TestContext.Current.CancellationToken);
        var workbook = Assert.IsType<EmbeddedWorkbookContext>(
            Assert.Single(Assert.Single(document.Slides).Elements).Chart?.EmbeddedWorkbook);
        var destination = Path.Combine(directory.Path, workbook.SuggestedFileName);

        new OpenXmlEmbeddedWorkbookAssetExporter().Export(
            path,
            workbook,
            destination,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(destination));
        Assert.Equal(
            workbook.Sha256,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))).ToLowerInvariant());
        using var archive = ZipFile.OpenRead(destination);
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
    }

    [Fact]
    public void Read_keeps_chart_cache_and_marks_only_chart_partial_when_workbook_is_malformed()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateMalformedEmbeddedWorkbookChart(directory.Path);

        var document = new OpenXmlDeckContextReader().Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Partial, document.Status);
        var element = Assert.Single(Assert.Single(document.Slides).Elements);
        Assert.Equal(ExtractionStatus.Partial, element.Status);
        var chart = Assert.IsType<ChartContext>(element.Chart);
        var workbook = Assert.IsType<EmbeddedWorkbookContext>(chart.EmbeddedWorkbook);
        Assert.Equal(ExtractionStatus.Failed, workbook.Status);
        Assert.Empty(workbook.Worksheets);
        Assert.Empty(workbook.ReferencedRanges);
        Assert.True(workbook.SizeBytes > 0);
        Assert.Equal(100d, chart.Plots[0].Series[0].Values?.Points[0].NumericValue);
        var diagnostic = Assert.Single(
            element.Diagnostics,
            item => item.Code == "DCX-WORKBOOK-READ-FAILED");
        Assert.Equal("EmbeddedWorkbookExtractor", diagnostic.Extractor);
        Assert.Equal(DiagnosticOutcome.Partial, diagnostic.Outcome);
        Assert.Equal("/ppt/charts/chart1.xml", diagnostic.Source?.PartUri);
        Assert.Equal("rId1", diagnostic.Source?.RelationshipId);
    }

    [Fact]
    public void Read_extracts_image_media_crop_transform_and_explicit_not_configured_interpretation()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateImage(directory.Path);

        var document = new OpenXmlDeckContextReader().Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Succeeded, document.Status);
        var element = Assert.Single(Assert.Single(document.Slides).Elements);
        Assert.Equal(ElementKind.Picture, element.Kind);
        Assert.Equal("40", element.Identity.Id);
        Assert.Equal("Market Map", element.Identity.Name);
        Assert.Equal(ExtractionStatus.Succeeded, element.Status);
        Assert.Equal(1_200_000L, element.NativeGeometry?.X);
        Assert.Equal(6_000_000L, element.NativeGeometry?.Width);

        var image = Assert.IsType<ImageContext>(element.Image);
        Assert.Equal(ExtractionStatus.Succeeded, image.Status);
        Assert.Equal("rId1", image.RelationshipId);
        Assert.Equal("/ppt/media/image1.png", image.PartUri);
        Assert.Null(image.ExternalUri);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(".png", image.FileExtension);
        Assert.Equal("image1.png", image.SuggestedFileName);
        Assert.True(image.SizeBytes > 0);
        Assert.Matches("^[0-9a-f]{64}$", image.Sha256);
        Assert.Equal("Source-backed market coverage map", image.AlternativeText);
        Assert.Equal("Coverage map", image.Title);

        var crop = Assert.IsType<ImageCropContext>(image.Crop);
        Assert.Equal(10_000, crop.LeftRaw);
        Assert.Equal(20_000, crop.TopRaw);
        Assert.Equal(5_000, crop.RightRaw);
        Assert.Equal(0, crop.BottomRaw);
        Assert.Equal(0.1d, crop.LeftFraction);
        Assert.Equal(0.2d, crop.TopFraction);
        Assert.Equal(0.05d, crop.RightFraction);
        Assert.Equal(0d, crop.BottomFraction);

        Assert.Equal(5_400_000L, image.Transform.RotationUnits);
        Assert.Equal(90d, image.Transform.RotationDegrees);
        Assert.True(image.Transform.FlipHorizontal is true);
        Assert.True(image.Transform.FlipVertical is false);
        Assert.Equal(ImageContentInterpretationStatus.NotConfigured, image.Interpretation.Status);
        Assert.Null(image.Interpretation.ProviderId);
        Assert.Null(image.Interpretation.Text);
        Assert.Null(image.Interpretation.Description);

        var diagnostic = Assert.Single(
            element.Diagnostics,
            item => item.Code == "DCX-IMAGE-TEXT-PROVIDER-NOT-CONFIGURED");
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Equal(DiagnosticOutcome.None, diagnostic.Outcome);
    }

    [Fact]
    public void Export_copies_the_exact_traced_image_asset()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateImage(directory.Path);
        var document = new OpenXmlDeckContextReader().Read(path, TestContext.Current.CancellationToken);
        var image = Assert.IsType<ImageContext>(
            Assert.Single(Assert.Single(document.Slides).Elements).Image);
        var destination = Path.Combine(directory.Path, "exported", image.SuggestedFileName!);

        new OpenXmlImageAssetExporter().Export(
            path,
            image,
            destination,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(destination));
        var bytes = File.ReadAllBytes(destination);
        Assert.Equal(
            image.Sha256,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Assert.Equal<byte>([0x89, 0x50, 0x4E, 0x47], bytes.Take(4));
    }

    [Fact]
    public void Read_marks_only_picture_failed_when_its_media_relationship_is_missing()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateMissingImageRelationship(directory.Path);

        var document = new OpenXmlDeckContextReader().Read(path, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Partial, document.Status);
        var element = Assert.Single(Assert.Single(document.Slides).Elements);
        Assert.Equal(ElementKind.Picture, element.Kind);
        Assert.Equal(ExtractionStatus.Failed, element.Status);
        var image = Assert.IsType<ImageContext>(element.Image);
        Assert.Equal(ExtractionStatus.Failed, image.Status);
        Assert.Equal("rId1", image.RelationshipId);
        Assert.Null(image.PartUri);
        Assert.Null(image.Sha256);
        Assert.Equal(ImageContentInterpretationStatus.NotConfigured, image.Interpretation.Status);
        var diagnostic = Assert.Single(
            element.Diagnostics,
            item => item.Code == "DCX-IMAGE-RELATIONSHIP-FAILED");
        Assert.Equal(DiagnosticOutcome.Skipped, diagnostic.Outcome);
        Assert.Equal("/ppt/slides/slide1.xml", diagnostic.Source?.PartUri);
        Assert.Equal("rId1", diagnostic.Source?.RelationshipId);
    }

    [Fact]
    public void ReadPackage_returns_asset_bytes_from_the_same_package_session()
    {
        using var directory = new TemporaryDirectory();
        var path = PresentationFixture.CreateImage(directory.Path);

        var result = new OpenXmlDeckContextReader().ReadPackage(
            path,
            TestContext.Current.CancellationToken);

        var asset = Assert.Single(result.Assets);
        Assert.Equal(OpenXmlExtractedAssetKind.Image, asset.Kind);
        Assert.Equal("/ppt/media/image1.png", asset.PartUri);
        Assert.Equal(asset.SizeBytes, asset.Content.Length);
        Assert.Equal(
            asset.Sha256,
            Convert.ToHexString(SHA256.HashData(asset.Content.Span)).ToLowerInvariant());
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "deck-context-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
