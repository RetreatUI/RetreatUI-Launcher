using System.Reflection;
using System.Text.Json;
using RetreatUI.Launcher.Models;

namespace RetreatUI.Launcher.Services;

public sealed class LauncherUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/RetreatUI/RetreatUI-Launcher/releases/latest";
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
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.3";

    public async Task<LauncherUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // The launcher repository may still be private during development.
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        GitHubRelease? release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
            stream,
            cancellationToken: cancellationToken);

        if (release is null || release.Draft || release.Prerelease)
        {
            return null;
        }

        GitHubAsset? asset = release.Assets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, "RetreatUI_Launcher.exe", StringComparison.OrdinalIgnoreCase));
        if (asset is null)
        {
            return null;
        }

        string latestVersion = NormalizeLauncherVersion(release.TagName);
        return CompareVersions(latestVersion, CurrentVersion) > 0
            ? new LauncherUpdate(latestVersion, release, asset)
            : null;
    }

    public async Task PrepareUpdateAndRestartAsync(
        LauncherUpdate update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
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

    private static int CompareVersions(string left, string right)
    {
        Version leftVersion = ParseVersion(left);
        Version rightVersion = ParseVersion(right);
        return leftVersion.CompareTo(rightVersion);
    }

    private static Version ParseVersion(string value)
    {
        string core = value.Split('-', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return Version.TryParse(core, out Version? version) ? version : new Version(0, 0, 0);
    }
}

public sealed record LauncherUpdate(string Version, GitHubRelease Release, GitHubAsset Asset);

