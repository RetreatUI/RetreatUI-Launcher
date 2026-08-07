using System.Diagnostics;
using System.IO.Compression;

namespace RetreatUI.Launcher.Services;

public sealed class AddonInstallerService
{
    private static readonly PackageSpec CoAPackage = new(
        "Conquest of Azeroth",
        new[]
        {
            new AddonFolderSpec("RetreatUI", "RetreatUI.toc"),
            new AddonFolderSpec("RetreatUI_Classes", "RetreatUI_Classes.toc")
        });

    private static readonly PackageSpec TbcPackage = new(
        "The Burning Crusade",
        new[]
        {
            new AddonFolderSpec("RetreatUI", "RetreatUI.toc")
        });

    private const string RollbackTestMarker = ".retreatui-launcher-test-rollback";

    public bool IsGameRunning()
    {
        string[] exactNames = { "Wow", "Wow-64", "Ascension", "Project Ascension" };
        foreach (string processName in exactNames)
        {
            if (Process.GetProcessesByName(processName).Length > 0) return true;
        }
        return Process.GetProcesses().Any(process =>
        {
            try { return process.ProcessName.Contains("Ascension", StringComparison.OrdinalIgnoreCase) && !process.ProcessName.Contains("Launcher", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });
    }

    public async Task<InstallResult> InstallAsync(string zipPath, string addOnsPath, string expectedVersion, IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        string workRoot = Path.Combine(Path.GetTempPath(), "RetreatUI-Launcher", Guid.NewGuid().ToString("N"));
        string extractPath = Path.Combine(workRoot, "Extracted");
        string backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetreatUI Launcher", "Backups");
        string backupPath = Path.Combine(backupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        bool hadExistingInstallation = false;
        bool installationStarted = false;
        Directory.CreateDirectory(extractPath);
        try
        {
            status?.Report("Validating download...");
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
            cancellationToken.ThrowIfCancellationRequested();
            (PackageSpec package, string sourceRoot) = FindPackage(extractPath) ?? throw new InvalidDataException("The archive does not contain a supported RetreatUI addon package.");
            ValidateAddonFolders(sourceRoot, package);
            ValidateArchiveVersions(sourceRoot, package, expectedVersion);
            hadExistingInstallation = package.Folders.Any(folder => Directory.Exists(Path.Combine(addOnsPath, folder.FolderName)));
            if (hadExistingInstallation)
            {
                status?.Report("Creating backup...");
                Directory.CreateDirectory(backupPath);
                foreach (AddonFolderSpec folder in package.Folders)
                {
                    string existing = Path.Combine(addOnsPath, folder.FolderName);
                    if (Directory.Exists(existing)) CopyDirectory(existing, Path.Combine(backupPath, folder.FolderName), overwrite: true);
                }
            }
            try
            {
                installationStarted = true;
                status?.Report(hadExistingInstallation ? "Installing update..." : "Installing RetreatUI...");
                for (int index = 0; index < package.Folders.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddonFolderSpec folder = package.Folders[index];
                    string destination = Path.Combine(addOnsPath, folder.FolderName);
                    DeleteDirectory(destination);
                    CopyDirectory(Path.Combine(sourceRoot, folder.FolderName), destination, overwrite: true);
                    string rollbackMarker = Path.Combine(addOnsPath, RollbackTestMarker);
                    if (index == 0 && File.Exists(rollbackMarker)) { File.Delete(rollbackMarker); throw new IOException("Rollback test requested."); }
                }
                status?.Report("Verifying installation...");
                ValidateInstalledCopy(sourceRoot, addOnsPath, package, expectedVersion);
            }
            catch (Exception installError)
            {
                status?.Report("Installation failed. Restoring previous version...");
                try { RestoreBackup(addOnsPath, hadExistingInstallation ? backupPath : null, package); }
                catch (Exception restoreError) { throw new IOException($"The update failed and the previous installation could not be fully restored. Update error: {installError.Message} Restore error: {restoreError.Message}", restoreError); }
                string recoveryMessage = hadExistingInstallation ? "The previous RetreatUI installation was restored successfully." : "The partial installation was removed successfully.";
                throw new IOException($"The installation failed. {recoveryMessage} Details: {installError.Message}", installError);
            }
            status?.Report(hadExistingInstallation ? "Update installed successfully." : "RetreatUI installed successfully.");
            if (hadExistingInstallation) CleanupOldBackups(backupRoot, keep: 5);
            await Task.CompletedTask;
            return new InstallResult(true, hadExistingInstallation ? backupPath : null, null, hadExistingInstallation, false);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, hadExistingInstallation && Directory.Exists(backupPath) ? backupPath : null, ex.Message, hadExistingInstallation, installationStarted);
        }
        finally { TryDelete(workRoot); }
    }

    private static (PackageSpec Package, string SourceRoot)? FindPackage(string extractPath)
    {
        foreach (PackageSpec package in new[] { CoAPackage, TbcPackage })
        {
            string? sourceRoot = FindSourceRoot(extractPath, package);
            if (sourceRoot is not null) return (package, sourceRoot);
        }
        return null;
    }

    private static string? FindSourceRoot(string extractPath, PackageSpec package)
    {
        if (package.Folders.All(folder => Directory.Exists(Path.Combine(extractPath, folder.FolderName)))) return extractPath;
        foreach (string directory in Directory.EnumerateDirectories(extractPath, "*", SearchOption.AllDirectories))
            if (package.Folders.All(folder => Directory.Exists(Path.Combine(directory, folder.FolderName)))) return directory;
        return null;
    }

    private static void ValidateAddonFolders(string sourceRoot, PackageSpec package)
    {
        foreach (AddonFolderSpec folder in package.Folders)
        {
            string folderPath = Path.Combine(sourceRoot, folder.FolderName);
            string toc = Path.Combine(folderPath, folder.TocName);
            if (!Directory.Exists(folderPath) || !File.Exists(toc)) throw new InvalidDataException($"Missing required addon file: {folder.FolderName}\\{folder.TocName}");
        }
    }

    private static void ValidateArchiveVersions(string sourceRoot, PackageSpec package, string expectedVersion)
    {
        string expected = NormalizeVersion(expectedVersion);
        string[] versions = package.Folders.Select(folder => ReadTocVersion(Path.Combine(sourceRoot, folder.FolderName, folder.TocName))).ToArray();
        if (versions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) throw new InvalidDataException($"The addon folders in the {package.DisplayName} package use different versions ({string.Join(", ", versions)})." );
        if (!string.Equals(versions[0], expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"The downloaded addon version is {versions[0]}, but release {expected} was expected.");
    }

    private static void ValidateInstalledCopy(string sourceRoot, string addOnsPath, PackageSpec package, string expectedVersion)
    {
        foreach (AddonFolderSpec folder in package.Folders) ValidateDirectoryCopy(Path.Combine(sourceRoot, folder.FolderName), Path.Combine(addOnsPath, folder.FolderName));
        ValidateArchiveVersions(addOnsPath, package, expectedVersion);
    }

    private static void ValidateDirectoryCopy(string source, string destination)
    {
        if (!Directory.Exists(destination)) throw new IOException($"The installed folder is missing: {Path.GetFileName(destination)}");
        string[] sourceFiles = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
        string[] destinationFiles = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).ToArray();
        if (sourceFiles.Length != destinationFiles.Length) throw new IOException($"File verification failed for {Path.GetFileName(destination)} ({destinationFiles.Length} of {sourceFiles.Length} files installed).");
        foreach (string sourceFile in sourceFiles)
        {
            string relative = Path.GetRelativePath(source, sourceFile);
            string destinationFile = Path.Combine(destination, relative);
            if (!File.Exists(destinationFile) || new FileInfo(sourceFile).Length != new FileInfo(destinationFile).Length) throw new IOException($"File verification failed: {Path.GetFileName(destination)}\\{relative}");
        }
    }

    private static string ReadTocVersion(string tocPath)
    {
        foreach (string line in File.ReadLines(tocPath)) if (line.StartsWith("## Version:", StringComparison.OrdinalIgnoreCase)) return NormalizeVersion(line[(line.IndexOf(':') + 1)..]);
        throw new InvalidDataException($"No version was found in {Path.GetFileName(tocPath)}.");
    }

    private static string NormalizeVersion(string version) => version.Trim().TrimStart('v', 'V');

    private static void RestoreBackup(string addOnsPath, string? backupPath, PackageSpec package)
    {
        foreach (AddonFolderSpec folder in package.Folders) DeleteDirectory(Path.Combine(addOnsPath, folder.FolderName));
        if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath)) return;
        foreach (AddonFolderSpec folder in package.Folders)
        {
            string source = Path.Combine(backupPath, folder.FolderName);
            if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(addOnsPath, folder.FolderName), overwrite: true);
        }
    }

    private static void DeleteDirectory(string path) { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory)) File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite);
        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory)) CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)), overwrite);
    }
    private static void CleanupOldBackups(string backupRoot, int keep)
    {
        try
        {
            if (!Directory.Exists(backupRoot)) return;
            foreach (DirectoryInfo directory in new DirectoryInfo(backupRoot).EnumerateDirectories().OrderByDescending(directory => directory.CreationTimeUtc).Skip(keep)) directory.Delete(recursive: true);
        }
        catch { }
    }
    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); else if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
    private sealed record PackageSpec(string DisplayName, AddonFolderSpec[] Folders);
    private sealed record AddonFolderSpec(string FolderName, string TocName);
}

public sealed record InstallResult(bool Success, string? BackupPath, string? ErrorMessage, bool HadExistingInstallation, bool InstallationStarted);
