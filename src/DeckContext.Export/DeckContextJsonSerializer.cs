using System.Text.Json;
using System.Text.Json.Serialization;
using DeckContext.Domain.Model;

namespace DeckContext.Export;

public sealed class DeckContextJsonSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public string Serialize(DeckContextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var json = JsonSerializer.Serialize(document, Options)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        return $"{json}\n";
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
