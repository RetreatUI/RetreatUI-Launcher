using Microsoft.Win32;

namespace RetreatUI.Launcher.Services;

public sealed class GamePathService
{
    private static readonly string[] AddonFolderNames = { "RetreatUI", "RetreatUI_Classes" };

    public string? AutoDetectAddOnsPath()
    {
        foreach (string candidate in GetCandidates())
        {
            string? found = NormalizeToAddOnsPath(candidate);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    public string? NormalizeToAddOnsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch
        {
            return null;
        }

        if (Directory.Exists(fullPath)
            && string.Equals(Path.GetFileName(fullPath), "AddOns", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(Directory.GetParent(fullPath)?.FullName), "Interface", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        string direct = Path.Combine(fullPath, "Interface", "AddOns");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        try
        {
            foreach (string interfaceDirectory in Directory.EnumerateDirectories(fullPath, "Interface", SearchOption.AllDirectories).Take(20))
            {
                string addons = Path.Combine(interfaceDirectory, "AddOns");
                if (Directory.Exists(addons))
                {
                    return addons;
                }
            }
        }
        catch
        {
            // Ignore inaccessible folders.
        }

        return null;
    }

    public string? FindGameExecutable(string addOnsPath)
    {
        DirectoryInfo? current = new DirectoryInfo(addOnsPath);
        for (int level = 0; level < 6 && current is not null; level++, current = current.Parent)
        {
            try
            {
                string[] preferredNames =
                {
                    "Ascension Launcher.exe",
                    "Project Ascension.exe",
                    "Ascension.exe",
                    "Launcher.exe"
                };

                foreach (string fileName in preferredNames)
                {
                    string candidate = Path.Combine(current.FullName, fileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                string? ascensionExe = Directory.EnumerateFiles(current.FullName, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(path => Path.GetFileName(path).Contains("Ascension", StringComparison.OrdinalIgnoreCase));
                if (ascensionExe is not null)
                {
                    return ascensionExe;
                }
            }
            catch
            {
                // Ignore inaccessible folders.
            }
        }

        return null;
    }

    public string ReadInstalledVersion(string addOnsPath)
    {
        string? version = ReadTocVersion(Path.Combine(addOnsPath, "RetreatUI", "RetreatUI.toc"));
        version ??= ReadTocVersion(Path.Combine(addOnsPath, "RetreatUI_Classes", "RetreatUI_Classes.toc"));
        return string.IsNullOrWhiteSpace(version) ? "Not installed" : version;
    }

    public bool HasValidAddOnsPath(string addOnsPath)
    {
        return Directory.Exists(addOnsPath)
               && string.Equals(Path.GetFileName(addOnsPath), "AddOns", StringComparison.OrdinalIgnoreCase);
    }

    public IEnumerable<string> GetInstalledAddonFolders(string addOnsPath)
    {
        return AddonFolderNames
            .Select(name => Path.Combine(addOnsPath, name))
            .Where(Directory.Exists);
    }

    private static string? ReadTocVersion(string tocPath)
    {
        if (!File.Exists(tocPath))
        {
            return null;
        }

        try
        {
            foreach (string line in File.ReadLines(tocPath))
            {
                const string prefix = "## Version:";
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return line[prefix.Length..].Trim();
                }
            }
        }
        catch
        {
            // Return null when the file cannot be read.
        }

        return null;
    }

    private static IEnumerable<string> GetCandidates()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        string[] basePaths =
        {
            Path.Combine(programFiles, "Project Ascension"),
            Path.Combine(programFiles, "Ascension Launcher"),
            Path.Combine(programFilesX86, "Project Ascension"),
            Path.Combine(programFilesX86, "Ascension Launcher"),
            Path.Combine(localAppData, "Project Ascension"),
            Path.Combine(localAppData, "Ascension Launcher"),
            Path.Combine(localAppData, "Programs", "Project Ascension"),
            Path.Combine(localAppData, "Programs", "Ascension Launcher"),
            Path.Combine(appData, "Project Ascension"),
            Path.Combine(appData, "Ascension Launcher"),
            Path.Combine(desktop, "Project Ascension"),
            Path.Combine(desktop, "Ascension Launcher")
        };

        foreach (string path in basePaths.Where(Directory.Exists))
        {
            yield return path;
        }

        foreach (string registryPath in GetRegistryInstallLocations())
        {
            yield return registryPath;
        }
    }

    private static IEnumerable<string> GetRegistryInstallLocations()
    {
        string[] roots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (string root in roots)
            {
                RegistryView[] views = { RegistryView.Registry64, RegistryView.Registry32 };
                foreach (RegistryView view in views)
                {
                    using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using RegistryKey? uninstall = baseKey.OpenSubKey(root);
                    if (uninstall is null) continue;

                    foreach (string subKeyName in uninstall.GetSubKeyNames())
                    {
                        using RegistryKey? app = uninstall.OpenSubKey(subKeyName);
                        string? displayName = app?.GetValue("DisplayName") as string;
                        if (displayName?.Contains("Ascension", StringComparison.OrdinalIgnoreCase) != true)
                        {
                            continue;
                        }

                        string? installLocation = app?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                        {
                            yield return installLocation;
                        }
                    }
                }
            }
        }
    }
}
