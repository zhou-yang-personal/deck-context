using System.Net;
using System.Text;
using DeckContext.Application.Contracts;
using DeckContext.Domain.Model;
using DeckContext.Pipeline;

namespace DeckContext.OpenXml.Tests;

public sealed class OpenAiImageTextProviderTests
{
    [Fact]
    public async Task Analyze_sends_an_explicit_non_stored_image_request_and_reads_structured_output()
    {
        string? requestBody = null;
        string? authorization = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            authorization = request.Headers.Authorization?.ToString();
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            const string interpretation = "{\"text\":\"图中文字\",\"description\":\"流程从左到右\"}";
            var responseJson = "{\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":" +
                               System.Text.Json.JsonSerializer.Serialize(interpretation) + "}]}]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }));
        using var provider = new OpenAiImageTextProvider("secret-key", "vision-model", httpClient);

        var result = await provider.AnalyzeAsync(
            new ImageTextRequest(
                "image/png",
                "/ppt/media/image1.png",
                new byte[] { 1, 2, 3 },
                new SourceReference("sample.pptx", SlideIndex: 1, ElementId: "4")),
            TestContext.Current.CancellationToken);

        Assert.Equal(ImageContentInterpretationStatus.Succeeded, result.Status);
        Assert.Equal("图中文字", result.Text);
        Assert.Equal("流程从左到右", result.Description);
        Assert.Equal("Bearer secret-key", authorization);
        Assert.Contains("\"store\":false", requestBody, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,AQID", requestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_returns_a_failed_interpretation_for_http_errors()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                ReasonPhrase = "Too Many Requests"
            }));
        using var provider = new OpenAiImageTextProvider("secret-key", "vision-model", httpClient);

        var result = await provider.AnalyzeAsync(
            new ImageTextRequest(
                "image/png",
                "/ppt/media/image1.png",
                new byte[] { 1 },
                new SourceReference("sample.pptx")),
            TestContext.Current.CancellationToken);

        Assert.Equal(ImageContentInterpretationStatus.Failed, result.Status);
        Assert.Contains("429", result.Description, StringComparison.Ordinal);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
