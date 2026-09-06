using System.Diagnostics;
using System.IO;
using System.Windows;
using DeckContext.Pipeline;
using Microsoft.Win32;

namespace DeckContext.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = new MainWindowViewModel(new DeckContextConversionService());
        DataContext = viewModel;
    }

    private void SelectPowerPoint_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select one or more PowerPoint presentations",
            Filter = "PowerPoint presentations (*.pptx)|*.pptx",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.SetInputPaths(dialog.FileNames);
        }
    }

    private void SelectPowerPointFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder containing PowerPoint presentations",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(viewModel.InputPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(viewModel.InputPath) ?? string.Empty;
        }

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.SetInputDirectory(dialog.FolderName);
        }
    }

    private void SelectOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose an output folder",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(viewModel.OutputDirectory))
        {
            dialog.InitialDirectory = Directory.Exists(viewModel.OutputDirectory)
                ? viewModel.OutputDirectory
                : Path.GetDirectoryName(viewModel.OutputDirectory) ?? string.Empty;
        }

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.SetOutputDirectory(dialog.FolderName);
        }
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await viewModel.ConvertAsync();
        }
        catch (Exception exception)
        {
            viewModel.ReportUnexpectedConversionFailure(exception);
        }
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanOpenOutput)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = viewModel.OutputDirectory,
            UseShellExecute = true
        });
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = viewModel.CanChangePaths && TryGetPowerPointPaths(e.Data, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (viewModel.CanChangePaths && TryGetPowerPointPaths(e.Data, out var paths))
        {
            viewModel.SetInputPaths(paths);
        }
    }

    private static bool TryGetPowerPointPaths(IDataObject data, out string[] paths)
    {
        paths = [];
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return false;
        }

        paths = files
            .SelectMany(GetDroppedPowerPointPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return paths.Length > 0;
    }

    private static IEnumerable<string> GetDroppedPowerPointPaths(string path)
    {
        if (File.Exists(path) &&
            string.Equals(Path.GetExtension(path), ".pptx", StringComparison.OrdinalIgnoreCase))
        {
            return [path];
        }

        if (!Directory.Exists(path))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                .Where(file => string.Equals(
                    Path.GetExtension(file),
                    ".pptx",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
