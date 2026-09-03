using DeckContext.Export;
using DeckContext.OpenXml;

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
    Directory.CreateDirectory(outputDirectory);
    var document = new OpenXmlDeckContextReader().Read(sourcePath);
    var contextPath = Path.Combine(outputDirectory, "deck.context.json");
    File.WriteAllText(contextPath, new DeckContextJsonSerializer().Serialize(document));

    var workbookDirectory = Path.Combine(outputDirectory, "workbooks");
    var workbooks = document.Slides
        .SelectMany(slide => slide.Elements)
        .Select(element => element.Chart?.EmbeddedWorkbook)
        .Where(workbook => workbook is not null)
        .Cast<DeckContext.Domain.Model.EmbeddedWorkbookContext>()
        .DistinctBy(workbook => (workbook.ChartPartUri, workbook.RelationshipId))
        .ToArray();
    var exporter = new OpenXmlEmbeddedWorkbookAssetExporter();

    foreach (var workbook in workbooks)
    {
        exporter.Export(
            sourcePath,
            workbook,
            Path.Combine(workbookDirectory, SanitizeFileName(workbook.SuggestedFileName)));
    }

    Console.WriteLine($"Status: {document.Status}");
    Console.WriteLine($"Slides: {document.Slides.Count}");
    Console.WriteLine($"Embedded workbooks exported: {workbooks.Length}");
    Console.WriteLine($"Context: {contextPath}");
    return document.Status == DeckContext.Domain.Extraction.ExtractionStatus.Failed ? 1 : 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"DeckContext verification failed: {exception.Message}");
    return 1;
}

static string SanitizeFileName(string fileName)
{
    var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
    return new string(fileName
        .Select(character => invalidCharacters.Contains(character) ? '_' : character)
        .ToArray());
}
