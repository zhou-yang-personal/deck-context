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
TesseractImageTextProvider? imageTextProvider = null;

try
{
    if (args.Length == 3 && !string.Equals(args[2], "--no-ocr", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("The optional argument must be --no-ocr.");
        return 2;
    }

    imageTextProvider = args.Length == 3 ? null : new TesseractImageTextProvider();

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
    return result.Document.Status == DeckContext.Domain.Extraction.ExtractionStatus.Failed ? 1 : 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"DeckContext verification failed: {exception.Message}");
    return 1;
}
finally
{
    imageTextProvider?.Dispose();
}
