namespace RetreatUI.Launcher.Models;

public sealed class LauncherSettings
{
    public string SelectedEdition { get; set; } = nameof(GameEdition.CoA);
    public string CoAAddOnsPath { get; set; } = string.Empty;
    public string CoAGameExecutablePath { get; set; } = string.Empty;
    public string TbcAddOnsPath { get; set; } = string.Empty;
    public string TbcGameExecutablePath { get; set; } = string.Empty;
    public bool IncludeBeta { get; set; }

    // Legacy v0.2.x values. They are migrated to the CoA profile on first launch.
    public string AddOnsPath { get; set; } = string.Empty;
    public string GameExecutablePath { get; set; } = string.Empty;
}
