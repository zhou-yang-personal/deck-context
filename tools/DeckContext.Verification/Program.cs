using DeckContext.Pipeline;

if (args.Length is not 2 and not 3)
{
    Console.Error.WriteLine(
        "Usage: DeckContext.Verification.exe <input.pptx> <output-directory> " +
        "[--no-ocr]");
    return 2;
}

var sourcePath = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(args[1]);
try
{
    if (args.Length == 3 && !string.Equals(args[2], "--no-ocr", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("The optional argument must be --no-ocr.");
        return 2;
    }

    var imageTextProvider = args.Length == 3 ? null : new TesseractImageTextProvider();
    WorkerLifetime.NativeOcrProvider = imageTextProvider;

    var progress = new Progress<ConversionProgress>(item =>
        Console.WriteLine($"[{item.Percentage,3}%] {item.Stage}: {item.Message}"));
    var result = await new DeckContextConversionService().ConvertAsync(
        sourcePath,
        outputDirectory,
        progress,
        options: new DeckContextConversionOptions(imageTextProvider));

    Console.WriteLine($"Status: {result.Document.Status}");
    Console.WriteLine($"Slides: {result.Document.Slides.Count}");
    Console.WriteLine($"Assets: {result.Assets.Count}");
    Console.WriteLine($"Markdown: {result.MarkdownPath}");
    Console.WriteLine($"JSON: {result.ContextJsonPath}");
    Console.WriteLine($"Report: {result.ExtractionReportPath}");
    Console.WriteLine($"Manifest: {result.ManifestPath}");
    // This executable is a short-lived isolation worker. Keep the provider rooted
    // and let the OS reclaim native OCR state at process exit; native teardown must
    // never run inside the long-lived WPF process.
    return result.Document.Status == DeckContext.Domain.Extraction.ExtractionStatus.Failed ? 1 : 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"DeckContext verification failed: {exception.Message}");
    return 1;
}

internal static class WorkerLifetime
{
    internal static TesseractImageTextProvider? NativeOcrProvider { get; set; }
}
