using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;

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
        Assert.Equal(2, slide.Elements.Count);

        var group = slide.Elements[0];
        Assert.Equal(ElementKind.Group, group.Kind);
        Assert.Equal("10", group.Identity.Id);
        Assert.Null(group.ParentGroupId);
        Assert.Equal(GeometryCoordinateSpace.Slide, group.NativeGeometry?.CoordinateSpace);
        Assert.NotNull(group.NormalizedGeometry);

        var child = slide.Elements[1];
        Assert.Equal(ElementKind.Shape, child.Kind);
        Assert.Equal("11", child.Identity.Id);
        Assert.Equal("10", child.ParentGroupId);
        Assert.Equal(GeometryCoordinateSpace.ParentGroup, child.NativeGeometry?.CoordinateSpace);
        Assert.Null(child.NormalizedGeometry);
        Assert.Equal("Grouped evidence", child.Text?.Paragraphs[0].Runs[0].Text);
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
