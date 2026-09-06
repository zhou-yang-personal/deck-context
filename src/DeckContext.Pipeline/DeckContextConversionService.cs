using System.Security.Cryptography;
using System.Text;
using DeckContext.Application.Contracts;
using DeckContext.Domain.Diagnostics;
using DeckContext.Domain.Extraction;
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

public sealed record DeckContextConversionOptions(
    IImageTextProvider? ImageTextProvider = null);

public interface IDeckContextConversionService
{
    Task<ContextPackageResult> ConvertAsync(
        string sourcePath,
        string outputDirectory,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        DeckContextConversionOptions? options = null);
}

public sealed class DeckContextConversionService : IDeckContextConversionService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public Task<ContextPackageResult> ConvertAsync(
        string sourcePath,
        string outputDirectory,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        DeckContextConversionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        return Task.Run(
            () => ConvertAsync(sourcePath, outputDirectory, progress, options, cancellationToken),
            cancellationToken);
    }

    private static async Task<ContextPackageResult> ConvertAsync(
        string sourcePath,
        string outputDirectory,
        IProgress<ConversionProgress>? progress,
        DeckContextConversionOptions? options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullOutputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));

        progress?.Report(new ConversionProgress(5, "Reading", "Reading PPTX package and relationships."));
        var readResult = new OpenXmlDeckContextReader().ReadPackage(fullSourcePath, cancellationToken);
        progress?.Report(new ConversionProgress(20, "Images", options?.ImageTextProvider is null
            ? "Recording extracted images without pixel interpretation."
            : $"Analyzing unique images with {options.ImageTextProvider.ProviderId}."));
        var document = await ApplyImageInterpretationsAsync(
            readResult,
            options?.ImageTextProvider,
            progress,
            cancellationToken).ConfigureAwait(false);
        string? stagingDirectory = null;

        try
        {
            stagingDirectory = ContextPackageDirectoryPublisher.CreateStagingDirectory(fullOutputDirectory);
            progress?.Report(new ConversionProgress(35, "Serializing", "Writing Markdown and JSON context."));
            var stagedMarkdownPath = Path.Combine(stagingDirectory, "deck.context.md");
            var stagedContextJsonPath = Path.Combine(stagingDirectory, "deck.context.json");
            var stagedExtractionReportPath = Path.Combine(stagingDirectory, "extraction-report.json");
            var stagedManifestPath = Path.Combine(stagingDirectory, "manifest.json");
            WriteText(stagedMarkdownPath, new DeckContextMarkdownExporter().Serialize(document));
            WriteText(stagedContextJsonPath, new DeckContextJsonSerializer().Serialize(document));
            WriteText(stagedExtractionReportPath, new ExtractionReportSerializer().Serialize(document));

            var assets = new List<ContextPackageAsset>
            {
                CreateGeneratedAsset(ContextPackageAssetKind.ContextMarkdown, stagedMarkdownPath, stagingDirectory),
                CreateGeneratedAsset(ContextPackageAssetKind.ContextJson, stagedContextJsonPath, stagingDirectory),
                CreateGeneratedAsset(ContextPackageAssetKind.ExtractionReport, stagedExtractionReportPath, stagingDirectory),
            };
            var extractedAssets = readResult.Assets.ToDictionary(
                asset => $"{asset.Kind}:{asset.PartUri}",
                StringComparer.Ordinal);

            progress?.Report(new ConversionProgress(60, "Assets", "Exporting embedded workbook assets."));
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
                var destinationPath = Path.Combine(stagingDirectory, relativePath);
                var snapshot = GetExtractedAsset(
                    extractedAssets,
                    OpenXmlExtractedAssetKind.EmbeddedWorkbook,
                    workbook.PartUri,
                    workbook.Sha256,
                    workbook.SizeBytes);
                WriteExtractedAsset(destinationPath, snapshot);
                assets.Add(new ContextPackageAsset(
                    ContextPackageAssetKind.EmbeddedWorkbook,
                    NormalizePath(relativePath),
                    workbook.PartUri,
                    workbook.RelationshipId,
                    workbook.Sha256,
                    workbook.SizeBytes));
            }

            progress?.Report(new ConversionProgress(78, "Assets", "Exporting image media assets."));
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
                var destinationPath = Path.Combine(stagingDirectory, relativePath);
                var snapshot = GetExtractedAsset(
                    extractedAssets,
                    OpenXmlExtractedAssetKind.Image,
                    image.PartUri!,
                    image.Sha256!,
                    image.SizeBytes!.Value);
                WriteExtractedAsset(destinationPath, snapshot);
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
            WriteText(stagedManifestPath, new ContextPackageManifestSerializer().Serialize(manifest));
            cancellationToken.ThrowIfCancellationRequested();
            ContextPackageDirectoryPublisher.Publish(stagingDirectory, fullOutputDirectory);
            stagingDirectory = null;
            progress?.Report(new ConversionProgress(100, "Complete", "Context package created."));

            return new ContextPackageResult(
                document,
                fullOutputDirectory,
                Path.Combine(fullOutputDirectory, "deck.context.md"),
                Path.Combine(fullOutputDirectory, "deck.context.json"),
                Path.Combine(fullOutputDirectory, "extraction-report.json"),
                Path.Combine(fullOutputDirectory, "manifest.json"),
                assets);
        }
        finally
        {
            ContextPackageDirectoryPublisher.DeleteStagingDirectory(stagingDirectory);
        }
    }

    private static async Task<DeckContextDocument> ApplyImageInterpretationsAsync(
        OpenXmlDeckContextReadResult readResult,
        IImageTextProvider? provider,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        const string notConfiguredCode = "DCX-IMAGE-TEXT-PROVIDER-NOT-CONFIGURED";
        var document = readResult.Document;
        var imageElements = document.Slides
            .SelectMany(slide => slide.Elements)
            .Where(element => element.Image is not null)
            .ToArray();

        if (imageElements.Length == 0)
        {
            return document;
        }

        var uniqueInternalImages = imageElements
            .Where(element => element.Image is { Sha256: not null, PartUri: not null, ContentType: not null })
            .DistinctBy(element => InterpretationKey(element.Image!), StringComparer.Ordinal)
            .ToArray();
        var interpretations = new Dictionary<string, ImageContentInterpretationContext>(StringComparer.Ordinal);

        if (provider is not null)
        {
            var imageAssets = readResult.Assets
                .Where(asset => asset.Kind == OpenXmlExtractedAssetKind.Image)
                .ToDictionary(asset => asset.PartUri, StringComparer.Ordinal);

            for (var imageIndex = 0; imageIndex < uniqueInternalImages.Length; imageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var element = uniqueInternalImages[imageIndex];
                var image = element.Image!;
                var imageNumber = imageIndex + 1;
                var imageProgress = 20 + (int)Math.Floor(14d * imageNumber / uniqueInternalImages.Length);
                progress?.Report(new ConversionProgress(
                    imageProgress,
                    "Images",
                    $"Analyzing image {imageNumber} of {uniqueInternalImages.Length} with {provider.ProviderId}."));

                if (!imageAssets.TryGetValue(image.PartUri!, out var asset))
                {
                    interpretations[InterpretationKey(image)] = new ImageContentInterpretationContext(
                        ImageContentInterpretationStatus.Failed,
                        provider.ProviderId,
                        null,
                        "The extracted image bytes were unavailable for interpretation.");
                    continue;
                }

                try
                {
                    interpretations[InterpretationKey(image)] = await provider.AnalyzeAsync(
                        new ImageTextRequest(
                            image.ContentType!,
                            image.PartUri!,
                            asset.Content,
                            element.Source,
                            image.Crop),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    interpretations[InterpretationKey(image)] = new ImageContentInterpretationContext(
                        ImageContentInterpretationStatus.Failed,
                        provider.ProviderId,
                        null,
                        $"The configured image provider failed: {exception.Message}");
                }
            }
        }

        var updatedSlides = document.Slides
            .Select(slide => UpdateSlide(slide, provider, interpretations, notConfiguredCode))
            .ToArray();
        var deckDiagnostics = document.Diagnostics
            .Where(diagnostic => diagnostic.Code != notConfiguredCode)
            .ToList();

        if (provider is null)
        {
            deckDiagnostics.Add(new ExtractionDiagnostic(
                notConfiguredCode,
                $"{imageElements.Length} image placement(s) referencing {uniqueInternalImages.Length} unique OCR input(s) " +
                "were extracted without OCR/Vision pixel interpretation.",
                DiagnosticSeverity.Information,
                "ImageInterpretationPipeline",
                DiagnosticOutcome.None,
                new SourceReference(document.Deck.SourceFileName, document.Deck.PresentationPartUri)));
        }

        var deckStatus = document.Status == ExtractionStatus.Partial ||
                         updatedSlides.Any(slide => slide.Status is ExtractionStatus.Partial or ExtractionStatus.Failed)
            ? ExtractionStatus.Partial
            : document.Status == ExtractionStatus.Failed
                ? ExtractionStatus.Failed
                : ExtractionStatus.Succeeded;

        return document with
        {
            Slides = updatedSlides,
            Status = deckStatus,
            Diagnostics = deckDiagnostics
        };
    }

    private static SlideContext UpdateSlide(
        SlideContext slide,
        IImageTextProvider? provider,
        IReadOnlyDictionary<string, ImageContentInterpretationContext> interpretations,
        string notConfiguredCode)
    {
        var elements = slide.Elements
            .Select(element => UpdateImageElement(element, provider, interpretations, notConfiguredCode))
            .ToArray();
        var status = slide.Status == ExtractionStatus.Partial || elements.Any(element =>
            element.Status is ExtractionStatus.Partial or ExtractionStatus.Failed or ExtractionStatus.Unsupported)
            ? ExtractionStatus.Partial
            : slide.Status == ExtractionStatus.Failed
                ? ExtractionStatus.Failed
                : ExtractionStatus.Succeeded;

        return slide with { Elements = elements, Status = status };
    }

    private static SlideElementContext UpdateImageElement(
        SlideElementContext element,
        IImageTextProvider? provider,
        IReadOnlyDictionary<string, ImageContentInterpretationContext> interpretations,
        string notConfiguredCode)
    {
        if (element.Image is null)
        {
            return element;
        }

        var diagnostics = element.Diagnostics
            .Where(diagnostic => diagnostic.Code != notConfiguredCode)
            .ToList();
        var image = element.Image;

        if (provider is null)
        {
            return element with { Diagnostics = diagnostics };
        }

        var interpretation = image.Sha256 is not null &&
                             interpretations.TryGetValue(InterpretationKey(image), out var resolvedInterpretation)
            ? resolvedInterpretation
            : new ImageContentInterpretationContext(
                ImageContentInterpretationStatus.Failed,
                provider.ProviderId,
                null,
                image.ExternalUri is not null
                    ? "External linked image bytes were not available for interpretation."
                    : "The image did not have extracted bytes or a stable hash for interpretation.");

        var status = element.Status;
        if (interpretation.Status != ImageContentInterpretationStatus.Succeeded)
        {
            diagnostics.Add(new ExtractionDiagnostic(
                "DCX-IMAGE-TEXT-PROVIDER-FAILED",
                interpretation.Description ?? "The configured OCR/Vision provider failed to interpret the image.",
                DiagnosticSeverity.Warning,
                provider.ProviderId,
                DiagnosticOutcome.Partial,
                element.Source));

            if (status == ExtractionStatus.Succeeded)
            {
                status = ExtractionStatus.Partial;
            }
        }

        return element with
        {
            Image = image with { Interpretation = interpretation },
            Status = status,
            Diagnostics = diagnostics
        };
    }

    private static string InterpretationKey(ImageContext image)
    {
        if (image.Crop is null)
        {
            return image.Sha256 ?? string.Empty;
        }

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{image.Sha256}:{image.Crop.LeftRaw}:{image.Crop.TopRaw}:{image.Crop.RightRaw}:{image.Crop.BottomRaw}");
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

    private static OpenXmlExtractedAsset GetExtractedAsset(
        IReadOnlyDictionary<string, OpenXmlExtractedAsset> assets,
        OpenXmlExtractedAssetKind kind,
        string partUri,
        string expectedSha256,
        long expectedSizeBytes)
    {
        if (!assets.TryGetValue($"{kind}:{partUri}", out var asset) ||
            !string.Equals(asset.Sha256, expectedSha256, StringComparison.Ordinal) ||
            asset.SizeBytes != expectedSizeBytes)
        {
            throw new InvalidDataException(
                $"Extracted asset snapshot '{partUri}' is missing or does not match its IR provenance.");
        }

        return asset;
    }

    private static void WriteExtractedAsset(string path, OpenXmlExtractedAsset asset)
    {
        var directory = Path.GetDirectoryName(path);

        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(asset.Content.Span);
    }

    private static string Hash(byte[] bytes)
    {
        return System.Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
