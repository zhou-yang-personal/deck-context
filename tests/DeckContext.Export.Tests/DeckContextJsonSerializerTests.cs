using System.Text.Json;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;

namespace DeckContext.Export.Tests;

public sealed class DeckContextJsonSerializerTests
{
    [Fact]
    public void Serialize_is_deterministic_and_uses_the_stable_context_contract()
    {
        var document = new DeckContextDocument(
            DeckContextDocument.CurrentSchemaVersion,
            new DeckMetadata("sample.pptx", "/ppt/presentation.xml", 100, 50, 1),
            [
                new SlideContext(
                    new SlideMetadata(1, "256", "rId1", "/ppt/slides/slide1.xml", 100, 50),
                    [],
                    ExtractionStatus.Succeeded,
                    []),
            ],
            ExtractionStatus.Succeeded,
            []);
        var serializer = new DeckContextJsonSerializer();

        var first = serializer.Serialize(document);
        var second = serializer.Serialize(document);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", first, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", first, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(first);
        var root = json.RootElement;
        Assert.Equal("0.1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("sample.pptx", root.GetProperty("deck").GetProperty("sourceFileName").GetString());
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        Assert.Equal(1, root.GetProperty("slides").GetArrayLength());
    }
}
