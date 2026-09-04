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
    private string inputPath = string.Empty;
    private string outputDirectory = string.Empty;
    private string statusMessage = "Select or drop a PowerPoint file to begin.";
    private int progressPercentage;
    private bool isBusy;
    private bool hasCompleted;

    public MainWindowViewModel(IDeckContextConversionService conversionService)
    {
        this.conversionService = conversionService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string InputPath
    {
        get => inputPath;
        private set => SetField(ref inputPath, value);
    }

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

    public bool CanConvert => !IsBusy && File.Exists(InputPath) &&
        string.Equals(Path.GetExtension(InputPath), ".pptx", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(OutputDirectory);

    public bool CanOpenOutput => HasCompleted && Directory.Exists(OutputDirectory);

    public bool CanChangePaths => !IsBusy;

    public ObservableCollection<DiagnosticDisplayItem> Diagnostics { get; } = [];

    public void SetInputPath(string path)
    {
        if (IsBusy)
        {
            return;
        }

        InputPath = path;
        HasCompleted = false;
        ProgressPercentage = 0;
        Diagnostics.Clear();

        if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".pptx", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
            OutputDirectory = Path.Combine(parent, $"{Path.GetFileNameWithoutExtension(path)}.deck-context");
            StatusMessage = "Ready to extract the presentation context.";
        }
        else
        {
            StatusMessage = "Choose an existing .pptx file.";
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
            StatusMessage = "Choose a valid .pptx file and output folder first.";
            return;
        }

        var jobInputPath = InputPath;
        var jobOutputDirectory = OutputDirectory;
        IsBusy = true;
        HasCompleted = false;
        ProgressPercentage = 0;
        Diagnostics.Clear();
        var acceptsProgress = 1;

        try
        {
            var progress = new Progress<ConversionProgress>(update =>
            {
                if (Volatile.Read(ref acceptsProgress) == 0)
                {
                    return;
                }

                ProgressPercentage = update.Percentage;
                StatusMessage = update.Message;
            });

            var result = await conversionService.ConvertAsync(
                jobInputPath,
                jobOutputDirectory,
                progress,
                cancellationToken);
            Interlocked.Exchange(ref acceptsProgress, 0);
            ProgressPercentage = 100;
            HasCompleted = true;
            StatusMessage = result.Document.Status switch
            {
                ExtractionStatus.Succeeded => "Extraction completed successfully.",
                ExtractionStatus.Partial => "Extraction completed with recoverable diagnostics.",
                _ => "Extraction finished, but the presentation could not be fully processed."
            };

            foreach (var diagnostic in result.Document.Diagnostics
                         .Concat(result.Document.Slides.SelectMany(slide => slide.Diagnostics))
                         .Concat(result.Document.Slides.SelectMany(slide => slide.Elements).SelectMany(element => element.Diagnostics)))
            {
                Diagnostics.Add(new DiagnosticDisplayItem(
                    diagnostic.Severity.ToString(),
                    diagnostic.Code,
                    diagnostic.Message,
                    FormatLocation(diagnostic.Source)));
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Extraction was cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Extraction failed: {exception.Message}";
            Diagnostics.Add(new DiagnosticDisplayItem("Error", "APP-CONVERT", exception.Message, "Application"));
        }
        finally
        {
            Interlocked.Exchange(ref acceptsProgress, 0);
            IsBusy = false;
        }
    }

    private void NotifyCommandState()
    {
        OnPropertyChanged(nameof(CanConvert));
        OnPropertyChanged(nameof(CanOpenOutput));
        OnPropertyChanged(nameof(CanChangePaths));
    }

    private static string FormatLocation(SourceReference? source)
    {
        if (source is null)
        {
            return "Deck";
        }

        var parts = new List<string>();

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

        return parts.Count == 0 ? source.SourceFileName : string.Join(" · ", parts);
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
