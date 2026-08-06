using System.Diagnostics;
using RetreatUI.Launcher.Models;

namespace RetreatUI.Launcher.Services;

public sealed class GameProcessService
{
    public bool IsGameRunning(GameEdition edition)
    {
        string[] exactNames = edition == GameEdition.CoA
            ? new[] { "Wow", "Wow-64", "Ascension", "Project Ascension" }
            : new[] { "Wow", "WowClassic", "WowClassicT" };

        foreach (string processName in exactNames)
        {
            if (Process.GetProcessesByName(processName).Length > 0)
            {
                return true;
            }
        }

        return Process.GetProcesses().Any(process =>
        {
            try
            {
                string processName = process.ProcessName;
                if (processName.Contains("Launcher", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return edition == GameEdition.CoA
                    ? processName.Contains("Ascension", StringComparison.OrdinalIgnoreCase)
                    : processName.Contains("Wow", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        });
    }
}
