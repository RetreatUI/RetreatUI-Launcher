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
    private const string ReleasesUrl = "https://api.github.com/repos/RetreatUI/RetreatUI-Addon/releases?per_page=100";
    private readonly HttpClient _httpClient;

    public GitHubReleaseService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("RetreatUI-Launcher", "0.2.5"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<GitHubRelease?> GetLatestReleaseAsync(
        bool includeBeta,
        CancellationToken cancellationToken = default)
    {
        string requestUrl =
            $"{ReleasesUrl}&cache_bust={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        using HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };
        request.Headers.Pragma.ParseAdd("no-cache");

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        List<GitHubRelease> releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(
            stream,
            cancellationToken: cancellationToken) ?? new List<GitHubRelease>();

        IEnumerable<GitHubRelease> candidates = releases.Where(release => !release.Draft);
        if (!includeBeta)
        {
            candidates = candidates.Where(release => !release.Prerelease);
        }

        return candidates
            .Where(HasRetreatUiAsset)
            .OrderByDescending(release => AddonVersion.Parse(release.TagName))
            .ThenByDescending(release => release.PublishedAt)
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

    private static bool HasRetreatUiAsset(GitHubRelease release) =>
        FindRetreatUiAsset(release) is not null;
}
