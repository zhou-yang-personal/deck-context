using System.IO.Compression;
using System.Text;

namespace DeckContext.OpenXml.Tests;

internal static class PresentationFixture
{
    private static readonly DateTimeOffset FixedEntryTime =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static string CreateBasic(string directory)
    {
        var path = Path.Combine(directory, "presentation-basic.pptx");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", ContentTypes(twoSlides: true));
        WriteEntry(archive, "_rels/.rels", PackageRelationships);
        WriteEntry(archive, "ppt/presentation.xml", PresentationXml(twoSlides: true));
        WriteEntry(archive, "ppt/_rels/presentation.xml.rels", PresentationRelationships(twoSlides: true));
        WriteEntry(archive, "ppt/slides/slide1.xml", SlideWithShape);
        WriteEntry(archive, "ppt/slides/slide2.xml", SlideWithConnector);

        return path;
    }

    public static string CreateMissingSlideRelationship(string directory)
    {
        var path = Path.Combine(directory, "missing-slide-relationship.pptx");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", ContentTypes(twoSlides: false));
        WriteEntry(archive, "_rels/.rels", PackageRelationships);
        WriteEntry(archive, "ppt/presentation.xml", PresentationXml(twoSlides: false));

        return path;
    }

    public static string CreateGroup(string directory)
    {
        var path = Path.Combine(directory, "groups-basic.pptx");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", ContentTypes(twoSlides: false));
        WriteEntry(archive, "_rels/.rels", PackageRelationships);
        WriteEntry(archive, "ppt/presentation.xml", PresentationXml(twoSlides: false));
        WriteEntry(archive, "ppt/_rels/presentation.xml.rels", PresentationRelationships(twoSlides: false));
        WriteEntry(archive, "ppt/slides/slide1.xml", SlideWithGroup);

        return path;
    }

    public static string CreateTable(string directory)
    {
        var path = Path.Combine(directory, "table-basic.pptx");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", ContentTypes(twoSlides: false));
        WriteEntry(archive, "_rels/.rels", PackageRelationships);
        WriteEntry(archive, "ppt/presentation.xml", PresentationXml(twoSlides: false));
        WriteEntry(archive, "ppt/_rels/presentation.xml.rels", PresentationRelationships(twoSlides: false));
        WriteEntry(archive, "ppt/slides/slide1.xml", SlideWithTable);

        return path;
    }

    public static string CreateImage(string directory)
    {
        return CreateImagePackage(directory, "images-basic.pptx", includeRelationship: true);
    }

    public static string CreateMissingImageRelationship(string directory)
    {
        return CreateImagePackage(directory, "images-missing-relationship.pptx", includeRelationship: false);
    }

    public static string CreateChart(string directory)
    {
        return CreateChartPackage(directory, "chart-basic.pptx", BasicChart);
    }

    public static string CreateUnsupportedChart(string directory)
    {
        return CreateChartPackage(directory, "chart-unsupported.pptx", UnsupportedChart);
    }

    public static string CreateEmbeddedWorkbookChart(string directory)
    {
        return CreateChartPackage(
            directory,
            "chart-embedded-workbook.pptx",
            ChartWithExternalData,
            CreateWorkbookBytes());
    }

    public static string CreateMalformedEmbeddedWorkbookChart(string directory)
    {
        return CreateChartPackage(
            directory,
            "chart-malformed-embedded-workbook.pptx",
            ChartWithExternalData,
            [0x44, 0x43, 0x58]);
    }

    public static string CreateMissingChartRelationship(string directory)
    {
        var path = Path.Combine(directory, "chart-missing-relationship.pptx");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", ContentTypes(twoSlides: false));
        WriteEntry(archive, "_rels/.rels", PackageRelationships);
        WriteEntry(archive, "ppt/presentation.xml", PresentationXml(twoSlides: false));
        WriteEntry(archive, "ppt/_rels/presentation.xml.rels", PresentationRelationships(twoSlides: false));
        WriteEntry(archive, "ppt/slides/slide1.xml", SlideWithChart);

        return path;
    }

