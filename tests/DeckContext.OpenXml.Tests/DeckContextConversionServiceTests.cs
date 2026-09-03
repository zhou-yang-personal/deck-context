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
        Assert.Empty(result.Assets.Where(asset => asset.Kind == ContextPackageAssetKind.EmbeddedWorkbook));

        var markdown = await File.ReadAllTextAsync(result.MarkdownPath, TestContext.Current.CancellationToken);
        Assert.Contains("status `Partial`", markdown, StringComparison.Ordinal);
        var report = await File.ReadAllTextAsync(result.ExtractionReportPath, TestContext.Current.CancellationToken);
        Assert.Contains("DCX-WORKBOOK-READ-FAILED", report, StringComparison.Ordinal);
    }
}
