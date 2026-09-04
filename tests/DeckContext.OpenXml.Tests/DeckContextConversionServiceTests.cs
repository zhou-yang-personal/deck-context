using System.Text.Json;
using System.Security.Cryptography;
using DeckContext.Domain.Extraction;
using DeckContext.Export;
using DeckContext.Pipeline;

namespace DeckContext.OpenXml.Tests;

public sealed class DeckContextConversionServiceTests
{
    [Fact]
    public async Task Convert_creates_deterministic_context_report_manifest_and_workbook_asset()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = PresentationFixture.CreateEmbeddedWorkbookChart(directory.Path);
        var firstOutput = Path.Combine(directory.Path, "first");
        var secondOutput = Path.Combine(directory.Path, "second");
        var service = new DeckContextConversionService();

        var first = await service.ConvertAsync(
            sourcePath,
            firstOutput,
            cancellationToken: TestContext.Current.CancellationToken);
        var second = await service.ConvertAsync(
            sourcePath,
            secondOutput,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Succeeded, first.Document.Status);
        Assert.Equal(
            await File.ReadAllTextAsync(first.MarkdownPath, TestContext.Current.CancellationToken),
            await File.ReadAllTextAsync(second.MarkdownPath, TestContext.Current.CancellationToken));
        Assert.Equal(
            await File.ReadAllTextAsync(first.ContextJsonPath, TestContext.Current.CancellationToken),
            await File.ReadAllTextAsync(second.ContextJsonPath, TestContext.Current.CancellationToken));
        Assert.Equal(
            await File.ReadAllTextAsync(first.ExtractionReportPath, TestContext.Current.CancellationToken),
            await File.ReadAllTextAsync(second.ExtractionReportPath, TestContext.Current.CancellationToken));
        Assert.Equal(
            await File.ReadAllTextAsync(first.ManifestPath, TestContext.Current.CancellationToken),
            await File.ReadAllTextAsync(second.ManifestPath, TestContext.Current.CancellationToken));

