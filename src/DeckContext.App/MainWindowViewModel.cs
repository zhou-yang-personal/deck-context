using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using DeckContext.Domain.Extraction;
using DeckContext.Domain.Model;
using DeckContext.Pipeline;

namespace DeckContext.App;

public sealed record DiagnosticDisplayItem(
    string Severity,
    string Code,
    string Message,
    string Location);

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IDeckContextConversionService conversionService;
    private IReadOnlyList<string> inputPaths = Array.Empty<string>();
    private string outputDirectory = string.Empty;
    private string statusMessage = "Select or drop PowerPoint files or a folder to begin.";
    private int progressPercentage;
    private bool isBusy;
    private bool hasCompleted;
    private bool localOcrEnabled = true;

    public MainWindowViewModel(IDeckContextConversionService conversionService)
    {
        this.conversionService = conversionService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> InputPaths => inputPaths;

    public string InputPath => inputPaths.FirstOrDefault() ?? string.Empty;

    public string InputSummary => inputPaths.Count switch
    {
        0 => string.Empty,
        1 => inputPaths[0],
        _ => $"{inputPaths.Count} PowerPoint files selected"
    };

    public string SelectedFilesTooltip => inputPaths.Count == 0
        ? "Drop one or more .pptx files or folders anywhere in this window."
        : string.Join(Environment.NewLine, inputPaths);

    public bool IsBatch => inputPaths.Count > 1;

    public string OutputFolderLabel => IsBatch ? "OUTPUT ROOT FOLDER" : "OUTPUT FOLDER";

    public string ConvertButtonText => IsBatch
        ? $"Extract {inputPaths.Count} presentations"
        : "Extract context";

    public string OutputDirectory
    {
        get => outputDirectory;
        private set => SetField(ref outputDirectory, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public int ProgressPercentage
    {
        get => progressPercentage;
        private set => SetField(ref progressPercentage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                NotifyCommandState();
            }
        }
    }

    public bool HasCompleted
    {
        get => hasCompleted;
        private set
        {
            if (SetField(ref hasCompleted, value))
            {
                OnPropertyChanged(nameof(CanOpenOutput));
            }
        }
    }

    public bool CanConvert => !IsBusy && inputPaths.Count > 0 && inputPaths.All(path =>
        File.Exists(path) &&
        string.Equals(Path.GetExtension(path), ".pptx", StringComparison.OrdinalIgnoreCase)) &&
        !string.IsNullOrWhiteSpace(OutputDirectory);

    public bool CanOpenOutput => HasCompleted && Directory.Exists(OutputDirectory);

    public bool CanChangePaths => !IsBusy;

    public bool LocalOcrEnabled
    {
        get => localOcrEnabled;
        set
        {
            if (IsBusy || !SetField(ref localOcrEnabled, value))
            {
                return;
            }

            StatusMessage = value
                ? "Offline image OCR enabled. Image bytes stay on this computer."
                : "Offline image OCR disabled. Only native PPTX structure and assets will be extracted.";
            NotifyCommandState();
        }
    }

    public ObservableCollection<DiagnosticDisplayItem> Diagnostics { get; } = [];

    public void SetInputPath(string path) => SetInputPaths([path]);

    public void SetInputDirectory(string directoryPath)
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            SetInputPaths([]);
            StatusMessage = "Choose an existing folder.";
            return;
        }

        try
        {
            var paths = Directory
                .EnumerateFiles(Path.GetFullPath(directoryPath), "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(
                    Path.GetExtension(path),
                    ".pptx",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            SetInputPaths(paths);
            if (paths.Length == 0)
            {
                StatusMessage = "No .pptx files were found in the selected folder.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetInputPaths([]);
            StatusMessage = $"Could not read the selected folder: {exception.Message}";
        }
    }

    public void SetInputPaths(IEnumerable<string> paths)
    {
        if (IsBusy)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(paths);
        inputPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => File.Exists(path) &&
                           string.Equals(Path.GetExtension(path), ".pptx", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        OnPropertyChanged(nameof(InputPaths));
        OnPropertyChanged(nameof(InputPath));
        OnPropertyChanged(nameof(InputSummary));
        OnPropertyChanged(nameof(SelectedFilesTooltip));
        OnPropertyChanged(nameof(IsBatch));
        OnPropertyChanged(nameof(OutputFolderLabel));
        OnPropertyChanged(nameof(ConvertButtonText));
        HasCompleted = false;
        ProgressPercentage = 0;
        Diagnostics.Clear();

        if (inputPaths.Count == 1)
        {
            var parent = Path.GetDirectoryName(inputPaths[0]) ?? Environment.CurrentDirectory;
            OutputDirectory = Path.Combine(parent, $"{Path.GetFileNameWithoutExtension(inputPaths[0])}.deck-context");
            StatusMessage = "Ready to extract the presentation context.";
        }
        else if (inputPaths.Count > 1)
        {
            var parent = Path.GetDirectoryName(inputPaths[0]) ?? Environment.CurrentDirectory;
            OutputDirectory = Path.Combine(parent, "DeckContext-batch-output");
            StatusMessage = $"Ready to extract {inputPaths.Count} presentations in sequence.";
        }
        else
        {
            OutputDirectory = string.Empty;
            StatusMessage = "Choose one or more existing .pptx files.";
        }

        NotifyCommandState();
    }

    public void SetOutputDirectory(string path)
    {
        if (IsBusy)
        {
            return;
        }

        OutputDirectory = path;
        HasCompleted = false;
        NotifyCommandState();
    }

    public async Task ConvertAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConvert)
        {
            StatusMessage = "Choose valid .pptx files and an output folder first.";
            return;
        }

        var jobInputPaths = inputPaths.ToArray();
        var jobOutputDirectory = OutputDirectory;
        var jobOutputDirectories = ResolveJobOutputDirectories(jobInputPaths, jobOutputDirectory);
        IsBusy = true;
        HasCompleted = false;
        ProgressPercentage = 0;
        Diagnostics.Clear();
        var acceptsProgress = 1;
        var activeProgressJob = -1;
        TesseractImageTextProvider? imageTextProvider = null;

        try
        {
            if (LocalOcrEnabled)
            {
                imageTextProvider = new TesseractImageTextProvider();
            }

            var succeeded = 0;
            var partial = 0;
            var failed = 0;
            var producedOutputs = 0;
            string? lastFailureMessage = null;

            for (var index = 0; index < jobInputPaths.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var jobIndex = index;
                var sourcePath = jobInputPaths[jobIndex];
                Volatile.Write(ref activeProgressJob, jobIndex);
                var progress = new Progress<ConversionProgress>(update =>
                {
                    if (Volatile.Read(ref acceptsProgress) == 0 ||
                        Volatile.Read(ref activeProgressJob) != jobIndex)
                    {
                        return;
                    }

                    ProgressPercentage = Math.Clamp(
                        (int)Math.Round(
                            ((double)jobIndex + (double)update.Percentage / 100) /
                            jobInputPaths.Length * 100,
                            MidpointRounding.AwayFromZero),
                        0,
                        100);
                    StatusMessage =
                        $"{jobIndex + 1}/{jobInputPaths.Length} {Path.GetFileName(sourcePath)} — {update.Message}";
                });

                try
                {
                    var result = await conversionService.ConvertAsync(
                        sourcePath,
                        jobOutputDirectories[jobIndex],
                        progress,
                        cancellationToken,
                        new DeckContextConversionOptions(imageTextProvider));
                    producedOutputs++;

                    switch (result.Document.Status)
                    {
                        case ExtractionStatus.Succeeded:
                            succeeded++;
                            break;
                        case ExtractionStatus.Partial:
                            partial++;
                            break;
                        default:
                            failed++;
                            break;
                    }

                    AddDiagnostics(result.Document);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failed++;
                    lastFailureMessage = exception.Message;
                    Diagnostics.Add(new DiagnosticDisplayItem(
                        "Error",
                        jobInputPaths.Length == 1 ? "APP-CONVERT" : "APP-BATCH-CONVERT",
                        exception.Message,
                        Path.GetFileName(sourcePath)));
                }
                finally
                {
                    Volatile.Write(ref activeProgressJob, -1);
                }

                ProgressPercentage = (jobIndex + 1) * 100 / jobInputPaths.Length;
            }

            Interlocked.Exchange(ref acceptsProgress, 0);
            ProgressPercentage = 100;
            HasCompleted = producedOutputs > 0;
            StatusMessage = jobInputPaths.Length == 1
                ? SingleJobStatus(succeeded, partial, lastFailureMessage)
                : $"Batch extraction completed: {succeeded} succeeded, {partial} partial, {failed} failed.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Extraction was cancelled.";
        }
        finally
        {
            imageTextProvider?.Dispose();
            Volatile.Write(ref activeProgressJob, -1);
            Interlocked.Exchange(ref acceptsProgress, 0);
            IsBusy = false;
        }
    }

    private void AddDiagnostics(DeckContextDocument document)
    {
        foreach (var diagnostic in document.Diagnostics
                     .Concat(document.Slides.SelectMany(slide => slide.Diagnostics))
                     .Concat(document.Slides.SelectMany(slide => slide.Elements)
                         .SelectMany(element => element.Diagnostics)))
        {
            Diagnostics.Add(new DiagnosticDisplayItem(
                diagnostic.Severity.ToString(),
                diagnostic.Code,
                diagnostic.Message,
                FormatLocation(diagnostic.Source)));
        }
    }

    private static string SingleJobStatus(int succeeded, int partial, string? failureMessage) =>
        (succeeded, partial) switch
        {
            (1, _) => "Extraction completed successfully.",
            (_, 1) => "Extraction completed with recoverable diagnostics.",
            _ when !string.IsNullOrWhiteSpace(failureMessage) => $"Extraction failed: {failureMessage}",
            _ => "Extraction finished, but the presentation could not be fully processed."
        };

    private static IReadOnlyList<string> ResolveJobOutputDirectories(
        IReadOnlyList<string> sourcePaths,
        string outputDirectory)
    {
        if (sourcePaths.Count == 1)
        {
            return [outputDirectory];
        }

        var results = new List<string>(sourcePaths.Count);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourcePath in sourcePaths)
        {
            var stem = Path.GetFileNameWithoutExtension(sourcePath);
            var suffix = 1;
            var directoryName = $"{stem}.deck-context";

            while (!usedNames.Add(directoryName))
            {
                suffix++;
                directoryName = $"{stem}-{suffix}.deck-context";
            }

            results.Add(Path.Combine(outputDirectory, directoryName));
        }

        return results;
    }

    private void NotifyCommandState()
    {
        OnPropertyChanged(nameof(CanConvert));
        OnPropertyChanged(nameof(CanOpenOutput));
        OnPropertyChanged(nameof(CanChangePaths));
        OnPropertyChanged(nameof(LocalOcrEnabled));
        OnPropertyChanged(nameof(ConvertButtonText));
    }

    private static string FormatLocation(SourceReference? source)
    {
        if (source is null)
        {
            return "Deck";
        }

        var parts = new List<string> { Path.GetFileName(source.SourceFileName) };

        if (source.SlideIndex is not null)
        {
            parts.Add($"Slide {source.SlideIndex}");
        }

        if (source.ElementId is not null)
        {
            parts.Add($"object {source.ElementId}");
        }

        if (source.PartUri is not null)
        {
            parts.Add(source.PartUri);
        }

        if (source.RelationshipId is not null)
        {
            parts.Add($"relationship {source.RelationshipId}");
        }

        return string.Join(" · ", parts);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
