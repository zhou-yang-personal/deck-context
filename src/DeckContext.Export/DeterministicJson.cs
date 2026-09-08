using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckContext.Export;

internal static class DeterministicJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize<T>(T value)
    {
        return $"{JsonSerializer.Serialize(value, Options).Replace("\r\n", "\n", StringComparison.Ordinal)}\n";
    }

    public static T Deserialize<T>(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<T>(json, Options) ??
               throw new JsonException($"The JSON payload did not contain a {typeof(T).Name} value.");
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