    public static string CreateMalformed(string directory)
    {
        var path = Path.Combine(directory, "malformed.pptx");
        File.WriteAllText(path, "not an OOXML package", Encoding.UTF8);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = FixedEntryTime;

        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteBinaryEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = FixedEntryTime;

        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string CreateChartPackage(
        string directory,
        string fileName,
        string chartXml,
        byte[]? workbookBytes = null)
    {
        var path = Path.Combine(directory, fileName);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            ContentTypes(twoSlides: false, hasChart: true, hasWorkbook: workbookBytes is not null));
        WriteEntry(archive, "_rels/.rels", PackageRelationships);
        WriteEntry(archive, "ppt/presentation.xml", PresentationXml(twoSlides: false));
        WriteEntry(archive, "ppt/_rels/presentation.xml.rels", PresentationRelationships(twoSlides: false));
        WriteEntry(archive, "ppt/slides/slide1.xml", SlideWithChart);
        WriteEntry(archive, "ppt/slides/_rels/slide1.xml.rels", SlideChartRelationships);
        WriteEntry(archive, "ppt/charts/chart1.xml", chartXml);

        if (workbookBytes is not null)
        {
            WriteEntry(archive, "ppt/charts/_rels/chart1.xml.rels", ChartWorkbookRelationships);
            WriteBinaryEntry(archive, "ppt/embeddings/workbook1.xlsx", workbookBytes);
        }

        return path;
    }

    private static string CreateImagePackage(
        string directory,
        string fileName,
        bool includeRelationship)
    {
        var path = Path.Combine(directory, fileName);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", ContentTypes(twoSlides: false, hasImage: true));
        WriteEntry(archive, "_rels/.rels", PackageRelationships);
        WriteEntry(archive, "ppt/presentation.xml", PresentationXml(twoSlides: false));
        WriteEntry(archive, "ppt/_rels/presentation.xml.rels", PresentationRelationships(twoSlides: false));
        WriteEntry(archive, "ppt/slides/slide1.xml", SlideWithImage);

        if (includeRelationship)
        {
            WriteEntry(archive, "ppt/slides/_rels/slide1.xml.rels", SlideImageRelationships);
            WriteBinaryEntry(archive, "ppt/media/image1.png", ImagePng);
        }

        return path;
    }

    private static string ContentTypes(
        bool twoSlides,
        bool hasChart = false,
        bool hasWorkbook = false,
        bool hasImage = false)
    {
        var secondSlide = twoSlides
            ? "<Override PartName=\"/ppt/slides/slide2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>"
            : string.Empty;
        var chart = hasChart
            ? "<Override PartName=\"/ppt/charts/chart1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>"
            : string.Empty;
        var workbook = hasWorkbook
            ? "<Default Extension=\"xlsx\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet\"/>"
            : string.Empty;
        var image = hasImage
            ? "<Default Extension=\"png\" ContentType=\"image/png\"/>"
            : string.Empty;

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
              <Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
              {secondSlide}
              {chart}
              {workbook}
              {image}
            </Types>
            """;
    }

    private static byte[] CreateWorkbookBytes()
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", WorkbookContentTypes);
            WriteEntry(archive, "_rels/.rels", WorkbookPackageRelationships);
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", WorkbookSheet);
            WriteEntry(archive, "xl/sharedStrings.xml", WorkbookSharedStrings);
        }

        return stream.ToArray();
    }

    private static string PresentationXml(bool twoSlides)
    {
        var secondSlide = twoSlides ? "<p:sldId id=\"257\" r:id=\"rId2\"/>" : string.Empty;

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <p:presentation xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                            xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
              <p:sldIdLst>
                <p:sldId id="256" r:id="rId1"/>
                {secondSlide}
              </p:sldIdLst>
              <p:sldSz cx="12192000" cy="6858000" type="screen16x9"/>
              <p:notesSz cx="6858000" cy="9144000"/>
            </p:presentation>
            """;
    }

