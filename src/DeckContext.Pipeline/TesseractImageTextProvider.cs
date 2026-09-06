using DeckContext.Application.Contracts;
using DeckContext.Domain.Model;
using TesseractOCR;
using TesseractOCR.Enums;
using PixImage = TesseractOCR.Pix.Image;

namespace DeckContext.Pipeline;

public sealed class TesseractImageTextProvider : IImageTextProvider, IDisposable
{
    public const string DefaultLanguages = "chi_sim+eng+spa";
    private readonly string dataDirectory;
    private readonly string languages;
    private readonly object syncRoot = new();
    private Engine? engine;
    private string? initializationFailure;
    private bool disposed;

    public TesseractImageTextProvider(
        string? dataDirectory = null,
        string languages = DefaultLanguages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languages);
        this.dataDirectory = Path.GetFullPath(dataDirectory ?? GetDefaultDataDirectory());
        this.languages = languages;
    }

    public string ProviderId => $"tesseract-local:{languages}";

    public Task<ImageContentInterpretationContext> AnalyzeAsync(
        ImageTextRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            try
            {
                var ocrEngine = GetOrCreateEngine();
                if (ocrEngine is null)
                {
                    return Task.FromResult(Failed(initializationFailure!));
                }

                using var image = PixImage.LoadFromMemory(request.Content.ToArray());
                var region = CreateVisibleRegion(image, request.Crop);
                using var page = ocrEngine.Process(image, region, PageSegMode.SparseText);
                cancellationToken.ThrowIfCancellationRequested();

                var evaluation = OcrTextQualityEvaluator.Evaluate(page.Text, page.MeanConfidence);
                var description = evaluation.StoredText is null
                    ? $"No readable text was detected by offline OCR ({languages})."
                    : evaluation.Assessment.IncludeInMarkdown
                        ? $"Offline OCR ({languages}); text passed the Markdown quality filter."
                        : $"Offline OCR ({languages}); low-quality text was retained only in JSON.";

                return Task.FromResult(new ImageContentInterpretationContext(
                    ImageContentInterpretationStatus.Succeeded,
                    ProviderId,
                    evaluation.StoredText,
                    description,
                    evaluation.Assessment));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Task.FromResult(Failed($"Offline OCR failed: {exception.Message}"));
            }
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            engine?.Dispose();
            engine = null;
            disposed = true;
        }
    }

    public static string GetDefaultDataDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "ocr", "tessdata");

    private Engine? GetOrCreateEngine()
    {
        if (engine is not null || initializationFailure is not null)
        {
            return engine;
        }

        var missingFiles = languages
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(language => Path.Combine(dataDirectory, $"{language}.traineddata"))
            .Where(path => !File.Exists(path))
            .Select(Path.GetFileName)
            .ToArray();

        if (missingFiles.Length > 0)
        {
            initializationFailure =
                $"Offline OCR data is incomplete in '{dataDirectory}'. Missing: {string.Join(", ", missingFiles)}.";
            return null;
        }

        try
        {
            engine = new Engine(dataDirectory, languages, EngineMode.LstmOnly)
            {
                DefaultPageSegMode = PageSegMode.SparseText
            };
            return engine;
        }
        catch (Exception exception)
        {
            initializationFailure =
                $"Offline OCR could not initialize from '{dataDirectory}': {exception.Message}";
            return null;
        }
    }

    private static Rect CreateVisibleRegion(PixImage image, ImageCropContext? crop)
    {
        if (crop is null)
        {
            return new Rect(0, 0, image.Width, image.Height);
        }

        var left = ClampCrop(crop.LeftFraction);
        var top = ClampCrop(crop.TopFraction);
        var right = ClampCrop(crop.RightFraction);
        var bottom = ClampCrop(crop.BottomFraction);
        var x = (int)Math.Round(image.Width * left, MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(image.Height * top, MidpointRounding.AwayFromZero);
        var width = image.Width - x -
                    (int)Math.Round(image.Width * right, MidpointRounding.AwayFromZero);
        var height = image.Height - y -
                     (int)Math.Round(image.Height * bottom, MidpointRounding.AwayFromZero);

        return width > 0 && height > 0
            ? new Rect(x, y, width, height)
            : new Rect(0, 0, image.Width, image.Height);
    }

    private static double ClampCrop(double fraction) => Math.Clamp(fraction, 0, 1);

    private ImageContentInterpretationContext Failed(string message) =>
        new(ImageContentInterpretationStatus.Failed, ProviderId, null, message);
}
