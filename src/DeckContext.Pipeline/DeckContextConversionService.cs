using System.Security.Cryptography;
using System.Text;
using DeckContext.Domain.Model;
using DeckContext.Export;
using DeckContext.OpenXml;

namespace DeckContext.Pipeline;

public sealed record ConversionProgress(
    int Percentage,
    string Stage,
    string Message);

public sealed record ContextPackageResult(
    DeckContextDocument Document,
    string OutputDirectory,
    string MarkdownPath,
    string ContextJsonPath,
    string ExtractionReportPath,
    string ManifestPath,
    IReadOnlyList<ContextPackageAsset> Assets);

public interface IDeckContextConversionService
{
    Task<ContextPackageResult> ConvertAsync(
        string sourcePath,
        string outputDirectory,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class DeckContextConversionService : IDeckContextConversionService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public Task<ContextPackageResult> ConvertAsync(
        string sourcePath,
        string outputDirectory,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        return Task.Run(
            () => Convert(sourcePath, outputDirectory, progress, cancellationToken),
            cancellationToken);
    }

    private static ContextPackageResult Convert(
        string sourcePath,
        string outputDirectory,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        progress?.Report(new ConversionProgress(5, "Reading", "Reading PPTX package and relationships."));
        var document = new OpenXmlDeckContextReader().Read(fullSourcePath, cancellationToken);

        progress?.Report(new ConversionProgress(30, "Serializing", "Writing Markdown and JSON context."));
        var markdownPath = Path.Combine(fullOutputDirectory, "deck.context.md");
        var contextJsonPath = Path.Combine(fullOutputDirectory, "deck.context.json");
        var extractionReportPath = Path.Combine(fullOutputDirectory, "extraction-report.json");
        var manifestPath = Path.Combine(fullOutputDirectory, "manifest.json");
        WriteText(markdownPath, new DeckContextMarkdownExporter().Serialize(document));
        WriteText(contextJsonPath, new DeckContextJsonSerializer().Serialize(document));
        WriteText(extractionReportPath, new ExtractionReportSerializer().Serialize(document));

        var assets = new List<ContextPackageAsset>
        {
            CreateGeneratedAsset(ContextPackageAssetKind.ContextMarkdown, markdownPath, fullOutputDirectory),
            CreateGeneratedAsset(ContextPackageAssetKind.ContextJson, contextJsonPath, fullOutputDirectory),
            CreateGeneratedAsset(ContextPackageAssetKind.ExtractionReport, extractionReportPath, fullOutputDirectory),
        };

        progress?.Report(new ConversionProgress(60, "Assets", "Exporting embedded workbook assets."));
        var workbookExporter = new OpenXmlEmbeddedWorkbookAssetExporter();
        var workbooks = document.Slides
            .SelectMany(slide => slide.Elements)
            .Select(element => element.Chart?.EmbeddedWorkbook)
            .Where(workbook => workbook is not null)
            .Cast<EmbeddedWorkbookContext>()
            .DistinctBy(workbook => workbook.PartUri, StringComparer.Ordinal)
            .OrderBy(workbook => workbook.PartUri, StringComparer.Ordinal)
            .ToArray();

        foreach (var workbook in workbooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.Combine("workbooks", SafeFileName(Path.GetFileName(workbook.PartUri)));
            var destinationPath = Path.Combine(fullOutputDirectory, relativePath);
            workbookExporter.Export(fullSourcePath, workbook, destinationPath, cancellationToken);
            assets.Add(new ContextPackageAsset(
                ContextPackageAssetKind.EmbeddedWorkbook,
                NormalizePath(relativePath),
                workbook.PartUri,
                workbook.RelationshipId,
                workbook.Sha256,
                workbook.SizeBytes));
        }

        progress?.Report(new ConversionProgress(78, "Assets", "Exporting image media assets."));
        var imageExporter = new OpenXmlImageAssetExporter();
        var images = document.Slides
            .SelectMany(slide => slide.Elements)
            .Select(element => element.Image)
            .Where(image => image is { PartUri: not null, Sha256: not null, SuggestedFileName: not null })
            .Cast<ImageContext>()
            .DistinctBy(image => image.PartUri, StringComparer.Ordinal)
            .OrderBy(image => image.PartUri, StringComparer.Ordinal)
            .ToArray();

        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.Combine("images", SafeFileName(image.SuggestedFileName!));
            var destinationPath = Path.Combine(fullOutputDirectory, relativePath);
            imageExporter.Export(fullSourcePath, image, destinationPath, cancellationToken);
            assets.Add(new ContextPackageAsset(
                ContextPackageAssetKind.Image,
                NormalizePath(relativePath),
                image.PartUri,
                image.RelationshipId,
                image.Sha256!,
                image.SizeBytes!.Value));
        }

        progress?.Report(new ConversionProgress(92, "Manifest", "Writing the traceable asset manifest."));
        var manifest = new ContextPackageManifest(
            document.SchemaVersion,
            document.Deck.SourceFileName,
            assets);
        WriteText(manifestPath, new ContextPackageManifestSerializer().Serialize(manifest));
        progress?.Report(new ConversionProgress(100, "Complete", "Context package created."));

        return new ContextPackageResult(
            document,
            fullOutputDirectory,
            markdownPath,
            contextJsonPath,
            extractionReportPath,
            manifestPath,
            assets);
    }

    private static ContextPackageAsset CreateGeneratedAsset(
        ContextPackageAssetKind kind,
        string path,
        string outputDirectory)
    {
        var bytes = File.ReadAllBytes(path);
        return new ContextPackageAsset(
            kind,
            NormalizePath(Path.GetRelativePath(outputDirectory, path)),
            null,
            null,
            Hash(bytes),
            bytes.LongLength);
    }

    private static void WriteText(string path, string content)
    {
        File.WriteAllText(path, content, Utf8WithoutBom);
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string SafeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var safeName = new string(fileName
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(safeName) ? "asset.bin" : safeName;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
