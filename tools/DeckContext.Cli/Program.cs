using DeckContext.Pipeline;

const string Usage =
    "Usage: DeckContext.Cli.exe <input.pptx-or-folder> <output-directory> [--no-ocr]";

if (args.Length == 1 &&
    (string.Equals(args[0], "--help", StringComparison.Ordinal) ||
     string.Equals(args[0], "-h", StringComparison.Ordinal)))
{
    Console.WriteLine(Usage);
    return 0;
}

if (args.Length is not 2 and not 3)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

if (args.Length == 3 && !string.Equals(args[2], "--no-ocr", StringComparison.Ordinal))
{
    Console.Error.WriteLine("The optional argument must be --no-ocr.");
    Console.Error.WriteLine(Usage);
    return 2;
}

var localOcrEnabled = args.Length != 3;
var workerPath = Path.Combine(AppContext.BaseDirectory, "DeckContext.Verification.exe");

try
{
    var progress = new ConsoleBatchProgress();
    var result = await new DeckContextBatchConversionService(
            new ProcessIsolatedDeckContextConversionService(workerPath))
        .ConvertAsync(
            args[0],
            args[1],
            localOcrEnabled,
            progress);

    string? batchResultPath = null;
    if (result.InputKind == DeckContextBatchInputKind.Directory)
    {
        batchResultPath = Path.Combine(result.OutputRoot, "batch-result.json");
        await new DeckContextBatchResultJsonSerializer().WriteAsync(result, batchResultPath);
    }

    Console.WriteLine();
    foreach (var item in result.Items)
    {
        Console.WriteLine(
            $"[{item.Index}/{result.TotalCount}] {item.Status}: {item.SourceFileName} -> {item.OutputDirectory}");
        if (!string.IsNullOrWhiteSpace(item.Error))
        {
            Console.Error.WriteLine($"  Error: {item.Error}");
        }
    }

    Console.WriteLine();
    Console.WriteLine(
        $"Summary: {result.SucceededCount} succeeded, {result.PartialCount} partial, " +
        $"{result.FailedCount} failed, {result.TotalCount} total.");
    if (batchResultPath is not null)
    {
        Console.WriteLine($"Batch result JSON: {batchResultPath}");
    }

    return result.FailedCount == 0 ? 0 : 1;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"DeckContext CLI input error: {exception.Message}");
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"DeckContext CLI failed: {exception.Message}");
    return 1;
}

internal sealed class ConsoleBatchProgress : IProgress<DeckContextBatchProgress>
{
    public void Report(DeckContextBatchProgress value)
    {
        Console.WriteLine(
            $"[{value.ItemIndex}/{value.ItemCount} {value.OverallPercentage,3}%] " +
            $"{value.SourceFileName} — {value.Conversion.Stage}: {value.Conversion.Message}");
    }
}
