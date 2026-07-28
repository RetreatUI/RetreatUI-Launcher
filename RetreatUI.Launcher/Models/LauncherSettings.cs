namespace RetreatUI.Launcher.Models;

public sealed class LauncherSettings
{
    public string AddOnsPath { get; set; } = string.Empty;
    public string GameExecutablePath { get; set; } = string.Empty;
    public bool IncludeBeta { get; set; }
}
