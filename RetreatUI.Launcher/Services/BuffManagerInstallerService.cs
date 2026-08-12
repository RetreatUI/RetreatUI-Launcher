using System.IO.Compression;

namespace RetreatUI.Launcher.Services;

public sealed class BuffManagerInstallerService
{
    private const string FolderName = "RetreatUI_BuffManager";
    private const string TocName = "RetreatUI_BuffManager.toc";

    public async Task InstallAsync(string zipPath, string addOnsPath, string expectedVersion, IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Path.Combine(addOnsPath, "RetreatUI")))
            throw new InvalidOperationException("Install RetreatUI for CoA before installing Buff Manager.");

        string workRoot = Path.Combine(Path.GetTempPath(), "RetreatUI-Launcher", "BuffManager", Guid.NewGuid().ToString("N"));
        string extractPath = Path.Combine(workRoot, "Extracted");
        string backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetreatUI Launcher", "Backups", "BuffManager");
        string backupPath = Path.Combine(backupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        string destination = Path.Combine(addOnsPath, FolderName);
        bool hadExisting = Directory.Exists(destination);
        Directory.CreateDirectory(extractPath);

        try
        {
            status?.Report("Validating Buff Manager download...");
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
            cancellationToken.ThrowIfCancellationRequested();
            string source = FindAddonFolder(extractPath) ?? throw new InvalidDataException($"The archive does not contain {FolderName}\\{TocName}.");
            string archiveVersion = ReadTocVersion(Path.Combine(source, TocName));
            if (!string.Equals(NormalizeVersion(archiveVersion), NormalizeVersion(expectedVersion), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The Buff Manager archive is {archiveVersion}, but {expectedVersion} was expected.");

            if (hadExisting)
            {
                status?.Report("Backing up existing Buff Manager...");
                Directory.CreateDirectory(backupPath);
                CopyDirectory(destination, Path.Combine(backupPath, FolderName), overwrite: true);
            }

            try
            {
                status?.Report(hadExisting ? "Updating Buff Manager..." : "Installing Buff Manager...");
                DeleteDirectory(destination);
                CopyDirectory(source, destination, overwrite: true);
                VerifyCopy(source, destination);
                string installedVersion = ReadTocVersion(Path.Combine(destination, TocName));
                if (!string.Equals(NormalizeVersion(installedVersion), NormalizeVersion(expectedVersion), StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Installed Buff Manager version {installedVersion} does not match {expectedVersion}.");
            }
            catch
            {
                status?.Report("Buff Manager install failed. Restoring previous copy...");
                DeleteDirectory(destination);
                string backup = Path.Combine(backupPath, FolderName);
                if (hadExisting && Directory.Exists(backup)) CopyDirectory(backup, destination, overwrite: true);
                throw;
            }

            status?.Report(hadExisting ? "Buff Manager updated successfully." : "Buff Manager installed successfully.");
            CleanupOldBackups(backupRoot, keep: 5);
            await Task.CompletedTask;
        }
        finally { TryDelete(workRoot); }
    }

    public static string? GetInstalledVersion(string addOnsPath)
    {
        string toc = Path.Combine(addOnsPath, FolderName, TocName);
        if (!File.Exists(toc)) return null;
        try { return ReadTocVersion(toc); } catch { return null; }
    }

    private static string? FindAddonFolder(string extractPath)
    {
        string direct = Path.Combine(extractPath, FolderName);
        if (File.Exists(Path.Combine(direct, TocName))) return direct;
        foreach (string directory in Directory.EnumerateDirectories(extractPath, FolderName, SearchOption.AllDirectories))
            if (File.Exists(Path.Combine(directory, TocName))) return directory;
        return null;
    }

    private static string ReadTocVersion(string tocPath)
    {
        foreach (string line in File.ReadLines(tocPath))
            if (line.StartsWith("## Version:", StringComparison.OrdinalIgnoreCase))
                return NormalizeVersion(line[(line.IndexOf(':') + 1)..]);
        throw new InvalidDataException($"No version was found in {tocPath}.");
    }

    private static string NormalizeVersion(string version) => version.Trim().TrimStart('v', 'V');

    private static void VerifyCopy(string source, string destination)
    {
        string[] sourceFiles = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
        string[] destinationFiles = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).ToArray();
        if (sourceFiles.Length != destinationFiles.Length) throw new IOException("Buff Manager file verification failed.");
        foreach (string sourceFile in sourceFiles)
        {
            string relative = Path.GetRelativePath(source, sourceFile);
            string destinationFile = Path.Combine(destination, relative);
            if (!File.Exists(destinationFile) || new FileInfo(sourceFile).Length != new FileInfo(destinationFile).Length)
                throw new IOException($"Buff Manager verification failed: {relative}");
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
            foreach (DirectoryInfo directory in new DirectoryInfo(backupRoot).EnumerateDirectories().OrderByDescending(d => d.CreationTimeUtc).Skip(keep)) directory.Delete(recursive: true);
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