    private static string PresentationRelationships(bool twoSlides)
    {
        var secondSlide = twoSlides
            ? "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide2.xml\"/>"
            : string.Empty;

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>
              {secondSlide}
            </Relationships>
            """;
    }

    private const string PackageRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>
        </Relationships>
        """;

    private const string SlideChartRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="../charts/chart1.xml"/>
        </Relationships>
        """;

    private const string SlideImageRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/image1.png"/>
        </Relationships>
        """;

    private const string ChartWorkbookRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/package" Target="../embeddings/workbook1.xlsx"/>
        </Relationships>
        """;

    private const string WorkbookContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
        </Types>
        """;

    private const string WorkbookPackageRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private const string WorkbookRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
        </Relationships>
        """;

    private const string WorkbookXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets><sheet name="Data" sheetId="1" r:id="rId1"/></sheets>
        </workbook>
        """;

    private const string WorkbookSharedStrings = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="4" uniqueCount="4">
          <si><t>Operator A</t></si>
          <si><t>2024</t></si>
          <si><t>2025</t></si>
          <si><t>2026</t></si>
        </sst>
        """;

    private const string WorkbookSheet = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1"><c r="B1" t="s"><v>0</v></c></row>
            <row r="2"><c r="A2" t="s"><v>1</v></c><c r="B2"><v>100</v></c></row>
            <row r="3"><c r="A3" t="s"><v>2</v></c><c r="B3"><v>125.5</v></c></row>
            <row r="4"><c r="A4" t="s"><v>3</v></c><c r="B4"><f>SUM(B2:B3)+24.5</f><v>150</v></c></row>
          </sheetData>
        </worksheet>
        """;

    private const string SlideWithShape = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
               xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
               xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
          <p:cSld>
            <p:spTree>
              <p:nvGrpSpPr>
                <p:cNvPr id="1" name=""/>
                <p:cNvGrpSpPr/>
                <p:nvPr/>
              </p:nvGrpSpPr>
              <p:grpSpPr>
                <a:xfrm>
                  <a:off x="0" y="0"/>
                  <a:ext cx="0" cy="0"/>
                  <a:chOff x="0" y="0"/>
                  <a:chExt cx="0" cy="0"/>
                </a:xfrm>
              </p:grpSpPr>
              <p:sp>
                <p:nvSpPr>
                  <p:cNvPr id="2" name="Title 1"/>
                  <p:cNvSpPr/>
                  <p:nvPr/>
                </p:nvSpPr>
                <p:spPr>
                  <a:xfrm>
                    <a:off x="1000000" y="500000"/>
                    <a:ext cx="6000000" cy="1000000"/>
                  </a:xfrm>
                </p:spPr>
                <p:txBody>
                  <a:bodyPr/>
                  <a:lstStyle/>
                  <a:p>
                    <a:pPr lvl="0" algn="l">
                      <a:defRPr sz="1800"><a:latin typeface="Arial"/></a:defRPr>
                    </a:pPr>
                    <a:r>
                      <a:rPr lang="en-US" sz="2400" b="1">
                        <a:solidFill><a:srgbClr val="D60000"/></a:solidFill>
                        <a:latin typeface="Arial"/>
                      </a:rPr>
                      <a:t>Slide One</a:t>
                    </a:r>
                  </a:p>
                </p:txBody>
              </p:sp>
            </p:spTree>
          </p:cSld>
          <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
        </p:sld>
        """;

    private const string SlideWithConnector = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
               xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
               xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
          <p:cSld>
            <p:spTree>
              <p:nvGrpSpPr>
                <p:cNvPr id="1" name=""/>
                <p:cNvGrpSpPr/>
                <p:nvPr/>
              </p:nvGrpSpPr>
              <p:grpSpPr>
                <a:xfrm>
                  <a:off x="0" y="0"/>
                  <a:ext cx="0" cy="0"/>
                  <a:chOff x="0" y="0"/>
                  <a:chExt cx="0" cy="0"/>
                </a:xfrm>
              </p:grpSpPr>
              <p:cxnSp>
                <p:nvCxnSpPr>
                  <p:cNvPr id="3" name="Connector 1"/>
                  <p:cNvCxnSpPr/>
                  <p:nvPr/>
                </p:nvCxnSpPr>
                <p:spPr>
                  <a:xfrm>
                    <a:off x="1200000" y="1400000"/>
                    <a:ext cx="3000000" cy="100000"/>
                  </a:xfrm>
                </p:spPr>
              </p:cxnSp>
            </p:spTree>
          </p:cSld>
          <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
        </p:sld>
        """;

    private const string SlideWithGroup = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
               xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
               xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
          <p:cSld>
            <p:spTree>
              <p:nvGrpSpPr>
                <p:cNvPr id="1" name=""/>
                <p:cNvGrpSpPr/>
                <p:nvPr/>
              </p:nvGrpSpPr>
              <p:grpSpPr>
                <a:xfrm>
                  <a:off x="0" y="0"/>
                  <a:ext cx="0" cy="0"/>
                  <a:chOff x="0" y="0"/>
                  <a:chExt cx="0" cy="0"/>
                </a:xfrm>
              </p:grpSpPr>
              <p:grpSp>
                <p:nvGrpSpPr>
                  <p:cNvPr id="10" name="Evidence Group"/>
                  <p:cNvGrpSpPr/>
                  <p:nvPr/>
                </p:nvGrpSpPr>
                <p:grpSpPr>
                  <a:xfrm>
                    <a:off x="2000000" y="1000000"/>
                    <a:ext cx="4000000" cy="2000000"/>
                    <a:chOff x="0" y="0"/>
                    <a:chExt cx="4000000" cy="2000000"/>
                  </a:xfrm>
                </p:grpSpPr>
                <p:sp>
                  <p:nvSpPr>
                    <p:cNvPr id="11" name="Grouped Text"/>
                    <p:cNvSpPr/>
                    <p:nvPr/>
                  </p:nvSpPr>
                  <p:spPr>
                    <a:xfrm>
                      <a:off x="250000" y="300000"/>
                      <a:ext cx="2000000" cy="500000"/>
                    </a:xfrm>
                  </p:spPr>
                  <p:txBody>
                    <a:bodyPr/>
                    <a:lstStyle/>
                    <a:p><a:r><a:t>Grouped evidence</a:t></a:r></a:p>
                  </p:txBody>
                </p:sp>
              </p:grpSp>
            </p:spTree>
          </p:cSld>
          <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
        </p:sld>
        """;

    private const string SlideWithTable = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
               xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
               xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
          <p:cSld>
            <p:spTree>
              <p:nvGrpSpPr>
                <p:cNvPr id="1" name=""/>
                <p:cNvGrpSpPr/>
                <p:nvPr/>
              </p:nvGrpSpPr>
              <p:grpSpPr>
                <a:xfrm>
                  <a:off x="0" y="0"/>
                  <a:ext cx="0" cy="0"/>
                  <a:chOff x="0" y="0"/>
                  <a:chExt cx="0" cy="0"/>
                </a:xfrm>
              </p:grpSpPr>
              <p:graphicFrame>
                <p:nvGraphicFramePr>
                  <p:cNvPr id="20" name="Plan Comparison"/>
                  <p:cNvGraphicFramePr/>
                  <p:nvPr/>
                </p:nvGraphicFramePr>
                <p:xfrm>
                  <a:off x="1000000" y="1200000"/>
                  <a:ext cx="9000000" cy="2400000"/>
                </p:xfrm>
                <a:graphic>
                  <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/table">
                    <a:tbl>
                      <a:tblPr firstRow="1" bandRow="1"/>
                      <a:tblGrid>
                        <a:gridCol w="3000000"/>
                        <a:gridCol w="3000000"/>
                        <a:gridCol w="3000000"/>
                      </a:tblGrid>
                      <a:tr h="1200000">
                        <a:tc gridSpan="2">
                          <a:txBody>
                            <a:bodyPr/><a:lstStyle/>
                            <a:p><a:r><a:rPr b="1"/><a:t>Combined Header</a:t></a:r></a:p>
                          </a:txBody>
                          <a:tcPr><a:solidFill><a:srgbClr val="D9EAF7"/></a:solidFill></a:tcPr>
                        </a:tc>
                        <a:tc hMerge="1">
                          <a:txBody><a:bodyPr/><a:lstStyle/><a:p/></a:txBody>
                          <a:tcPr/>
                        </a:tc>
                        <a:tc>
                          <a:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Price</a:t></a:r></a:p></a:txBody>
                          <a:tcPr/>
                        </a:tc>
                      </a:tr>
                      <a:tr h="1200000">
                        <a:tc>
                          <a:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Plan A</a:t></a:r></a:p></a:txBody>
                          <a:tcPr/>
                        </a:tc>
                        <a:tc>
                          <a:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>500 Mbps</a:t></a:r></a:p></a:txBody>
                          <a:tcPr/>
                        </a:tc>
                        <a:tc>
                          <a:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>$35</a:t></a:r></a:p></a:txBody>
                          <a:tcPr/>
                        </a:tc>
                      </a:tr>
                    </a:tbl>
                  </a:graphicData>
                </a:graphic>
              </p:graphicFrame>
            </p:spTree>
          </p:cSld>
          <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
        </p:sld>
        """;

    private const string SlideWithChart = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
               xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
               xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
               xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
          <p:cSld>
            <p:spTree>
              <p:nvGrpSpPr>
                <p:cNvPr id="1" name=""/>
                <p:cNvGrpSpPr/>
                <p:nvPr/>
              </p:nvGrpSpPr>
              <p:grpSpPr>
                <a:xfrm>
                  <a:off x="0" y="0"/>
                  <a:ext cx="0" cy="0"/>
                  <a:chOff x="0" y="0"/>
                  <a:chExt cx="0" cy="0"/>
                </a:xfrm>
              </p:grpSpPr>
              <p:graphicFrame>
                <p:nvGraphicFramePr>
                  <p:cNvPr id="30" name="Subscriber Growth"/>
                  <p:cNvGraphicFramePr/>
                  <p:nvPr/>
                </p:nvGraphicFramePr>
                <p:xfrm>
                  <a:off x="1200000" y="900000"/>
                  <a:ext cx="9000000" cy="4800000"/>
                </p:xfrm>
                <a:graphic>
                  <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart">
                    <c:chart r:id="rId1"/>
                  </a:graphicData>
                </a:graphic>
              </p:graphicFrame>
            </p:spTree>
          </p:cSld>
          <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
        </p:sld>
        """;

    private const string SlideWithImage = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
               xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
               xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
          <p:cSld>
            <p:spTree>
              <p:nvGrpSpPr>
                <p:cNvPr id="1" name=""/>
                <p:cNvGrpSpPr/>
                <p:nvPr/>
              </p:nvGrpSpPr>
              <p:grpSpPr>
                <a:xfrm>
                  <a:off x="0" y="0"/><a:ext cx="0" cy="0"/>
                  <a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/>
                </a:xfrm>
              </p:grpSpPr>
              <p:pic>
                <p:nvPicPr>
                  <p:cNvPr id="40" name="Market Map" descr="Source-backed market coverage map" title="Coverage map"/>
                  <p:cNvPicPr/><p:nvPr/>
                </p:nvPicPr>
                <p:blipFill>
                  <a:blip r:embed="rId1"/>
                  <a:srcRect l="10000" t="20000" r="5000" b="0"/>
                  <a:stretch><a:fillRect/></a:stretch>
                </p:blipFill>
                <p:spPr>
                  <a:xfrm rot="5400000" flipH="1" flipV="0">
                    <a:off x="1200000" y="900000"/>
                    <a:ext cx="6000000" cy="4000000"/>
                  </a:xfrm>
                  <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                </p:spPr>
              </p:pic>
            </p:spTree>
          </p:cSld>
          <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
        </p:sld>
        """;

    private static readonly byte[] ImagePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private const string BasicChart = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <c:chart>
            <c:title>
              <c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Subscriber Growth</a:t></a:r></a:p></c:rich></c:tx>
            </c:title>
            <c:plotArea>
              <c:layout/>
              <c:barChart>
                <c:barDir val="col"/>
                <c:grouping val="clustered"/>
                <c:ser>
                  <c:idx val="0"/><c:order val="0"/>
                  <c:tx><c:strRef><c:f>Data!$B$1</c:f><c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>Operator A</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:cat><c:strRef><c:f>Data!$A$2:$A$4</c:f><c:strCache><c:ptCount val="3"/><c:pt idx="0"><c:v>2024</c:v></c:pt><c:pt idx="1"><c:v>2025</c:v></c:pt><c:pt idx="2"><c:v>2026</c:v></c:pt></c:strCache></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Data!$B$2:$B$4</c:f><c:numCache><c:formatCode>#,##0</c:formatCode><c:ptCount val="3"/><c:pt idx="0"><c:v>100</c:v></c:pt><c:pt idx="1"><c:v>125.5</c:v></c:pt><c:pt idx="2"><c:v>150</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:ser>
                  <c:idx val="1"/><c:order val="1"/>
                  <c:tx><c:v>Operator B</c:v></c:tx>
                  <c:cat><c:strLit><c:ptCount val="3"/><c:pt idx="0"><c:v>2024</c:v></c:pt><c:pt idx="1"><c:v>2025</c:v></c:pt><c:pt idx="2"><c:v>2026</c:v></c:pt></c:strLit></c:cat>
                  <c:val><c:numLit><c:formatCode>0</c:formatCode><c:ptCount val="3"/><c:pt idx="0"><c:v>80</c:v></c:pt><c:pt idx="1"><c:v>110</c:v></c:pt><c:pt idx="2"><c:v>145</c:v></c:pt></c:numLit></c:val>
                </c:ser>
                <c:dLbls><c:numFmt formatCode="#,##0" sourceLinked="1"/><c:dLblPos val="outEnd"/><c:showLegendKey val="0"/><c:showVal val="1"/><c:showCatName val="0"/><c:showSerName val="0"/><c:showPercent val="0"/><c:separator>;</c:separator></c:dLbls>
                <c:axId val="1001"/><c:axId val="1002"/>
              </c:barChart>
              <c:catAx>
                <c:axId val="1001"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:axPos val="b"/>
                <c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Year</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:crossAx val="1002"/>
              </c:catAx>
              <c:valAx>
                <c:axId val="1002"/><c:scaling><c:min val="0"/><c:max val="200"/></c:scaling><c:axPos val="l"/>
                <c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Subscribers</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:numFmt formatCode="#,##0" sourceLinked="0"/><c:majorUnit val="50"/><c:minorUnit val="10"/><c:crossAx val="1001"/>
              </c:valAx>
            </c:plotArea>
            <c:legend>
              <c:legendPos val="r"/>
              <c:legendEntry><c:idx val="1"/><c:delete val="1"/></c:legendEntry>
              <c:overlay val="0"/>
            </c:legend>
          </c:chart>
        </c:chartSpace>
        """;

    private static readonly string ChartWithExternalData = BasicChart.Replace(
        "</c:chartSpace>",
        "<c:externalData r:id=\"rId1\"><c:autoUpdate val=\"0\"/></c:externalData></c:chartSpace>",
        StringComparison.Ordinal);

    private const string UnsupportedChart = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <c:chart>
            <c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Unsupported Surface</a:t></a:r></a:p></c:rich></c:tx></c:title>
            <c:plotArea>
              <c:layout/>
              <c:surface3DChart>
                <c:wireframe val="0"/>
                <c:ser>
                  <c:idx val="0"/><c:order val="0"/>
                  <c:tx><c:v>Surface</c:v></c:tx>
                  <c:cat><c:strLit><c:ptCount val="2"/><c:pt idx="0"><c:v>A</c:v></c:pt><c:pt idx="1"><c:v>B</c:v></c:pt></c:strLit></c:cat>
                  <c:val><c:numLit><c:ptCount val="2"/><c:pt idx="0"><c:v>1</c:v></c:pt><c:pt idx="1"><c:v>2</c:v></c:pt></c:numLit></c:val>
                </c:ser>
              </c:surface3DChart>
            </c:plotArea>
          </c:chart>
        </c:chartSpace>
        """;
}
