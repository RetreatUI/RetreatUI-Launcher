using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RetreatUI.Launcher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            ShowStartupFailure(ex);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowStartupFailure(e.Exception);
        Shutdown(-1);
    }

    private static void ShowStartupFailure(Exception exception)
    {
        string logPath = WriteStartupLog(exception);
        MessageBox.Show(
            $"RetreatUI Launcher could not start.\n\nA diagnostic log was saved here:\n{logPath}\n\n{exception.Message}",
            "RetreatUI Launcher startup error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static string WriteStartupLog(Exception exception)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RetreatUI Launcher");
        Directory.CreateDirectory(directory);
        string logPath = Path.Combine(directory, "startup-error.log");
        File.WriteAllText(
            logPath,
            $"UTC: {DateTime.UtcNow:O}{Environment.NewLine}{exception}");
        return logPath;
    }
}
