using System.Text.Json;
using RetreatUI.Launcher.Models;

namespace RetreatUI.Launcher.Services;

public sealed class SettingsService
{
    private readonly string _settingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RetreatUI Launcher");

    private string SettingsPath => Path.Combine(_settingsDirectory, "settings.json");

    public async Task<LauncherSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new LauncherSettings();
            }

            await using FileStream stream = File.OpenRead(SettingsPath);
            return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream)
                   ?? new LauncherSettings();
        }
        catch
        {
            return new LauncherSettings();
        }
    }

    public async Task SaveAsync(LauncherSettings settings)
    {
        Directory.CreateDirectory(_settingsDirectory);
        await using FileStream stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
