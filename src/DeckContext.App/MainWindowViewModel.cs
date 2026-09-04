using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using DeckContext.Domain.Extraction;
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

    public ObservableCollection<DiagnosticDisplayItem> Diagnostics { get; } = [];

    public void SetInputPath(string path)
    {
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

        IsBusy = true;
        HasCompleted = false;
        ProgressPercentage = 0;
        Diagnostics.Clear();

        try
        {
            var progress = new Progress<ConversionProgress>(update =>
            {
                ProgressPercentage = update.Percentage;
                StatusMessage = update.Message;
            });

            var result = await conversionService.ConvertAsync(InputPath, OutputDirectory, progress, cancellationToken);
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
                    diagnostic.Source?.ToString() ?? "Deck"));
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
            IsBusy = false;
        }
    }

    private void NotifyCommandState()
    {
        OnPropertyChanged(nameof(CanConvert));
        OnPropertyChanged(nameof(CanOpenOutput));
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
