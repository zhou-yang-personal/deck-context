using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace DeckContext.App;

public partial class App : System.Windows.Application
{
    private const string FailureDialogTitle = "DeckContext could not start";
    private static readonly object LogLock = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        base.OnStartup(e);

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            ReportFatalFailure("The main window could not be created.", exception);
            Shutdown(1);
        }
    }

    private void HandleDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ReportFatalFailure("DeckContext encountered an unrecoverable UI error.", e.Exception);
        Shutdown(1);
    }

    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            TryWriteFailure("An unrecoverable background error occurred.", exception);
        }
    }

    private static void ReportFatalFailure(string message, Exception exception)
    {
        var logPath = TryWriteFailure(message, exception);
        var logMessage = logPath is null
            ? "The error log could not be written."
            : $"Details were written to:\n{logPath}";

        try
        {
            MessageBox.Show(
                $"{message}\n\n{exception.Message}\n\n{logMessage}",
                FailureDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // The application is already failing; logging above is the final fallback.
        }
    }

    private static string? TryWriteFailure(string message, Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeckContext",
                "Logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "application-errors.log");
            var entry = new StringBuilder()
                .AppendLine($"[{DateTimeOffset.UtcNow:O}] {message}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($".NET: {Environment.Version}")
                .AppendLine($"Application directory: {AppContext.BaseDirectory}")
                .AppendLine(exception.ToString())
                .AppendLine()
                .ToString();

            lock (LogLock)
            {
                File.AppendAllText(logPath, entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            return logPath;
        }
        catch
        {
            return null;
        }
    }
}