        var workbookAsset = Assert.Single(
            first.Assets,
            asset => asset.Kind == ContextPackageAssetKind.EmbeddedWorkbook);
        Assert.True(File.Exists(Path.Combine(first.OutputDirectory, workbookAsset.RelativePath)));
        Assert.Equal(64, workbookAsset.Sha256.Length);
        Assert.Contains("Embedded workbook", await File.ReadAllTextAsync(
            first.MarkdownPath,
            TestContext.Current.CancellationToken), StringComparison.Ordinal);

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            first.ManifestPath,
            TestContext.Current.CancellationToken));
        foreach (var asset in manifest.RootElement.GetProperty("assets").EnumerateArray())
        {
            var relativePath = asset.GetProperty("relativePath").GetString()!;
            var bytes = await File.ReadAllBytesAsync(
                Path.Combine(first.OutputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                TestContext.Current.CancellationToken);
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Assert.Equal(asset.GetProperty("sha256").GetString(), actualHash);
            Assert.Equal(asset.GetProperty("sizeBytes").GetInt64(), bytes.LongLength);
        }
    }

    [Fact]
    public async Task Convert_exports_images_and_reports_unconfigured_pixel_interpretation()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = PresentationFixture.CreateImage(directory.Path);
        var output = Path.Combine(directory.Path, "output");

        var result = await new DeckContextConversionService().ConvertAsync(
            sourcePath,
            output,
            cancellationToken: TestContext.Current.CancellationToken);

        var imageAsset = Assert.Single(
            result.Assets,
            asset => asset.Kind == ContextPackageAssetKind.Image);
        Assert.Equal("images/image1.png", imageAsset.RelativePath);
        Assert.True(File.Exists(Path.Combine(output, "images", "image1.png")));
        var markdown = await File.ReadAllTextAsync(
            result.MarkdownPath,
            TestContext.Current.CancellationToken);
        Assert.Contains("Pixel content: not analyzed", markdown, StringComparison.Ordinal);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(
            result.ExtractionReportPath,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, report.RootElement.GetProperty("summary").GetProperty("informationCount").GetInt32());
        Assert.Equal("DCX-IMAGE-TEXT-PROVIDER-NOT-CONFIGURED", report.RootElement
            .GetProperty("entries")[0]
            .GetProperty("code")
            .GetString());
    }

    [Fact]
    public async Task Convert_preserves_partial_degradation_in_the_complete_output_package()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = PresentationFixture.CreateMalformedEmbeddedWorkbookChart(directory.Path);
        var output = Path.Combine(directory.Path, "partial-output");

        var result = await new DeckContextConversionService().ConvertAsync(
            sourcePath,
            output,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStatus.Partial, result.Document.Status);
        var chart = Assert.Single(result.Document.Slides[0].Elements, element => element.Chart is not null);
        Assert.Equal(ExtractionStatus.Partial, chart.Status);
        Assert.NotEmpty(chart.Chart!.Plots[0].Series[0].Values!.Points);
        var workbookAsset = Assert.Single(
            result.Assets,
            asset => asset.Kind == ContextPackageAssetKind.EmbeddedWorkbook);
        Assert.Equal(3, workbookAsset.SizeBytes);

        var markdown = await File.ReadAllTextAsync(result.MarkdownPath, TestContext.Current.CancellationToken);
        Assert.Contains("status `Partial`", markdown, StringComparison.Ordinal);
        var report = await File.ReadAllTextAsync(result.ExtractionReportPath, TestContext.Current.CancellationToken);
        Assert.Contains("DCX-WORKBOOK-READ-FAILED", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_replaces_an_owned_package_without_leaving_stale_assets()
    {
        using var directory = new TemporaryDirectory();
        var imageSource = PresentationFixture.CreateImage(directory.Path);
        var workbookSource = PresentationFixture.CreateEmbeddedWorkbookChart(directory.Path);
        var output = Path.Combine(directory.Path, "replace-output");
        var service = new DeckContextConversionService();

        await service.ConvertAsync(
            imageSource,
            output,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Path.Combine(output, "images", "image1.png")));

        var result = await service.ConvertAsync(
            workbookSource,
            output,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(Path.Combine(output, "images")));
        Assert.Single(result.Assets, asset => asset.Kind == ContextPackageAssetKind.EmbeddedWorkbook);
        Assert.True(File.Exists(Path.Combine(output, "workbooks", "workbook1.xlsx")));
    }

    [Fact]
    public async Task Convert_refuses_to_overwrite_a_non_package_directory()
    {
        using var directory = new TemporaryDirectory();
        var source = PresentationFixture.CreateImage(directory.Path);
        var output = Path.Combine(directory.Path, "unowned-output");
        Directory.CreateDirectory(output);
        var sentinelPath = Path.Combine(output, "keep-me.txt");
        await File.WriteAllTextAsync(
            sentinelPath,
            "user data",
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeckContextConversionService().ConvertAsync(
                source,
                output,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("not an intact DeckContext package", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            "user data",
            await File.ReadAllTextAsync(sentinelPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Convert_refuses_to_overwrite_a_directory_that_only_contains_subdirectories()
    {
        using var directory = new TemporaryDirectory();
        var source = PresentationFixture.CreateImage(directory.Path);
        var output = Path.Combine(directory.Path, "directory-only-output");
        var sentinelDirectory = Path.Combine(output, "keep-me");
        Directory.CreateDirectory(sentinelDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeckContextConversionService().ConvertAsync(
                source,
                output,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(Directory.Exists(sentinelDirectory));
    }

    [Fact]
    public async Task Convert_refuses_to_overwrite_a_modified_package()
    {
        using var directory = new TemporaryDirectory();
        var source = PresentationFixture.CreateImage(directory.Path);
        var output = Path.Combine(directory.Path, "modified-output");
        var service = new DeckContextConversionService();
        await service.ConvertAsync(
            source,
            output,
            cancellationToken: TestContext.Current.CancellationToken);
        var markdownPath = Path.Combine(output, "deck.context.md");
        var originalMarkdown = await File.ReadAllTextAsync(
            markdownPath,
            TestContext.Current.CancellationToken);
        var modifiedMarkdown = $"!{originalMarkdown[1..]}";
        await File.WriteAllTextAsync(
            markdownPath,
            modifiedMarkdown,
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConvertAsync(
                source,
                output,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("not an intact DeckContext package", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            modifiedMarkdown,
            await File.ReadAllTextAsync(markdownPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Convert_preserves_the_previous_package_when_staged_work_is_cancelled()
    {
        using var directory = new TemporaryDirectory();
        var imageSource = PresentationFixture.CreateImage(directory.Path);
        var workbookSource = PresentationFixture.CreateEmbeddedWorkbookChart(directory.Path);
        var output = Path.Combine(directory.Path, "cancel-output");
        var service = new DeckContextConversionService();
        await service.ConvertAsync(
            imageSource,
            output,
            cancellationToken: TestContext.Current.CancellationToken);
        var originalManifest = await File.ReadAllTextAsync(
            Path.Combine(output, "manifest.json"),
            TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var progress = new CallbackProgress(item =>
        {
            if (item.Percentage == 92)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ConvertAsync(workbookSource, output, progress, cancellation.Token));

        Assert.Equal(
            originalManifest,
            await File.ReadAllTextAsync(Path.Combine(output, "manifest.json"), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(output, "images", "image1.png")));
        Assert.False(Directory.Exists(Path.Combine(output, "workbooks")));
    }

    private sealed class CallbackProgress(Action<ConversionProgress> callback) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => callback(value);
    }
}
