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

        var document = reader.Read(path);

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

        var document = reader.Read(path);

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

        var document = reader.Read(path);

        Assert.Equal(ExtractionStatus.Partial, document.Status);
        var slide = Assert.Single(document.Slides);
        Assert.Equal(ExtractionStatus.Failed, slide.Status);
        Assert.Empty(slide.Elements);
        var diagnostic = Assert.Single(slide.Diagnostics);
        Assert.Equal("DCX-SLIDE-RELATIONSHIP-FAILED", diagnostic.Code);
        Assert.Equal(1, diagnostic.Source?.SlideIndex);
        Assert.Equal("rId1", diagnostic.Source?.RelationshipId);
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
