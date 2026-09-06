using DeckContext.Pipeline;

if (args.Length is not 2 and not 4)
{
    Console.Error.WriteLine(
        "Usage: DeckContext.Verification.exe <input.pptx> <output-directory> " +
        "[--vision-model <model>] (reads OPENAI_API_KEY)");
    return 2;
}

var sourcePath = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(args[1]);
OpenAiImageTextProvider? imageTextProvider = null;

try
{
    if (args.Length == 4)
    {
        if (!string.Equals(args[2], "--vision-model", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("The optional argument must be --vision-model <model>.");
            return 2;
        }

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("OPENAI_API_KEY must be set when --vision-model is used.");
            return 2;
        }

        imageTextProvider = new OpenAiImageTextProvider(apiKey, args[3]);
    }

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
