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
    private const string ReleaseFeedUrl =
        "https://raw.githubusercontent.com/RetreatUI/RetreatUI-Launcher-Releases/main/feed/addon-releases.json";
    private const string ApiReleasesUrl =
        "https://api.github.com/repos/RetreatUI/RetreatUI-Addon/releases?per_page=100";

    private static readonly string[] TbcAssetPrefixes =
    {
        "RetreatUI_TBC_v",
        "RetreatUI-TBC-v"
    };

    private readonly HttpClient _httpClient;

    public GitHubReleaseService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("RetreatUI-Launcher", "0.3.7"));
    }

    public async Task<GitHubRelease?> GetLatestReleaseAsync(
        GameEdition edition,
        bool includeBeta,
        CancellationToken cancellationToken = default)
    {
        List<GitHubRelease> releases = await LoadReleasesAsync(cancellationToken);

        IEnumerable<GitHubRelease> candidates = releases.Where(release => !release.Draft);
        if (!includeBeta)
        {
            candidates = candidates.Where(release => !release.Prerelease);
        }

        return candidates
            .Where(release => FindRetreatUiAsset(release, edition) is not null)
            .OrderByDescending(release => AddonVersion.Parse(GetAssetVersion(release, edition)))
            .ThenByDescending(release => release.PublishedAt)
            .FirstOrDefault();
    }

    public static GitHubAsset? FindRetreatUiAsset(GitHubRelease release, GameEdition edition)
    {
        return release.Assets.FirstOrDefault(asset => IsCompatibleAsset(asset.Name, edition));
    }

    public static string GetAssetVersion(GitHubRelease release, GameEdition edition)
    {
        GitHubAsset? asset = FindRetreatUiAsset(release, edition);
        if (asset is not null)
        {
            string fileName = Path.GetFileNameWithoutExtension(asset.Name);
            if (edition == GameEdition.CoA
                && fileName.StartsWith("RetreatUI_v", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeVersion(fileName["RetreatUI_v".Length..]);
            }

            foreach (string prefix in TbcAssetPrefixes)
            {
                if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeVersion(fileName[prefix.Length..]);
                }
            }
        }

        return NormalizeVersion(release.TagName);
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

    private async Task<List<GitHubRelease>> LoadReleasesAsync(CancellationToken cancellationToken)
    {
        List<GitHubRelease>? feedReleases = null;
        List<GitHubRelease>? apiReleases = null;
        Exception? feedError = null;
        Exception? apiError = null;

        try
        {
            string feedUrl = $"{ReleaseFeedUrl}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            using HttpRequestMessage feedRequest = new(HttpMethod.Get, feedUrl);
            feedRequest.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            using HttpResponseMessage feedResponse = await _httpClient.SendAsync(
                feedRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            feedResponse.EnsureSuccessStatusCode();
            feedReleases = await DeserializeReleasesAsync(feedResponse, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            feedError = ex;
        }
        catch (JsonException ex)
        {
            feedError = ex;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            feedError = ex;
        }

        try
        {
            using HttpRequestMessage apiRequest = new(HttpMethod.Get, ApiReleasesUrl);
            apiRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            apiRequest.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            using HttpResponseMessage apiResponse = await _httpClient.SendAsync(
                apiRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            apiResponse.EnsureSuccessStatusCode();
            apiReleases = await DeserializeReleasesAsync(apiResponse, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            apiError = ex;
        }
        catch (JsonException ex)
        {
            apiError = ex;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            apiError = ex;
        }

        if (feedReleases is null && apiReleases is null)
        {
            throw new HttpRequestException(
                "Neither the RetreatUI release feed nor the GitHub Releases API could be loaded.",
                apiError ?? feedError);
        }

        Dictionary<string, GitHubRelease> merged = new(StringComparer.OrdinalIgnoreCase);
        AddReleases(merged, feedReleases);

        // GitHub is authoritative when both sources contain the same tag. This also
        // means a valid but stale CDN feed can never hide a newly published release.
        AddReleases(merged, apiReleases);

        return merged.Values.ToList();
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

    private static bool IsCompatibleAsset(string assetName, GameEdition edition)
    {
        if (!assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (edition == GameEdition.Tbc)
        {
            return TbcAssetPrefixes.Any(prefix =>
                assetName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        return assetName.StartsWith("RetreatUI_v", StringComparison.OrdinalIgnoreCase)
               && !assetName.Contains("TBC", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string version) =>
        version.Trim().TrimStart('v', 'V');
}
