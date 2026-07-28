using System.Diagnostics;
using System.IO.Compression;

namespace RetreatUI.Launcher.Services;

public sealed class AddonInstallerService
{
    private static readonly string[] ManagedFolders = { "RetreatUI", "RetreatUI_Classes" };

    public bool IsGameRunning()
    {
        string[] exactNames = { "Wow", "Wow-64", "Ascension", "Project Ascension" };
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
                return process.ProcessName.Contains("Ascension", StringComparison.OrdinalIgnoreCase)
                       && !process.ProcessName.Contains("Launcher", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<InstallResult> InstallAsync(
        string zipPath,
        string addOnsPath,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        string workRoot = Path.Combine(Path.GetTempPath(), "RetreatUI-Launcher", Guid.NewGuid().ToString("N"));
        string extractPath = Path.Combine(workRoot, "Extracted");
        string backupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RetreatUI Launcher",
            "Backups",
            DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));

        Directory.CreateDirectory(extractPath);

        try
        {
            status?.Report("Validating download...");
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
            cancellationToken.ThrowIfCancellationRequested();

            string sourceRoot = FindSourceRoot(extractPath)
                                ?? throw new InvalidDataException(
                                    "The archive does not contain RetreatUI and RetreatUI_Classes folders.");

            ValidateAddonFolder(sourceRoot, "RetreatUI", "RetreatUI.toc");
            ValidateAddonFolder(sourceRoot, "RetreatUI_Classes", "RetreatUI_Classes.toc");

            status?.Report("Creating backup...");
            Directory.CreateDirectory(backupPath);
            foreach (string folderName in ManagedFolders)
            {
                string existing = Path.Combine(addOnsPath, folderName);
                if (Directory.Exists(existing))
                {
                    CopyDirectory(existing, Path.Combine(backupPath, folderName), overwrite: true);
                }
            }

            status?.Report("Installing RetreatUI...");
            try
            {
                foreach (string folderName in ManagedFolders)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string destination = Path.Combine(addOnsPath, folderName);
                    if (Directory.Exists(destination))
                    {
                        Directory.Delete(destination, recursive: true);
                    }

                    CopyDirectory(Path.Combine(sourceRoot, folderName), destination, overwrite: true);
                }
            }
            catch
            {
                status?.Report("Installation failed. Restoring backup...");
                RestoreBackup(addOnsPath, backupPath);
                throw;
            }

            status?.Report("Update installed successfully.");
            CleanupOldBackups(Path.GetDirectoryName(backupPath)!, keep: 5);
            return new InstallResult(true, backupPath, null);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, Directory.Exists(backupPath) ? backupPath : null, ex.Message);
        }
        finally
        {
            TryDelete(workRoot);
        }
    }

    private static string? FindSourceRoot(string extractPath)
    {
        if (ManagedFolders.All(folder => Directory.Exists(Path.Combine(extractPath, folder))))
        {
            return extractPath;
        }

        foreach (string directory in Directory.EnumerateDirectories(extractPath, "*", SearchOption.AllDirectories))
        {
            if (ManagedFolders.All(folder => Directory.Exists(Path.Combine(directory, folder))))
            {
                return directory;
            }
        }

        return null;
    }

    private static void ValidateAddonFolder(string sourceRoot, string folderName, string tocName)
    {
        string folder = Path.Combine(sourceRoot, folderName);
        string toc = Path.Combine(folder, tocName);
        if (!Directory.Exists(folder) || !File.Exists(toc))
        {
            throw new InvalidDataException($"Missing required addon file: {folderName}\\{tocName}");
        }
    }

    private static void RestoreBackup(string addOnsPath, string backupPath)
    {
        foreach (string folderName in ManagedFolders)
        {
            string destination = Path.Combine(addOnsPath, folderName);
            TryDelete(destination);

            string source = Path.Combine(backupPath, folderName);
            if (Directory.Exists(source))
            {
                CopyDirectory(source, destination, overwrite: true);
            }
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            string destination = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, destination, overwrite);
        }

        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            string destination = Path.Combine(destinationDirectory, Path.GetFileName(directory));
            CopyDirectory(directory, destination, overwrite);
        }
    }

    private static void CleanupOldBackups(string backupRoot, int keep)
    {
        try
        {
            DirectoryInfo[] backups = new DirectoryInfo(backupRoot)
                .EnumerateDirectories()
                .OrderByDescending(directory => directory.CreationTimeUtc)
                .ToArray();

            foreach (DirectoryInfo directory in backups.Skip(keep))
            {
                directory.Delete(recursive: true);
            }
        }
        catch
        {
            // Backup cleanup should never fail the update.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}

public sealed record InstallResult(bool Success, string? BackupPath, string? ErrorMessage);
