using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeckContext.Application.Contracts;
using DeckContext.Domain.Model;

namespace DeckContext.Pipeline;

public sealed class OpenAiImageTextProvider : IImageTextProvider, IDisposable
{
    private static readonly Uri ResponsesEndpoint = new("https://api.openai.com/v1/responses");
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly string apiKey;
    private readonly string model;

    public OpenAiImageTextProvider(string apiKey, string model, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        this.apiKey = apiKey;
        this.model = model;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        ownsHttpClient = httpClient is null;
    }

    public string ProviderId => $"openai-responses:{model}";

    public async Task<ImageContentInterpretationContext> AnalyzeAsync(
        ImageTextRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            message.Content = new StringContent(
                JsonSerializer.Serialize(CreateRequest(request)),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Failed($"OpenAI returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            var outputText = ReadOutputText(responseBody);
            if (string.IsNullOrWhiteSpace(outputText))
            {
                return Failed("OpenAI returned no image interpretation text.");
            }

            using var interpretationJson = JsonDocument.Parse(outputText);
            var root = interpretationJson.RootElement;
            var text = ReadOptionalString(root, "text");
            var description = ReadOptionalString(root, "description");

            if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(description))
            {
                return Failed("OpenAI returned an empty image interpretation.");
            }

            return new ImageContentInterpretationContext(
                ImageContentInterpretationStatus.Succeeded,
                ProviderId,
                text,
                description);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("OpenAI image interpretation timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return Failed($"OpenAI image interpretation failed: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private object CreateRequest(ImageTextRequest request)
    {
        var dataUrl = $"data:{request.ContentType};base64,{Convert.ToBase64String(request.Content.Span)}";
        return new
        {
            model,
            store = false,
            input = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = "Analyze this image as source material from a presentation. " +
                                   "Transcribe all readable text faithfully in reading order. " +
                                   "Describe the visual structure, relationships, chart or map meaning, and important evidence. " +
                                   "Do not invent hidden values or facts; state uncertainty in the description."
                        },
                        new { type = "input_image", image_url = dataUrl, detail = "high" }
                    }
                }
            },
            max_output_tokens = 1500,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "deck_context_image_interpretation",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            text = new { type = new[] { "string", "null" } },
                            description = new { type = new[] { "string", "null" } }
                        },
                        required = new[] { "text", "description" },
                        additionalProperties = false
                    }
                }
            }
        };
    }

    private static string? ReadOutputText(string responseBody)
    {
        using var responseJson = JsonDocument.Parse(responseBody);
        var root = responseJson.RootElement;

        if (root.TryGetProperty("output_text", out var directText) && directText.ValueKind == JsonValueKind.String)
        {
            return directText.GetString();
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("type", out var type) &&
                    type.GetString() == "output_text" &&
                    contentItem.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private ImageContentInterpretationContext Failed(string message) =>
        new(ImageContentInterpretationStatus.Failed, ProviderId, null, message);
}
