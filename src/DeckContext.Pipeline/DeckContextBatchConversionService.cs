using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeckContext.Domain.Extraction;

namespace DeckContext.Pipeline;

public enum DeckContextBatchInputKind
{
    File,
    Directory,
}

public sealed record DeckContextBatchProgress(
    int ItemIndex,
    int ItemCount,
    string SourceFileName,
    int OverallPercentage,
    ConversionProgress Conversion);

public sealed record DeckContextBatchItemResult(
    int Index,
    string SourceFileName,
    string SourcePath,
    string OutputDirectory,
    ExtractionStatus Status,
    string? MarkdownPath,
    string? ContextJsonPath,
    string? ExtractionReportPath,
    string? ManifestPath,
    string? Error);

public sealed record DeckContextBatchResult(
    string SchemaVersion,
    DeckContextBatchInputKind InputKind,
    string InputPath,
    string OutputRoot,
    bool LocalOcrEnabled,
    IReadOnlyList<DeckContextBatchItemResult> Items)
{
    public const string CurrentSchemaVersion = "0.1";

    public int TotalCount => Items.Count;

    public int SucceededCount => Items.Count(item => item.Status == ExtractionStatus.Succeeded);

    public int PartialCount => Items.Count(item => item.Status == ExtractionStatus.Partial);

    public int FailedCount => TotalCount - SucceededCount - PartialCount;
}

public sealed class DeckContextBatchConversionService(
    IDeckContextConversionService conversionService)
{
    public async Task<DeckContextBatchResult> ConvertAsync(
        string inputPath,
        string outputDirectory,
        bool useBundledLocalOcr = true,
        IProgress<DeckContextBatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var fullInputPath = Path.GetFullPath(inputPath);
        var fullOutputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        var (inputKind, sourcePaths) = ResolveInputs(fullInputPath);
        var outputDirectories = ResolveOutputDirectories(inputKind, sourcePaths, fullOutputDirectory);
        var items = new List<DeckContextBatchItemResult>(sourcePaths.Count);

        for (var itemIndex = 0; itemIndex < sourcePaths.Count; itemIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = sourcePaths[itemIndex];
            var sourceFileName = Path.GetFileName(sourcePath);
            var outputPath = outputDirectories[itemIndex];
            var currentIndex = itemIndex + 1;
            var itemProgress = new InlineProgress<ConversionProgress>(update =>
            {
                var overallPercentage = Math.Clamp(
                    ((currentIndex - 1) * 100 + update.Percentage) / sourcePaths.Count,
                    0,
                    100);
                progress?.Report(new DeckContextBatchProgress(
                    currentIndex,
                    sourcePaths.Count,
                    sourceFileName,
                    overallPercentage,
                    update));
            });

            try
            {
                var result = await conversionService.ConvertAsync(
                    sourcePath,
                    outputPath,
                    itemProgress,
                    cancellationToken,
                    new DeckContextConversionOptions(
                        UseBundledLocalOcr: useBundledLocalOcr)).ConfigureAwait(false);
                items.Add(new DeckContextBatchItemResult(
                    currentIndex,
                    sourceFileName,
                    sourcePath,
                    outputPath,
                    result.Document.Status,
                    result.MarkdownPath,
                    result.ContextJsonPath,
                    result.ExtractionReportPath,
                    result.ManifestPath,
                    null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                items.Add(new DeckContextBatchItemResult(
                    currentIndex,
                    sourceFileName,
                    sourcePath,
                    outputPath,
                    ExtractionStatus.Failed,
                    null,
                    null,
                    null,
                    null,
                    Limit(exception.Message, 4000)));
                progress?.Report(new DeckContextBatchProgress(
                    currentIndex,
                    sourcePaths.Count,
                    sourceFileName,
                    currentIndex * 100 / sourcePaths.Count,
                    new ConversionProgress(100, "Failed", exception.Message)));
            }
        }

        return new DeckContextBatchResult(
            DeckContextBatchResult.CurrentSchemaVersion,
            inputKind,
            fullInputPath,
            fullOutputDirectory,
            useBundledLocalOcr,
            items);
    }

    private static (DeckContextBatchInputKind Kind, IReadOnlyList<string> SourcePaths) ResolveInputs(
        string inputPath)
    {
        if (File.Exists(inputPath))
        {
            if (!string.Equals(Path.GetExtension(inputPath), ".pptx", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The input file must use the .pptx extension.", nameof(inputPath));
            }

            return (DeckContextBatchInputKind.File, [inputPath]);
        }

        if (!Directory.Exists(inputPath))
        {
            throw new ArgumentException("The input path does not exist.", nameof(inputPath));
        }

        var sourcePaths = Directory
            .EnumerateFiles(inputPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(
                Path.GetExtension(path),
                ".pptx",
                StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (sourcePaths.Length == 0)
        {
            throw new ArgumentException(
                "No .pptx files were found in the input folder's top level.",
                nameof(inputPath));
        }

        return (DeckContextBatchInputKind.Directory, sourcePaths);
    }

    private static IReadOnlyList<string> ResolveOutputDirectories(
        DeckContextBatchInputKind inputKind,
        IReadOnlyList<string> sourcePaths,
        string outputDirectory)
    {
        if (inputKind == DeckContextBatchInputKind.File)
        {
            return [outputDirectory];
        }

        var results = new List<string>(sourcePaths.Count);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourcePath in sourcePaths)
        {
            var stem = Path.GetFileNameWithoutExtension(sourcePath);
            var candidate = $"{stem}.deck-context";
            var suffix = 2;
            while (!usedNames.Add(candidate))
            {
                candidate = $"{stem}-{suffix}.deck-context";
                suffix++;
            }

            results.Add(Path.Combine(outputDirectory, candidate));
        }

        return results;
    }

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : $"{value[..maximumLength]}…";

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public sealed class DeckContextBatchResultJsonSerializer
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public string Serialize(DeckContextBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return $"{JsonSerializer.Serialize(result, Options).Replace("\r\n", "\n", StringComparison.Ordinal)}\n";
    }

    public async Task WriteAsync(
        DeckContextBatchResult result,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ??
                        throw new ArgumentException("The result path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                Serialize(result),
                Utf8WithoutBom,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
