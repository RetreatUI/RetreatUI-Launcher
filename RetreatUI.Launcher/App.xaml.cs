using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RetreatUI.Launcher;

public partial class App : Application
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            MainWindow = new MainWindow();
            MainWindow.SourceInitialized += (_, _) => ApplyDarkTitleBar(MainWindow);
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            ShowStartupFailure(ex);
            Shutdown(-1);
        }
    }

    private static void ApplyDarkTitleBar(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            int enabled = 1;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            }

            int captionColor = ToColorRef(7, 11, 16);
            int textColor = ToColorRef(242, 243, 245);
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch
        {
            // The launcher remains fully usable on Windows versions without these DWM attributes.
        }
    }

    private static int ToColorRef(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

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
