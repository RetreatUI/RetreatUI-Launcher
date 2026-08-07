using System.Diagnostics;
using RetreatUI.Launcher.Models;

namespace RetreatUI.Launcher.Services;

public sealed class GameProcessService
{
    public bool IsGameRunning(GameEdition edition)
    {
        // Do not use a broad "contains Wow" scan here. Battle.net helper/background
        // processes can contain WoW-related text even when the actual game client is
        // closed, which previously caused false positives for TBC updates.
        string[] executableNames = edition == GameEdition.CoA
            ? new[] { "Wow", "Wow-64", "Ascension", "Project Ascension" }
            : new[] { "WowClassic", "WowClassicT", "WowClassicT.exe", "WowClassic.exe" };

        foreach (string executableName in executableNames)
        {
            string processName = Path.GetFileNameWithoutExtension(executableName);
            if (string.IsNullOrWhiteSpace(processName))
            {
                continue;
            }

            try
            {
                if (Process.GetProcessesByName(processName).Any(process => IsLiveProcess(process)))
                {
                    return true;
                }
            }
            catch
            {
                // Process enumeration can race with processes exiting. A failed probe
                // must never block an addon update as though the game were running.
            }
        }

        return false;
    }

    private static bool IsLiveProcess(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
        finally
        {
            process.Dispose();
        }
    }
}
