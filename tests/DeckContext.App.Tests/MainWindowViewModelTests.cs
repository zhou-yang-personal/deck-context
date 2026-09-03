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

    private sealed class FakeConversionService(params ExtractionDiagnostic[] diagnostics)
        : IDeckContextConversionService
    {
        public Task<ContextPackageResult> ConvertAsync(
            string sourcePath,
            string outputDirectory,
            IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
