using System.Reflection;
using System.Text.Json;
using RetreatUI.Launcher.Models;

namespace RetreatUI.Launcher.Services;

public sealed class LauncherUpdateService
{
    private const string ReleasesUrl = "https://api.github.com/repos/RetreatUI/RetreatUI-Launcher/releases?per_page=30";
    private readonly HttpClient _httpClient;

    public LauncherUpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("RetreatUI-Launcher", CurrentVersion));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.5";

    public async Task<LauncherUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(ReleasesUrl, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        List<GitHubRelease>? releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(
            stream,
            cancellationToken: cancellationToken);

        if (releases is null || releases.Count == 0)
        {
            return null;
        }

        LauncherUpdate? bestUpdate = null;
        foreach (GitHubRelease release in releases)
        {
            if (release.Draft || release.Prerelease)
            {
                continue;
            }

            GitHubAsset? asset = release.Assets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "RetreatUI_Launcher.exe", StringComparison.OrdinalIgnoreCase));
            if (asset is null)
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
                bestUpdate = new LauncherUpdate(candidateVersion, release, asset);
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

        string currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The launcher executable path could not be determined.");

        string updateRoot = Path.Combine(
            Path.GetTempPath(),
            "RetreatUI-Launcher",
            "SelfUpdate",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateRoot);

        string downloadedExecutable = Path.Combine(updateRoot, "RetreatUI_Launcher.exe");
        string scriptPath = Path.Combine(updateRoot, "apply-update.ps1");

        using (HttpResponseMessage response = await _httpClient.GetAsync(
                   update.Asset.BrowserDownloadUrl,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream output = new(
                downloadedExecutable,
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
        }

        string escapedDownloaded = EscapePowerShellLiteral(downloadedExecutable);
        string escapedCurrent = EscapePowerShellLiteral(currentExecutable);
        string escapedRoot = EscapePowerShellLiteral(updateRoot);
        int processId = Environment.ProcessId;

        string script = $$"""
            $ErrorActionPreference = 'Stop'
            $processIdToWaitFor = {{processId}}
            while (Get-Process -Id $processIdToWaitFor -ErrorAction SilentlyContinue) {
                Start-Sleep -Milliseconds 300
            }
            Copy-Item -LiteralPath '{{escapedDownloaded}}' -Destination '{{escapedCurrent}}' -Force
            Start-Process -FilePath '{{escapedCurrent}}'
            Start-Sleep -Milliseconds 300
            Remove-Item -LiteralPath '{{escapedRoot}}' -Recurse -Force -ErrorAction SilentlyContinue
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

public sealed record LauncherUpdate(string Version, GitHubRelease Release, GitHubAsset Asset);

