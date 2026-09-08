using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DeckContext.Export;

namespace DeckContext.Pipeline;

public sealed class ProcessIsolatedDeckContextConversionService : IDeckContextConversionService
{
    private static readonly Regex ProgressPattern = new(
        @"^\[\s*(?<percentage>\d+)%\]\s+(?<stage>[^:]+):\s*(?<message>.*)$",
        RegexOptions.CultureInvariant);
    private readonly string workerPath;
    private readonly IDeckContextWorkerProcessRunner processRunner;

    public ProcessIsolatedDeckContextConversionService(string? workerPath = null)
        : this(
            workerPath ?? Path.Combine(AppContext.BaseDirectory, "DeckContext.Verification.exe"),
            new DeckContextWorkerProcessRunner())
    {
    }

    internal ProcessIsolatedDeckContextConversionService(
        string workerPath,
        IDeckContextWorkerProcessRunner processRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerPath);
        this.workerPath = Path.GetFullPath(workerPath);
        this.processRunner = processRunner;
    }

    public async Task<ContextPackageResult> ConvertAsync(
        string sourcePath,
        string outputDirectory,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        DeckContextConversionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (!File.Exists(workerPath))
        {
            throw new FileNotFoundException(
                "The isolated DeckContext conversion worker was not found.",
                workerPath);
        }

        if (options?.ImageTextProvider is not null)
        {
            throw new NotSupportedException(
                "A custom in-process image provider cannot cross the worker-process boundary. " +
                "Use UseBundledLocalOcr for the packaged offline OCR worker.");
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        var useLocalOcr = options?.UseBundledLocalOcr == true;
        var request = new DeckContextWorkerProcessRequest(
            workerPath,
            fullSourcePath,
            fullOutputDirectory,
            useLocalOcr);

        progress?.Report(new ConversionProgress(
            1,
            "Starting",
            "Starting the isolated conversion worker."));
        var processResult = await processRunner.RunAsync(
            request,
            line => ReportWorkerProgress(line, progress),
            cancellationToken).ConfigureAwait(false);

        if (processResult.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(processResult.StandardError)
                ? "The worker did not provide error details."
                : Limit(processResult.StandardError.Trim(), 2000);
            throw new InvalidOperationException(
                $"The isolated converter exited with code {processResult.ExitCode}. {detail}");
        }

        var markdownPath = Path.Combine(fullOutputDirectory, "deck.context.md");
        var contextJsonPath = Path.Combine(fullOutputDirectory, "deck.context.json");
        var extractionReportPath = Path.Combine(fullOutputDirectory, "extraction-report.json");
        var manifestPath = Path.Combine(fullOutputDirectory, "manifest.json");
        EnsureOutputExists(markdownPath);
        EnsureOutputExists(contextJsonPath);
        EnsureOutputExists(extractionReportPath);
        EnsureOutputExists(manifestPath);

        var document = new DeckContextJsonSerializer().Deserialize(
            await File.ReadAllTextAsync(contextJsonPath, cancellationToken).ConfigureAwait(false));
        var manifest = new ContextPackageManifestSerializer().Deserialize(
            await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false));
        progress?.Report(new ConversionProgress(100, "Complete", "Context package created."));

        return new ContextPackageResult(
            document,
            fullOutputDirectory,
            markdownPath,
            contextJsonPath,
            extractionReportPath,
            manifestPath,
            manifest.Assets);
    }

    private static void ReportWorkerProgress(
        string line,
        IProgress<ConversionProgress>? progress)
    {
        if (progress is null)
        {
            return;
        }

        var match = ProgressPattern.Match(line);
        if (!match.Success ||
            !int.TryParse(match.Groups["percentage"].Value, out var percentage))
        {
            return;
        }

        progress.Report(new ConversionProgress(
            Math.Clamp(percentage, 0, 100),
            match.Groups["stage"].Value.Trim(),
            match.Groups["message"].Value.Trim()));
    }

    private static void EnsureOutputExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException(
                $"The isolated converter reported success but did not create '{Path.GetFileName(path)}'.");
        }
    }

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : $"{value[..maximumLength]}…";
}

internal sealed record DeckContextWorkerProcessRequest(
    string WorkerPath,
    string SourcePath,
    string OutputDirectory,
    bool UseLocalOcr);

internal sealed record DeckContextWorkerProcessResult(
    int ExitCode,
    string StandardError);

internal interface IDeckContextWorkerProcessRunner
{
    Task<DeckContextWorkerProcessResult> RunAsync(
        DeckContextWorkerProcessRequest request,
        Action<string> outputLineReceived,
        CancellationToken cancellationToken);
}

internal sealed class DeckContextWorkerProcessRunner : IDeckContextWorkerProcessRunner
{
    public async Task<DeckContextWorkerProcessResult> RunAsync(
        DeckContextWorkerProcessRequest request,
        Action<string> outputLineReceived,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.WorkerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add(request.SourcePath);
        startInfo.ArgumentList.Add(request.OutputDirectory);
        if (!request.UseLocalOcr)
        {
            startInfo.ArgumentList.Add("--no-ocr");
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The isolated conversion worker could not be started.");
        }

        var outputTask = PumpOutputAsync(process.StandardOutput, outputLineReceived);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await outputTask.ConfigureAwait(false);
            return new DeckContextWorkerProcessResult(
                process.ExitCode,
                await errorTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task PumpOutputAsync(
        StreamReader reader,
        Action<string> outputLineReceived)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            outputLineReceived(line);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Cancellation has already won; process teardown is best effort.
        }
    }
}
