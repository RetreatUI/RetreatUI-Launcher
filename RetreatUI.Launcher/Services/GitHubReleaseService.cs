using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RetreatUI.Launcher.Models;

namespace RetreatUI.Launcher.Services;

public sealed class GitHubReleaseService
{
    private const string ReleasesUrl = "https://api.github.com/repos/RetreatUI/RetreatUI-Addon/releases?per_page=30";
    private readonly HttpClient _httpClient;

    public GitHubReleaseService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("RetreatUI-Launcher", "0.1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<GitHubRelease?> GetLatestReleaseAsync(bool includeBeta, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(ReleasesUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        List<GitHubRelease> releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(
            stream,
            cancellationToken: cancellationToken) ?? new List<GitHubRelease>();

        IEnumerable<GitHubRelease> candidates = releases.Where(r => !r.Draft);
        if (!includeBeta)
        {
            candidates = candidates.Where(r => !r.Prerelease);
        }

        return candidates
            .Where(HasRetreatUiAsset)
            .OrderByDescending(r => ParseVersion(r.TagName))
            .ThenByDescending(r => r.PublishedAt)
            .FirstOrDefault();
    }

    public static GitHubAsset? FindRetreatUiAsset(GitHubRelease release)
    {
        return release.Assets.FirstOrDefault(asset =>
            asset.Name.StartsWith("RetreatUI_v", StringComparison.OrdinalIgnoreCase)
            && asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public async Task DownloadAssetAsync(
        GitHubAsset asset,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            asset.BrowserDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream output = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

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

    private static bool HasRetreatUiAsset(GitHubRelease release) => FindRetreatUiAsset(release) is not null;

    private static VersionKey ParseVersion(string tag)
    {
        string value = tag.Trim().TrimStart('v', 'V');
        string[] split = value.Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
        string[] parts = split[0].Split('.');

        int major = parts.Length > 0 && int.TryParse(parts[0], out int ma) ? ma : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out int mi) ? mi : 0;
        int patch = parts.Length > 2 && int.TryParse(parts[2], out int pa) ? pa : 0;

        bool prerelease = split.Length > 1;
        int prereleaseNumber = 0;
        if (prerelease)
        {
            string[] prereleaseParts = split[1].Split('.');
            int.TryParse(prereleaseParts.LastOrDefault(), out prereleaseNumber);
        }

        return new VersionKey(major, minor, patch, prerelease, prereleaseNumber);
    }

    private readonly record struct VersionKey(
        int Major,
        int Minor,
        int Patch,
        bool IsPrerelease,
        int PrereleaseNumber) : IComparable<VersionKey>
    {
        public int CompareTo(VersionKey other)
        {
            int result = Major.CompareTo(other.Major);
            if (result != 0) return result;
            result = Minor.CompareTo(other.Minor);
            if (result != 0) return result;
            result = Patch.CompareTo(other.Patch);
            if (result != 0) return result;

            if (IsPrerelease != other.IsPrerelease)
            {
                return IsPrerelease ? -1 : 1;
            }

            return PrereleaseNumber.CompareTo(other.PrereleaseNumber);
        }
    }
}
