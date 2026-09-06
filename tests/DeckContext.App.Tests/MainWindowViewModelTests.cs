using DeckContext.Application.Contracts;
using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;
using DeckContext.Pipeline;

namespace DeckContext.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void SetInputPath_selects_a_default_output_and_enables_conversion()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.CreatePowerPointPlaceholder("sample.pptx");
        var viewModel = new MainWindowViewModel(new FakeConversionService());

        viewModel.SetInputPath(sourcePath);

        Assert.True(viewModel.CanConvert);
        Assert.Equal(
            Path.Combine(workspace.Path, "sample.deck-context"),
            viewModel.OutputDirectory);
        Assert.Contains("Ready", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SetInputPaths_selects_a_batch_output_root_and_updates_batch_labels()
    {
        using var workspace = new TemporaryWorkspace();
        var first = workspace.CreatePowerPointPlaceholder("first.pptx");
        var second = workspace.CreatePowerPointPlaceholder("second.pptx");
        var viewModel = new MainWindowViewModel(new FakeConversionService());

        viewModel.SetInputPaths([first, second]);

        Assert.True(viewModel.CanConvert);
        Assert.True(viewModel.IsBatch);
        Assert.Equal(2, viewModel.InputPaths.Count);
        Assert.Equal("2 PowerPoint files selected", viewModel.InputSummary);
        Assert.Equal("OUTPUT ROOT FOLDER", viewModel.OutputFolderLabel);
        Assert.Equal("Extract 2 presentations", viewModel.ConvertButtonText);
        Assert.Equal(
            Path.Combine(workspace.Path, "DeckContext-batch-output"),
            viewModel.OutputDirectory);
        Assert.Contains(first, viewModel.SelectedFilesTooltip, StringComparison.Ordinal);
        Assert.Contains(second, viewModel.SelectedFilesTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void SetInputDirectory_selects_only_top_level_powerpoints_in_stable_order()
    {
        using var workspace = new TemporaryWorkspace();
        var sourceFolder = Directory.CreateDirectory(Path.Combine(workspace.Path, "decks")).FullName;
        var second = workspace.CreatePowerPointPlaceholder(Path.Combine("decks", "zeta.PPTX"));
        var first = workspace.CreatePowerPointPlaceholder(Path.Combine("decks", "Alpha.pptx"));
        workspace.CreatePowerPointPlaceholder(Path.Combine("decks", "notes.txt"));
        workspace.CreatePowerPointPlaceholder(Path.Combine("decks", "nested", "ignored.pptx"));
        var viewModel = new MainWindowViewModel(new FakeConversionService());

        viewModel.SetInputDirectory(sourceFolder);

        Assert.Equal(new[] { first, second }, viewModel.InputPaths);
        Assert.True(viewModel.IsBatch);
        Assert.True(viewModel.CanConvert);
        Assert.Equal(
            Path.Combine(sourceFolder, "DeckContext-batch-output"),
            viewModel.OutputDirectory);
        Assert.Contains("2 presentations", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SetInputDirectory_reports_when_no_top_level_powerpoints_exist()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreatePowerPointPlaceholder("notes.txt");
        workspace.CreatePowerPointPlaceholder(Path.Combine("nested", "ignored.pptx"));
        var viewModel = new MainWindowViewModel(new FakeConversionService());

        viewModel.SetInputDirectory(workspace.Path);

        Assert.Empty(viewModel.InputPaths);
        Assert.False(viewModel.CanConvert);
        Assert.Empty(viewModel.OutputDirectory);
        Assert.Equal("No .pptx files were found in the selected folder.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ConvertAsync_processes_a_batch_in_order_and_uses_unique_per_deck_directories()
    {
        using var workspace = new TemporaryWorkspace();
        var first = workspace.CreatePowerPointPlaceholder(Path.Combine("one", "report.pptx"));
        var second = workspace.CreatePowerPointPlaceholder(Path.Combine("two", "report.pptx"));
        var outputRoot = Path.Combine(workspace.Path, "batch-output");
        var service = new FakeConversionService();
        var viewModel = new MainWindowViewModel(service) { LocalOcrEnabled = false };
        viewModel.SetInputPaths([first, second]);
        viewModel.SetOutputDirectory(outputRoot);

        await viewModel.ConvertAsync(TestContext.Current.CancellationToken);

        Assert.Collection(
            service.Calls,
            call =>
            {
                Assert.Equal(first, call.SourcePath);
                Assert.Equal(Path.Combine(outputRoot, "report.deck-context"), call.OutputDirectory);
            },
            call =>
            {
                Assert.Equal(second, call.SourcePath);
                Assert.Equal(Path.Combine(outputRoot, "report-2.deck-context"), call.OutputDirectory);
            });
        Assert.True(viewModel.HasCompleted);
        Assert.True(viewModel.CanOpenOutput);
        Assert.Equal(100, viewModel.ProgressPercentage);
        Assert.Equal("Batch extraction completed: 2 succeeded, 0 partial, 0 failed.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ConvertAsync_continues_the_batch_after_one_file_fails()
    {
        using var workspace = new TemporaryWorkspace();
        var first = workspace.CreatePowerPointPlaceholder("broken.pptx");
        var second = workspace.CreatePowerPointPlaceholder("healthy.pptx");
        var service = new FailFirstConversionService();
        var viewModel = new MainWindowViewModel(service) { LocalOcrEnabled = false };
        viewModel.SetInputPaths([first, second]);

        await viewModel.ConvertAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, service.AttemptedSourcePaths.Count);
        Assert.True(viewModel.HasCompleted);
        Assert.Equal("Batch extraction completed: 1 succeeded, 0 partial, 1 failed.", viewModel.StatusMessage);
        var diagnostic = Assert.Single(viewModel.Diagnostics);
        Assert.Equal("APP-BATCH-CONVERT", diagnostic.Code);
        Assert.Equal("broken.pptx", diagnostic.Location);
    }

    [Fact]
    public async Task ConvertAsync_uses_and_releases_a_separate_OCR_provider_for_each_deck()
    {
        using var workspace = new TemporaryWorkspace();
        var first = workspace.CreatePowerPointPlaceholder("first.pptx");
        var second = workspace.CreatePowerPointPlaceholder("second.pptx");
        var created = 0;
        var disposed = 0;
        var viewModel = new MainWindowViewModel(
            new FakeConversionService(),
            () =>
            {
                created++;
                return new TrackingImageTextProvider(() => disposed++);
            });
        viewModel.SetInputPaths([first, second]);

        await viewModel.ConvertAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, created);
        Assert.Equal(2, disposed);
        Assert.Equal("Batch extraction completed: 2 succeeded, 0 partial, 0 failed.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ConvertAsync_continues_after_one_decks_OCR_provider_cannot_start()
    {
        using var workspace = new TemporaryWorkspace();
        var first = workspace.CreatePowerPointPlaceholder("first.pptx");
        var second = workspace.CreatePowerPointPlaceholder("second.pptx");
        var factoryCalls = 0;
        var service = new FakeConversionService();
        var viewModel = new MainWindowViewModel(
            service,
            () => ++factoryCalls == 1
                ? throw new InvalidOperationException("OCR startup failed")
                : new TrackingImageTextProvider(() => { }));
        viewModel.SetInputPaths([first, second]);

        await viewModel.ConvertAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, factoryCalls);
        var call = Assert.Single(service.Calls);
        Assert.Equal(second, call.SourcePath);
        Assert.Equal("Batch extraction completed: 1 succeeded, 0 partial, 1 failed.", viewModel.StatusMessage);
        var diagnostic = Assert.Single(viewModel.Diagnostics);
        Assert.Equal("APP-BATCH-CONVERT", diagnostic.Code);
        Assert.Contains("OCR startup failed", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertAsync_keeps_the_application_usable_when_OCR_cleanup_fails()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.CreatePowerPointPlaceholder("sample.pptx");
        var viewModel = new MainWindowViewModel(
            new FakeConversionService(),
            () => new TrackingImageTextProvider(() => throw new InvalidOperationException("cleanup failed")));
        viewModel.SetInputPath(sourcePath);

        await viewModel.ConvertAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.HasCompleted);
        Assert.True(viewModel.CanConvert);
        var diagnostic = Assert.Single(viewModel.Diagnostics);
        Assert.Equal("APP-OCR-CLEANUP", diagnostic.Code);
        Assert.Contains("cleanup failed", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportUnexpectedConversionFailure_restores_the_interactive_state()
    {
        var viewModel = new MainWindowViewModel(new FakeConversionService());

        viewModel.ReportUnexpectedConversionFailure(new InvalidOperationException("unexpected failure"));

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanChangePaths);
        var diagnostic = Assert.Single(viewModel.Diagnostics);
        Assert.Equal("APP-CONVERT-UNHANDLED", diagnostic.Code);
        Assert.Contains("unexpected failure", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertAsync_exposes_progress_completion_and_diagnostics()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.CreatePowerPointPlaceholder("sample.pptx");
        var diagnostic = new ExtractionDiagnostic(
            "DCX-TEST-WARNING",
            "A recoverable condition was recorded.",
            DiagnosticSeverity.Warning,
            "Test",
            DiagnosticOutcome.Recovered);
        var viewModel = new MainWindowViewModel(new FakeConversionService(diagnostic));
        viewModel.SetInputPath(sourcePath);

        await viewModel.ConvertAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.HasCompleted);
        Assert.True(viewModel.CanOpenOutput);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(100, viewModel.ProgressPercentage);
        Assert.Contains("recoverable", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        var displayed = Assert.Single(viewModel.Diagnostics);
        Assert.Equal("DCX-TEST-WARNING", displayed.Code);
        Assert.Equal("Warning", displayed.Severity);
    }

    [Fact]
    public void SetInputPath_rejects_non_powerpoint_inputs()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.CreatePowerPointPlaceholder("notes.txt");
        var viewModel = new MainWindowViewModel(new FakeConversionService());

        viewModel.SetInputPath(sourcePath);

        Assert.False(viewModel.CanConvert);
        Assert.Contains(".pptx", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Local_OCR_is_enabled_by_default_without_credentials_and_can_be_disabled()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.CreatePowerPointPlaceholder("sample.pptx");
        var service = new FakeConversionService();
        var viewModel = new MainWindowViewModel(service);
        viewModel.SetInputPath(sourcePath);

        Assert.True(viewModel.LocalOcrEnabled);
        Assert.True(viewModel.CanConvert);
        await viewModel.ConvertAsync(TestContext.Current.CancellationToken);
        Assert.IsType<TesseractImageTextProvider>(service.LastOptions?.ImageTextProvider);

        viewModel.LocalOcrEnabled = false;
        Assert.True(viewModel.CanConvert);
        Assert.Contains("Only native PPTX", viewModel.StatusMessage, StringComparison.Ordinal);
        await viewModel.ConvertAsync(TestContext.Current.CancellationToken);
        Assert.Null(service.LastOptions?.ImageTextProvider);
    }

    [Fact]
    public async Task ConvertAsync_keeps_the_active_job_paths_immutable_while_busy()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.CreatePowerPointPlaceholder("sample.pptx");
        var otherSourcePath = workspace.CreatePowerPointPlaceholder("other.pptx");
        var service = new BlockingConversionService();
        var viewModel = new MainWindowViewModel(service);
        viewModel.SetInputPath(sourcePath);
        var originalOutput = viewModel.OutputDirectory;

        var conversion = viewModel.ConvertAsync(TestContext.Current.CancellationToken);
        await service.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanChangePaths);
        viewModel.SetInputPath(otherSourcePath);
        viewModel.SetOutputDirectory(Path.Combine(workspace.Path, "other-output"));
        Assert.Equal(sourcePath, viewModel.InputPath);
        Assert.Equal(originalOutput, viewModel.OutputDirectory);

        service.Release.SetResult();
        await conversion;
        Assert.True(viewModel.HasCompleted);
        Assert.Equal(originalOutput, viewModel.OutputDirectory);
    }

    private sealed class FakeConversionService(params ExtractionDiagnostic[] diagnostics)
        : IDeckContextConversionService
    {
        public DeckContextConversionOptions? LastOptions { get; private set; }

        public List<(string SourcePath, string OutputDirectory)> Calls { get; } = [];

        public Task<ContextPackageResult> ConvertAsync(
            string sourcePath,
            string outputDirectory,
            IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default,
            DeckContextConversionOptions? options = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastOptions = options;
            Calls.Add((sourcePath, outputDirectory));
            Directory.CreateDirectory(outputDirectory);
            progress?.Report(new ConversionProgress(50, "Test", "Testing conversion."));

            var status = diagnostics.Length == 0 ? ExtractionStatus.Succeeded : ExtractionStatus.Partial;
            var document = new DeckContextDocument(
                DeckContextDocument.CurrentSchemaVersion,
                new DeckMetadata(Path.GetFileName(sourcePath), null, null, null, 0),
                [],
                status,
                diagnostics);

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

    private sealed class FailFirstConversionService : IDeckContextConversionService
    {
        private readonly FakeConversionService successfulConversion = new();

        public List<string> AttemptedSourcePaths { get; } = [];

        public Task<ContextPackageResult> ConvertAsync(
            string sourcePath,
            string outputDirectory,
            IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default,
            DeckContextConversionOptions? options = null)
        {
            AttemptedSourcePaths.Add(sourcePath);
            if (AttemptedSourcePaths.Count == 1)
            {
                throw new InvalidOperationException("The first deck is intentionally invalid.");
            }

            return successfulConversion.ConvertAsync(
                sourcePath,
                outputDirectory,
                progress,
                cancellationToken,
                options);
        }
    }

    private sealed class BlockingConversionService : IDeckContextConversionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ContextPackageResult> ConvertAsync(
            string sourcePath,
            string outputDirectory,
            IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default,
            DeckContextConversionOptions? options = null)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            Directory.CreateDirectory(outputDirectory);
            var document = new DeckContextDocument(
                DeckContextDocument.CurrentSchemaVersion,
                new DeckMetadata(Path.GetFileName(sourcePath), null, null, null, 0),
                [],
                ExtractionStatus.Succeeded,
                []);
            return new ContextPackageResult(
                document,
                outputDirectory,
                Path.Combine(outputDirectory, "deck.context.md"),
                Path.Combine(outputDirectory, "deck.context.json"),
                Path.Combine(outputDirectory, "extraction-report.json"),
                Path.Combine(outputDirectory, "manifest.json"),
                []);
        }
    }

    private sealed class TrackingImageTextProvider(Action onDispose) : IImageTextProvider, IDisposable
    {
        public string ProviderId => "test-tracking-provider";

        public Task<ImageContentInterpretationContext> AnalyzeAsync(
            ImageTextRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImageContentInterpretationContext(
                ImageContentInterpretationStatus.Succeeded,
                ProviderId,
                null,
                "Test provider."));

        public void Dispose() => onDispose();
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"deck-context-app-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreatePowerPointPlaceholder(string fileName)
        {
            var path = System.IO.Path.Combine(Path, fileName);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "test");
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
