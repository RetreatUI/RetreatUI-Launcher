using Microsoft.Win32;
using RetreatUI.Launcher.Models;

namespace RetreatUI.Launcher.Services;

public sealed class GamePathService
{
    private static readonly string[] ManagedAddonFolders = { "RetreatUI", "RetreatUI_Classes" };

    public string? AutoDetectAddOnsPath(GameEdition edition)
    {
        foreach (string candidate in GetKnownCandidates(edition))
        {
            string? normalized = NormalizeToAddOnsPath(candidate);
            if (normalized is not null)
            {
                return normalized;
            }
        }

        return null;
    }

    public string? NormalizeToAddOnsPath(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(selectedPath.Trim());
        }
        catch
        {
            return null;
        }

        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        if (Path.GetFileName(fullPath).Equals("AddOns", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(Path.GetDirectoryName(fullPath) ?? string.Empty)
                .Equals("Interface", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        string direct = Path.Combine(fullPath, "Interface", "AddOns");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        string? interfaceFolder = Directory.EnumerateDirectories(fullPath, "Interface", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (interfaceFolder is not null)
        {
            string addOns = Path.Combine(interfaceFolder, "AddOns");
            if (Directory.Exists(addOns))
            {
                return addOns;
            }
        }

        return null;
    }

    public bool HasValidAddOnsPath(string path)
    {
        return NormalizeToAddOnsPath(path) is not null;
    }

    public string ReadInstalledVersion(string addOnsPath)
    {
        if (!HasValidAddOnsPath(addOnsPath))
        {
            return "Unknown";
        }

        string[] tocPaths =
        {
            Path.Combine(addOnsPath, "RetreatUI", "RetreatUI.toc"),
            Path.Combine(addOnsPath, "RetreatUI_Classes", "RetreatUI_Classes.toc")
        };

        foreach (string tocPath in tocPaths)
        {
            string? version = ReadTocVersion(tocPath);
            if (version is not null) return version;
        }

        return ManagedAddonFolders.Any(folder => Directory.Exists(Path.Combine(addOnsPath, folder)))
            ? "Unknown"
            : "Not installed";
    }

    public string ReadInstalledBuffManagerVersion(string addOnsPath)
    {
        if (!HasValidAddOnsPath(addOnsPath)) return "Unknown";

        string folder = Path.Combine(addOnsPath, "RetreatUI_BuffManager");
        string tocPath = Path.Combine(folder, "RetreatUI_BuffManager.toc");
        string? version = ReadTocVersion(tocPath);
        if (version is not null) return version;
        return Directory.Exists(folder) ? "Unknown" : "Not installed";
    }

    public string? FindGameExecutable(string addOnsPath, GameEdition edition)
    {
        if (string.IsNullOrWhiteSpace(addOnsPath))
        {
            return null;
        }

        DirectoryInfo? current = Directory.GetParent(addOnsPath);
        for (int level = 0; current is not null && level < 8; level++, current = current.Parent)
        {
            foreach (string executableName in GetExecutableNames(edition))
            {
                string candidate = Path.Combine(current.FullName, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? ReadTocVersion(string tocPath)
    {
        if (!File.Exists(tocPath)) return null;
        foreach (string line in File.ReadLines(tocPath))
        {
            if (line.StartsWith("## Version:", StringComparison.OrdinalIgnoreCase))
                return line[(line.IndexOf(':') + 1)..].Trim().TrimStart('v', 'V');
        }
        return "Unknown";
    }

    private static IEnumerable<string> GetKnownCandidates(GameEdition edition)
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (edition == GameEdition.CoA)
        {
            string[] roots =
            {
                Path.Combine(programFiles, "Project Ascension"),
                Path.Combine(programFilesX86, "Project Ascension"),
                Path.Combine(programFiles, "Ascension Launcher", "resources", "client"),
                Path.Combine(programFilesX86, "Ascension Launcher", "resources", "client"),
                Path.Combine(localAppData, "Programs", "Ascension Launcher", "resources", "client"),
                Path.Combine(desktop, "Project Ascension"),
                Path.Combine(desktop, "Ascension")
            };

            return roots.Concat(GetAscensionRegistryLocations());
        }

        string[] wowRoots =
        {
            Path.Combine(programFiles, "World of Warcraft"),
            Path.Combine(programFilesX86, "World of Warcraft"),
            Path.Combine(desktop, "World of Warcraft")
        };

        string[] clientFolders =
        {
            "_anniversary_",
            "_classic_anniversary_",
            "_classic_",
            "_classic_era_",
            "_classic_ptr_"
        };

        List<string> candidates = new();
        foreach (string root in wowRoots.Concat(GetBlizzardRegistryLocations()))
        {
            candidates.Add(root);
            candidates.AddRange(clientFolders.Select(folder => Path.Combine(root, folder)));
        }

        return candidates;
    }

    private static IEnumerable<string> GetAscensionRegistryLocations()
    {
        string[] uninstallRoots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (string uninstallRoot in uninstallRoots)
            {
                using RegistryKey? key = root.OpenSubKey(uninstallRoot);
                if (key is null)
                {
                    continue;
                }

                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    using RegistryKey? subKey = key.OpenSubKey(subKeyName);
                    string displayName = subKey?.GetValue("DisplayName") as string ?? string.Empty;
                    if (!displayName.Contains("Ascension", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? installLocation = subKey?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installLocation))
                    {
                        yield return installLocation;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> GetBlizzardRegistryLocations()
    {
        string[] keys =
        {
            @"SOFTWARE\Blizzard Entertainment\World of Warcraft",
            @"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft"
        };

        foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (string keyPath in keys)
            {
                using RegistryKey? key = root.OpenSubKey(keyPath);
                if (key is null)
                {
                    continue;
                }

                foreach (string valueName in new[] { "InstallPath", "Path" })
                {
                    string? path = key.GetValue(valueName) as string;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        yield return path;
                    }
                }
            }
        }
    }

    private static string[] GetExecutableNames(GameEdition edition)
    {
        return edition == GameEdition.CoA
            ? new[]
            {
                "Ascension Launcher.exe",
                "Project Ascension Launcher.exe",
                "Ascension.exe",
                "Project Ascension.exe",
                "Wow.exe",
                "Wow-64.exe"
            }
            : new[]
            {
                "WowClassic.exe",
                "WowClassicT.exe",
                "Wow.exe",
                "World of Warcraft Launcher.exe",
                "Battle.net Launcher.exe"
            };
    }
}