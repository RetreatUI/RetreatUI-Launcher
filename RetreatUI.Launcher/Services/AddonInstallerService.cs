using System.Diagnostics;
using System.IO.Compression;

namespace RetreatUI.Launcher.Services;

public sealed class AddonInstallerService
{
    private static readonly string[] ManagedFolders = { "RetreatUI", "RetreatUI_Classes" };
    private const string RollbackTestMarker = ".retreatui-launcher-test-rollback";

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
        string expectedVersion,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        string workRoot = Path.Combine(Path.GetTempPath(), "RetreatUI-Launcher", Guid.NewGuid().ToString("N"));
        string extractPath = Path.Combine(workRoot, "Extracted");
        string backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RetreatUI Launcher",
            "Backups");
        string backupPath = Path.Combine(backupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        bool hadExistingInstallation = ManagedFolders.Any(folder =>
            Directory.Exists(Path.Combine(addOnsPath, folder)));
        bool installationStarted = false;

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
            ValidateArchiveVersions(sourceRoot, expectedVersion);

            if (hadExistingInstallation)
            {
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
            }

            try
            {
                installationStarted = true;
                status?.Report(hadExistingInstallation ? "Installing update..." : "Installing RetreatUI...");

                for (int index = 0; index < ManagedFolders.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string folderName = ManagedFolders[index];
                    string destination = Path.Combine(addOnsPath, folderName);
                    DeleteDirectory(destination);
                    CopyDirectory(Path.Combine(sourceRoot, folderName), destination, overwrite: true);

                    // One-shot development hook used to verify rollback before the public release.
                    string rollbackMarker = Path.Combine(addOnsPath, RollbackTestMarker);
                    if (index == 0 && File.Exists(rollbackMarker))
                    {
                        File.Delete(rollbackMarker);
                        throw new IOException("Rollback test requested.");
                    }
                }

                status?.Report("Verifying installation...");
                ValidateInstalledCopy(sourceRoot, addOnsPath, expectedVersion);
            }
            catch (Exception installError)
            {
                status?.Report("Installation failed. Restoring previous version...");
                try
                {
                    RestoreBackup(addOnsPath, hadExistingInstallation ? backupPath : null);
                }
                catch (Exception restoreError)
                {
                    throw new IOException(
                        $"The update failed and the previous installation could not be fully restored. " +
                        $"Update error: {installError.Message} Restore error: {restoreError.Message}",
                        restoreError);
                }

                string recoveryMessage = hadExistingInstallation
                    ? "The previous RetreatUI installation was restored successfully."
                    : "The partial installation was removed successfully.";
                throw new IOException($"The installation failed. {recoveryMessage} Details: {installError.Message}", installError);
            }

            status?.Report(hadExistingInstallation
                ? "Update installed successfully."
                : "RetreatUI installed successfully.");

            if (hadExistingInstallation)
            {
                CleanupOldBackups(backupRoot, keep: 5);
            }

            await Task.CompletedTask;
            return new InstallResult(
                true,
                hadExistingInstallation ? backupPath : null,
                null,
                hadExistingInstallation,
                false);
        }
        catch (Exception ex)
        {
            return new InstallResult(
                false,
                hadExistingInstallation && Directory.Exists(backupPath) ? backupPath : null,
                ex.Message,
                hadExistingInstallation,
                installationStarted);
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

    private static void ValidateArchiveVersions(string sourceRoot, string expectedVersion)
    {
        string retreatVersion = ReadTocVersion(Path.Combine(sourceRoot, "RetreatUI", "RetreatUI.toc"));
        string classesVersion = ReadTocVersion(Path.Combine(sourceRoot, "RetreatUI_Classes", "RetreatUI_Classes.toc"));
        string expected = NormalizeVersion(expectedVersion);

        if (!string.Equals(retreatVersion, classesVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The two addon folders use different versions ({retreatVersion} and {classesVersion}).");
        }

        if (!string.Equals(retreatVersion, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The downloaded addon version is {retreatVersion}, but release {expected} was expected.");
        }
    }

    private static void ValidateInstalledCopy(string sourceRoot, string addOnsPath, string expectedVersion)
    {
        foreach (string folderName in ManagedFolders)
        {
            string source = Path.Combine(sourceRoot, folderName);
            string destination = Path.Combine(addOnsPath, folderName);
            ValidateDirectoryCopy(source, destination);
        }

        ValidateArchiveVersions(addOnsPath, expectedVersion);
    }

    private static void ValidateDirectoryCopy(string source, string destination)
    {
        if (!Directory.Exists(destination))
        {
            throw new IOException($"The installed folder is missing: {Path.GetFileName(destination)}");
        }

        string[] sourceFiles = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
        string[] destinationFiles = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).ToArray();
        if (sourceFiles.Length != destinationFiles.Length)
        {
            throw new IOException(
                $"File verification failed for {Path.GetFileName(destination)} " +
                $"({destinationFiles.Length} of {sourceFiles.Length} files installed).");
        }

        foreach (string sourceFile in sourceFiles)
        {
            string relative = Path.GetRelativePath(source, sourceFile);
            string destinationFile = Path.Combine(destination, relative);
            if (!File.Exists(destinationFile)
                || new FileInfo(sourceFile).Length != new FileInfo(destinationFile).Length)
            {
                throw new IOException($"File verification failed: {Path.GetFileName(destination)}\\{relative}");
            }
        }
    }

    private static string ReadTocVersion(string tocPath)
    {
        foreach (string line in File.ReadLines(tocPath))
        {
            if (line.StartsWith("## Version:", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeVersion(line[(line.IndexOf(':') + 1)..]);
            }
        }

        throw new InvalidDataException($"No version was found in {Path.GetFileName(tocPath)}.");
    }

    private static string NormalizeVersion(string version) => version.Trim().TrimStart('v', 'V');

    private static void RestoreBackup(string addOnsPath, string? backupPath)
    {
        foreach (string folderName in ManagedFolders)
        {
            DeleteDirectory(Path.Combine(addOnsPath, folderName));
        }

        if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
        {
            return;
        }

        foreach (string folderName in ManagedFolders)
        {
            string source = Path.Combine(backupPath, folderName);
            if (Directory.Exists(source))
            {
                CopyDirectory(source, Path.Combine(addOnsPath, folderName), overwrite: true);
            }
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
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
            if (!Directory.Exists(backupRoot))
            {
                return;
            }

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

public sealed record InstallResult(
    bool Success,
    string? BackupPath,
    string? ErrorMessage,
    bool HadExistingInstallation,
    bool InstallationStarted);
