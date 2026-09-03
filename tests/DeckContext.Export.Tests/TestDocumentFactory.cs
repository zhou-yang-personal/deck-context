using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;

namespace DeckContext.Export.Tests;

internal static class TestDocumentFactory
{
    public static DeckContextDocument CreateRich()
    {
        var source = new SourceReference(
            "sample.pptx",
            "/ppt/slides/slide1.xml",
            "rId1",
            1);
        var text = new TextContentContext(
            [
                new TextParagraphContext(
                    0,
                    0,
                    "l",
                    null,
                    [new TextRunContext(TextRunKind.Text, "Fiber growth is shifting", null)]),
            ]);
        var table = new TableContext(
            1,
            2,
            [
                new TableRowContext(
                    0,
                    200,
                    [
                        new TableCellContext(0, 0, 1, 1, false, false, CellText("Operator"), null),
                        new TableCellContext(0, 1, 1, 1, false, false, CellText("Share | 35%"), null),
                    ]),
            ]);
        var workbook = new EmbeddedWorkbookContext(
            "rId5",
            "/ppt/charts/chart1.xml",
            "/ppt/embeddings/workbook1.xlsx",
            "chart1-workbook.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            100,
            new string('b', 64),
            ExtractionStatus.Succeeded,
            [new WorkbookWorksheetContext("Data", "1", "rId1", "/xl/worksheets/sheet1.xml", ["range-001"])],
            [
                new WorkbookRangeContext(
                    "range-001",
                    "Data!$B$2:$B$3",
                    "Data",
                    "B2:B3",
                    [
                        new WorkbookCellContext("B2", 2, 2, true, null, null, null, "12", "12"),
                        new WorkbookCellContext("B3", 3, 2, true, null, null, "B2+3", "15", "15"),
                    ]),
            ],
            []);
        var chart = new ChartContext(
            "rId3",
            "/ppt/charts/chart1.xml",
            "Growth",
            [
                new ChartPlotContext(
                    "lineChart",
                    [
                        new ChartSeriesContext(
                            0,
                            0,
                            "Actual",
                            null,
                            new ChartDataSourceContext(null, null, [new ChartDataPointContext(0, "2025", 2025)]),
                            new ChartDataSourceContext(
                                "Data!$B$2:$B$3",
                                "0",
                                [new ChartDataPointContext(0, "12", 12), new ChartDataPointContext(1, "15", 15)],
                                "range-001")),
                    ],
                    new ChartDataLabelsContext(false, null, null, null, null, null, null, null, null, null)),
            ],
            new ChartLegendContext(false, null, null, []),
            [],
            "rId5",
            false,
            workbook);
        var image = new ImageContext(
            "rId4",
            "/ppt/media/image1.png",
            null,
            "image/png",
            ".png",
            "image1.png",
            80,
            new string('c', 64),
            "Market coverage map",
            null,
            null,
            new ImageTransformContext(null, null, false, false),
            new ImageContentInterpretationContext(ImageContentInterpretationStatus.NotConfigured, null, null, null),
            ExtractionStatus.Succeeded);
        var imageDiagnostic = new ExtractionDiagnostic(
            "DCX-IMAGE-TEXT-PROVIDER-NOT-CONFIGURED",
            "Image pixels were not interpreted because no OCR/Vision provider is configured.",
            DiagnosticSeverity.Information,
            "ImageExtractor",
            DiagnosticOutcome.None,
            source with { ElementId = "4", ElementName = "Map" });
        var unsupportedDiagnostic = new ExtractionDiagnostic(
            "DCX-ELEMENT-TYPE-UNSUPPORTED",
            "Unsupported object.",
            DiagnosticSeverity.Warning,
            "SlideElementReader",
            DiagnosticOutcome.Skipped,
            source with { ElementId = "5", ElementName = "Unsupported" });

        return new DeckContextDocument(
            DeckContextDocument.CurrentSchemaVersion,
            new DeckMetadata("sample.pptx", "/ppt/presentation.xml", 1000, 500, 1),
            [
                new SlideContext(
                    new SlideMetadata(1, "256", "rId1", "/ppt/slides/slide1.xml", 1000, 500),
                    [
                        Element("1", "Conclusion", ElementKind.Shape, 0, text: text),
                        Element("2", "Comparison", ElementKind.Table, 1, table: table),
                        Element("3", "Growth chart", ElementKind.Chart, 2, chart: chart),
                        Element("4", "Map", ElementKind.Picture, 3, image: image, diagnostics: [imageDiagnostic]),
                        Element(
                            "5",
                            "Unsupported",
                            ElementKind.Unknown,
                            4,
                            status: ExtractionStatus.Unsupported,
                            diagnostics: [unsupportedDiagnostic]),
                    ],
                    ExtractionStatus.Partial,
                    []),
            ],
            ExtractionStatus.Partial,
            []);
    }

    private static SlideElementContext Element(
        string id,
        string name,
        ElementKind kind,
        int zOrder,
        ExtractionStatus status = ExtractionStatus.Succeeded,
        TextContentContext? text = null,
        TableContext? table = null,
        ChartContext? chart = null,
        ImageContext? image = null,
        IReadOnlyList<ExtractionDiagnostic>? diagnostics = null)
    {
        return new SlideElementContext(
            new ElementIdentity(id, name),
            kind,
            new SourceReference("sample.pptx", "/ppt/slides/slide1.xml", "rId1", 1, id, name),
            zOrder,
            new NativeGeometry(zOrder * 100, zOrder * 50, 400, 100, GeometryCoordinateSpace.Slide),
            new NormalizedGeometry(zOrder * 0.1, zOrder * 0.1, 0.4, 0.2),
            status,
            diagnostics ?? [],
            Text: text,
            Table: table,
            Chart: chart,
            Image: image);
    }

    private static TextContentContext CellText(string value)
    {
        return new TextContentContext(
            [new TextParagraphContext(0, 0, "l", null, [new TextRunContext(TextRunKind.Text, value, null)])]);
    }
}
