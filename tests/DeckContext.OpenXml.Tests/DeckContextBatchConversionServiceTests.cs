using System.Text.Json;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;
using DeckContext.Pipeline;

namespace DeckContext.OpenXml.Tests;

public sealed class DeckContextBatchConversionServiceTests
{
    [Fact]
    public async Task Folder_input_processes_only_top_level_powerpoints_in_stable_order()
    {
        using var directory = new TemporaryDirectory();
        var inputDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "input")).FullName;
        var outputDirectory = Path.Combine(directory.Path, "output");
        var second = CreateFile(inputDirectory, "zeta.PPTX");
        var first = CreateFile(inputDirectory, "Alpha.pptx");
        CreateFile(inputDirectory, "notes.txt");
        CreateFile(Path.Combine(inputDirectory, "nested"), "ignored.pptx");
        var conversionService = new FakeConversionService(
            sourcePath => sourcePath == first ? ExtractionStatus.Partial : ExtractionStatus.Succeeded);
        var progress = new RecordingBatchProgress();
        var service = new DeckContextBatchConversionService(conversionService);

        var result = await service.ConvertAsync(
            inputDirectory,
            outputDirectory,
            useBundledLocalOcr: true,
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal(DeckContextBatchInputKind.Directory, result.InputKind);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(1, result.PartialCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Collection(
            conversionService.Calls,
            call =>
            {
                Assert.Equal(first, call.SourcePath);
                Assert.Equal(Path.Combine(outputDirectory, "Alpha.deck-context"), call.OutputDirectory);
                Assert.True(call.Options.UseBundledLocalOcr);
            },
            call =>
            {
                Assert.Equal(second, call.SourcePath);
                Assert.Equal(Path.Combine(outputDirectory, "zeta.deck-context"), call.OutputDirectory);
                Assert.True(call.Options.UseBundledLocalOcr);
            });
        Assert.DoesNotContain(
            conversionService.Calls,
            call => call.SourcePath.EndsWith("ignored.pptx", StringComparison.Ordinal));
        Assert.Contains(progress.Items, item => item.OverallPercentage == 25);
        Assert.Contains(progress.Items, item => item.OverallPercentage == 75);
    }

    [Fact]
    public async Task Folder_input_continues_after_one_deck_fails()
    {
        using var directory = new TemporaryDirectory();
        var inputDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "input")).FullName;
        CreateFile(inputDirectory, "broken.pptx");
        CreateFile(inputDirectory, "healthy.pptx");
        var conversionService = new FakeConversionService(
            _ => ExtractionStatus.Succeeded,
            sourcePath => sourcePath.EndsWith("broken.pptx", StringComparison.Ordinal));
        var service = new DeckContextBatchConversionService(conversionService);

        var result = await service.ConvertAsync(
            inputDirectory,
            Path.Combine(directory.Path, "output"),
            useBundledLocalOcr: false,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, conversionService.Calls.Count);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(0, result.PartialCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(ExtractionStatus.Failed, result.Items[0].Status);
        var error = Assert.IsType<string>(result.Items[0].Error);
        Assert.Contains("intentional worker failure", error, StringComparison.Ordinal);
        Assert.Equal(ExtractionStatus.Succeeded, result.Items[1].Status);
        Assert.All(conversionService.Calls, call => Assert.False(call.Options.UseBundledLocalOcr));
    }

    [Fact]
    public async Task File_input_preserves_the_direct_output_directory()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = CreateFile(directory.Path, "sample.pptx");
        var outputDirectory = Path.Combine(directory.Path, "direct-output");
        var conversionService = new FakeConversionService(_ => ExtractionStatus.Succeeded);
        var service = new DeckContextBatchConversionService(conversionService);

        var result = await service.ConvertAsync(
            sourcePath,
            outputDirectory,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DeckContextBatchInputKind.File, result.InputKind);
        var call = Assert.Single(conversionService.Calls);
        Assert.Equal(outputDirectory, call.OutputDirectory);
        Assert.Equal(outputDirectory, Assert.Single(result.Items).OutputDirectory);
    }

    [Fact]
    public async Task Batch_result_json_is_deterministic_and_machine_readable()
    {
        using var directory = new TemporaryDirectory();
        var result = new DeckContextBatchResult(
            DeckContextBatchResult.CurrentSchemaVersion,
            DeckContextBatchInputKind.Directory,
            Path.Combine(directory.Path, "input"),
            Path.Combine(directory.Path, "output"),
            true,
            [
                new DeckContextBatchItemResult(
                    1,
                    "sample.pptx",
                    Path.Combine(directory.Path, "input", "sample.pptx"),
                    Path.Combine(directory.Path, "output", "sample.deck-context"),
                    ExtractionStatus.Partial,
                    "deck.context.md",
                    "deck.context.json",
                    "extraction-report.json",
                    "manifest.json",
                    null),
            ]);
        var serializer = new DeckContextBatchResultJsonSerializer();

        var first = serializer.Serialize(result);
        var second = serializer.Serialize(result);
        var outputPath = Path.Combine(directory.Path, "output", "batch-result.json");
        await serializer.WriteAsync(result, outputPath, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.Equal(first, await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken));
        using var json = JsonDocument.Parse(first);
        Assert.Equal("directory", json.RootElement.GetProperty("inputKind").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("partialCount").GetInt32());
        Assert.Equal(
            "partial",
            json.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Folder_input_rejects_an_empty_top_level()
    {
        using var directory = new TemporaryDirectory();
        CreateFile(Path.Combine(directory.Path, "nested"), "ignored.pptx");
        var service = new DeckContextBatchConversionService(
            new FakeConversionService(_ => ExtractionStatus.Succeeded));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.ConvertAsync(
            directory.Path,
            Path.Combine(directory.Path, "output"),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("top level", exception.Message, StringComparison.Ordinal);
    }

    private static string CreateFile(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "test");
        return path;
    }

    private sealed class FakeConversionService(
        Func<string, ExtractionStatus> statusSelector,
        Func<string, bool>? failureSelector = null) : IDeckContextConversionService
    {
        public List<ConversionCall> Calls { get; } = [];

        public Task<ContextPackageResult> ConvertAsync(
            string sourcePath,
            string outputDirectory,
            IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default,
            DeckContextConversionOptions? options = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var effectiveOptions = options ?? new DeckContextConversionOptions();
            Calls.Add(new ConversionCall(sourcePath, outputDirectory, effectiveOptions));
            if (failureSelector?.Invoke(sourcePath) == true)
            {
                throw new InvalidOperationException("intentional worker failure");
            }

            progress?.Report(new ConversionProgress(50, "Test", "Converting fixture."));
            var document = new DeckContextDocument(
                DeckContextDocument.CurrentSchemaVersion,
                new DeckMetadata(Path.GetFileName(sourcePath), null, null, null, 0),
                [],
                statusSelector(sourcePath),
                []);
            return Task.FromResult(new ContextPackageResult(
                document,
                outputDirectory,
                Path.Combine(outputDirectory, "deck.context.md"),
                Path.Combine(outputDirectory, "deck.context.json"),
                Path.Combine(outputDirectory, "extraction-report.json"),
                Path.Combine(outputDirectory, "manifest.json"),
                []));
        }
    }

    private sealed record ConversionCall(
        string SourcePath,
        string OutputDirectory,
        DeckContextConversionOptions Options);

    private sealed class RecordingBatchProgress : IProgress<DeckContextBatchProgress>
    {
        public List<DeckContextBatchProgress> Items { get; } = [];

        public void Report(DeckContextBatchProgress value) => Items.Add(value);
    }
}
