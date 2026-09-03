using DeckContext.Pipeline;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: DeckContext.Verification.exe <input.pptx> <output-directory>");
    return 2;
}

var sourcePath = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(args[1]);

try
{
    var progress = new Progress<ConversionProgress>(item =>
        Console.WriteLine($"[{item.Percentage,3}%] {item.Stage}: {item.Message}"));
    var result = await new DeckContextConversionService().ConvertAsync(
        sourcePath,
        outputDirectory,
        progress);

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
