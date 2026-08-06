using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using RetreatUI.Launcher.Models;

namespace RetreatUI.Launcher.Services;

public sealed class LauncherUpdateService
{
    private const string ReleaseFeedUrl =
        "https://raw.githubusercontent.com/RetreatUI/RetreatUI-Launcher-Releases/main/feed/launcher-releases.json";
    private const string ApiReleasesUrl =
        "https://api.github.com/repos/RetreatUI/RetreatUI-Launcher-Releases/releases?per_page=30";
    private const string ExecutableAssetName = "RetreatUI_Launcher.exe";
    private const string ChecksumAssetName = "RetreatUI_Launcher.exe.sha256";

    private readonly HttpClient _httpClient;

    public LauncherUpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("RetreatUI-Launcher", CurrentVersion));
    }

    public string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.3.8";

    public async Task<LauncherUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        List<GitHubRelease> releases = await LoadReleasesAsync(cancellationToken);
        LauncherUpdate? bestUpdate = null;

        foreach (GitHubRelease release in releases)
        {
            if (release.Draft || release.Prerelease)
            {
                continue;
            }

            GitHubAsset? executableAsset = release.Assets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, ExecutableAssetName, StringComparison.OrdinalIgnoreCase)
                && IsVerifiedLauncherAsset(candidate));
            GitHubAsset? checksumAsset = release.Assets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase)
                && IsVerifiedLauncherAsset(candidate));

            if (executableAsset is null || checksumAsset is null)
            {
                continue;
            }

            string candidateVersion = NormalizeLauncherVersion(release.TagName);
            if (!IsNewerVersion(candidateVersion, CurrentVersion))
            {
                continue;
            }

            if (bestUpdate is null || IsNewerVersion(candidateVersion, bestUpdate.Version))
            {
                bestUpdate = new LauncherUpdate(
                    candidateVersion,
                    release,
                    executableAsset,
                    checksumAsset);
            }
        }

        return bestUpdate;
    }

    public async Task PrepareUpdateAndRestartAsync(
        LauncherUpdate update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsNewerVersion(update.Version, CurrentVersion))
        {
            throw new InvalidOperationException(
                $"Launcher {update.Version} is not newer than the installed launcher {CurrentVersion}.");
        }

        if (!IsVerifiedLauncherAsset(update.Asset)
            || !IsVerifiedLauncherAsset(update.ChecksumAsset))
        {
            throw new InvalidOperationException(
                "The selected launcher update is not backed by verified GitHub release assets.");
        }

        string currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The launcher executable path could not be determined.");
        string currentDirectory = Path.GetDirectoryName(currentExecutable)
            ?? throw new InvalidOperationException("The launcher directory could not be determined.");

        string updateRoot = Path.Combine(
            Path.GetTempPath(),
            "RetreatUI-Launcher",
            "SelfUpdate",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateRoot);

        string downloadedExecutable = Path.Combine(updateRoot, ExecutableAssetName);
        string downloadedChecksum = Path.Combine(updateRoot, ChecksumAssetName);
        string scriptPath = Path.Combine(updateRoot, "apply-update.ps1");
        string backupExecutable = Path.Combine(currentDirectory, "RetreatUI_Launcher.previous.exe");

        await DownloadFileAsync(
            update.Asset.BrowserDownloadUrl,
            downloadedExecutable,
            progress,
            cancellationToken);
        await DownloadFileAsync(
            update.ChecksumAsset.BrowserDownloadUrl,
            downloadedChecksum,
            null,
            cancellationToken);

        string expectedHash = ParseSha256(await File.ReadAllTextAsync(downloadedChecksum, cancellationToken));
        string actualHash = await ComputeSha256Async(downloadedExecutable, cancellationToken);
        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The downloaded launcher failed SHA-256 verification. The current launcher was not changed.");
        }

        FileVersionInfo downloadedVersionInfo = FileVersionInfo.GetVersionInfo(downloadedExecutable);
        string downloadedVersion = downloadedVersionInfo.FileVersion ?? string.Empty;
        if (!VersionsMatch(update.Version, downloadedVersion))
        {
            throw new InvalidOperationException(
                $"The downloaded launcher reports version {downloadedVersion}, but {update.Version} was expected.");
        }

        string escapedDownloaded = EscapePowerShellLiteral(downloadedExecutable);
        string escapedCurrent = EscapePowerShellLiteral(currentExecutable);
        string escapedBackup = EscapePowerShellLiteral(backupExecutable);
        string escapedRoot = EscapePowerShellLiteral(updateRoot);
        int processId = Environment.ProcessId;

        string script = $$"""
            $ErrorActionPreference = 'Stop'
            $processIdToWaitFor = {{processId}}
            $downloaded = '{{escapedDownloaded}}'
            $current = '{{escapedCurrent}}'
            $backup = '{{escapedBackup}}'
            $updateRoot = '{{escapedRoot}}'

            while (Get-Process -Id $processIdToWaitFor -ErrorAction SilentlyContinue) {
                Start-Sleep -Milliseconds 300
            }

            try {
                Copy-Item -LiteralPath $current -Destination $backup -Force
                Copy-Item -LiteralPath $downloaded -Destination $current -Force
                $newProcess = Start-Process -FilePath $current -PassThru
                Start-Sleep -Seconds 2
                if ($newProcess.HasExited) {
                    throw 'The updated launcher closed immediately after startup.'
                }
                Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
            }
            catch {
                if (Test-Path -LiteralPath $backup) {
                    Copy-Item -LiteralPath $backup -Destination $current -Force
                    Start-Process -FilePath $current
                }
            }
            finally {
                Remove-Item -LiteralPath $updateRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
            """;
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    public static bool IsNewerVersion(string candidate, string installed)
    {
        Version candidateVersion = ParseVersion(candidate);
        Version installedVersion = ParseVersion(installed);
        return candidateVersion > installedVersion;
    }

    private async Task<List<GitHubRelease>> LoadReleasesAsync(CancellationToken cancellationToken)
    {
        Task<List<GitHubRelease>?> feedTask = TryLoadFeedAsync(cancellationToken);
        Task<List<GitHubRelease>?> apiTask = TryLoadApiAsync(cancellationToken);
        await Task.WhenAll(feedTask, apiTask);

        List<GitHubRelease>? feedReleases = await feedTask;
        List<GitHubRelease>? apiReleases = await apiTask;
        if (feedReleases is null && apiReleases is null)
        {
            throw new HttpRequestException(
                "Neither the launcher release feed nor the GitHub Releases API could be loaded.");
        }

        Dictionary<string, GitHubRelease> merged = new(StringComparer.OrdinalIgnoreCase);
        AddReleases(merged, feedReleases);
        AddReleases(merged, apiReleases);
        return merged.Values.ToList();
    }

    private async Task<List<GitHubRelease>?> TryLoadFeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            string feedUrl = $"{ReleaseFeedUrl}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            using HttpRequestMessage request = new(HttpMethod.Get, feedUrl);
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return await DeserializeReleasesAsync(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<List<GitHubRelease>?> TryLoadApiAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, ApiReleasesUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return await DeserializeReleasesAsync(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static void AddReleases(
        IDictionary<string, GitHubRelease> target,
        IEnumerable<GitHubRelease>? releases)
    {
        if (releases is null)
        {
            return;
        }

        foreach (GitHubRelease release in releases)
        {
            if (!string.IsNullOrWhiteSpace(release.TagName))
            {
                target[release.TagName] = release;
            }
        }
    }

    private static async Task<List<GitHubRelease>> DeserializeReleasesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(
            stream,
            cancellationToken: cancellationToken) ?? new List<GitHubRelease>();
    }

    private async Task DownloadFileAsync(
        string url,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream output = new(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        byte[] buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            if (total is > 0)
            {
                progress?.Report(received * 100d / total.Value);
            }
        }

        if (received <= 0)
        {
            throw new InvalidOperationException("The downloaded launcher asset was empty.");
        }
    }

    private static bool IsVerifiedLauncherAsset(GitHubAsset asset)
    {
        if (asset is null
            || asset.Size <= 0
            || string.IsNullOrWhiteSpace(asset.Name)
            || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.AbsolutePath.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase)
               && !uri.AbsolutePath.Contains("/archive/", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ParseSha256(string value)
    {
        string hash = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("The launcher checksum file is invalid.");
        }

        return hash;
    }

    private static bool VersionsMatch(string expected, string actual)
    {
        Version expectedVersion = ParseVersion(expected);
        Version actualVersion = ParseVersion(actual);
        return expectedVersion.Major == actualVersion.Major
            && expectedVersion.Minor == actualVersion.Minor
            && expectedVersion.Build == actualVersion.Build;
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''");

    private static string NormalizeLauncherVersion(string value)
    {
        string normalized = value.Trim();
        if (normalized.StartsWith("launcher-v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[10..];
        }
        else
        {
            normalized = normalized.TrimStart('v', 'V');
        }

        return normalized;
    }

    private static Version ParseVersion(string value)
    {
        string core = value.Trim().Split('-', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return Version.TryParse(core, out Version? version) ? version : new Version(0, 0, 0);
    }
}

public sealed record LauncherUpdate(
    string Version,
    GitHubRelease Release,
    GitHubAsset Asset,
    GitHubAsset ChecksumAsset);
