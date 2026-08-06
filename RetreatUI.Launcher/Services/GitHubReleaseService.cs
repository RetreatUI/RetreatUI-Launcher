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
            new ProductInfoHeaderValue("RetreatUI-Launcher", "0.3.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<GitHubRelease?> GetLatestReleaseAsync(
        GameEdition edition,
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
