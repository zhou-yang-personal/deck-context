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

    private static string ContentTypes(bool twoSlides)
    {
        var secondSlide = twoSlides
            ? "<Override PartName=\"/ppt/slides/slide2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>"
            : string.Empty;

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
              <Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
              {secondSlide}
            </Types>
            """;
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
                <p:spPr/>
                <p:txBody>
                  <a:bodyPr/>
                  <a:lstStyle/>
                  <a:p><a:r><a:t>Slide One</a:t></a:r></a:p>
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
                <p:spPr/>
              </p:cxnSp>
            </p:spTree>
          </p:cSld>
          <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
        </p:sld>
        """;
}
