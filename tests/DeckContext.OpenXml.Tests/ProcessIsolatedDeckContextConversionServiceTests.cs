using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;
using DeckContext.Export;
using DeckContext.Pipeline;

namespace DeckContext.OpenXml.Tests;

public sealed class ProcessIsolatedDeckContextConversionServiceTests
{
    [Fact]
    public async Task Convert_reads_a_completed_worker_package_and_forwards_progress()
    {
        using var directory = new TemporaryDirectory();
        var workerPath = Path.Combine(directory.Path, "DeckContext.Verification.exe");
        var sourcePath = Path.Combine(directory.Path, "sample.pptx");
        var outputPath = Path.Combine(directory.Path, "output");
        File.WriteAllText(workerPath, "test worker");
        File.WriteAllText(sourcePath, "test deck");
        var runner = new SuccessfulWorkerProcessRunner();
        var service = new ProcessIsolatedDeckContextConversionService(workerPath, runner);
        var progress = new RecordingProgress();

        var result = await service.ConvertAsync(
            sourcePath,
            outputPath,
            progress,
            TestContext.Current.CancellationToken,
            new DeckContextConversionOptions(UseBundledLocalOcr: true));

        Assert.True(runner.Request!.UseLocalOcr);
        Assert.Equal(ExtractionStatus.Partial, result.Document.Status);
        Assert.Equal("sample.pptx", result.Document.Deck.SourceFileName);
        Assert.Equal(outputPath, result.OutputDirectory);
        Assert.Contains(progress.Items, item =>
            item.Percentage == 42 &&
            item.Stage == "Images" &&
            item.Message == "Analyzing image 2 of 5.");
        Assert.Equal(100, progress.Items[^1].Percentage);
    }

    [Fact]
    public async Task Convert_reports_a_worker_process_crash_as_a_regular_conversion_failure()
    {
        using var directory = new TemporaryDirectory();
        var workerPath = Path.Combine(directory.Path, "DeckContext.Verification.exe");
        var sourcePath = Path.Combine(directory.Path, "sample.pptx");
        File.WriteAllText(workerPath, "test worker");
        File.WriteAllText(sourcePath, "test deck");
        var service = new ProcessIsolatedDeckContextConversionService(
            workerPath,
            new FailedWorkerProcessRunner());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConvertAsync(
            sourcePath,
            Path.Combine(directory.Path, "output"),
            cancellationToken: TestContext.Current.CancellationToken,
            options: new DeckContextConversionOptions(UseBundledLocalOcr: true)));

        Assert.Contains("exited with code -1073741819", exception.Message, StringComparison.Ordinal);
        Assert.Contains("native OCR crash", exception.Message, StringComparison.Ordinal);
    }

    private sealed class SuccessfulWorkerProcessRunner : IDeckContextWorkerProcessRunner
    {
        public DeckContextWorkerProcessRequest? Request { get; private set; }

        public Task<DeckContextWorkerProcessResult> RunAsync(
            DeckContextWorkerProcessRequest request,
            Action<string> outputLineReceived,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            Directory.CreateDirectory(request.OutputDirectory);
            var diagnostic = new DeckContext.Domain.Diagnostics.ExtractionDiagnostic(
                "DCX-TEST",
                "Test warning.",
                DeckContext.Domain.Diagnostics.DiagnosticSeverity.Warning,
                "TestWorker",
                DeckContext.Domain.Diagnostics.DiagnosticOutcome.Partial);
            var document = new DeckContextDocument(
                DeckContextDocument.CurrentSchemaVersion,
                new DeckMetadata(Path.GetFileName(request.SourcePath), null, null, null, 0),
                [],
                ExtractionStatus.Partial,
                [diagnostic]);
            var manifest = new ContextPackageManifest(
                DeckContextDocument.CurrentSchemaVersion,
                Path.GetFileName(request.SourcePath),
                []);
            File.WriteAllText(
                Path.Combine(request.OutputDirectory, "deck.context.md"),
                "# Test");
            File.WriteAllText(
                Path.Combine(request.OutputDirectory, "deck.context.json"),
                new DeckContextJsonSerializer().Serialize(document));
            File.WriteAllText(
                Path.Combine(request.OutputDirectory, "extraction-report.json"),
                "{}");
            File.WriteAllText(
                Path.Combine(request.OutputDirectory, "manifest.json"),
                new ContextPackageManifestSerializer().Serialize(manifest));
            outputLineReceived("[ 42%] Images: Analyzing image 2 of 5.");
            return Task.FromResult(new DeckContextWorkerProcessResult(0, string.Empty));
        }
    }

    private sealed class FailedWorkerProcessRunner : IDeckContextWorkerProcessRunner
    {
        public Task<DeckContextWorkerProcessResult> RunAsync(
            DeckContextWorkerProcessRequest request,
            Action<string> outputLineReceived,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DeckContextWorkerProcessResult(
                -1073741819,
                "native OCR crash"));
    }

    private sealed class RecordingProgress : IProgress<ConversionProgress>
    {
        public List<ConversionProgress> Items { get; } = [];

        public void Report(ConversionProgress value) => Items.Add(value);
    }
}
