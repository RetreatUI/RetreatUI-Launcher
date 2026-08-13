using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RetreatUI.Launcher.Models;

namespace RetreatUI.Launcher.Services;

public sealed class GitHubReleaseService
{
    private const string PrimaryReleaseFeedUrl =
        "https://pub-1f3b72d79f1d4138945f7bd13e131def.r2.dev/feed/addon-releases.json";
    private const string LegacyReleaseFeedUrl =
        "https://raw.githubusercontent.com/RetreatUI/RetreatUI-Launcher-Releases/main/feed/addon-releases.json";
    private const string CoAApiReleasesUrl =
        "https://api.github.com/repos/RetreatUI/RetreatUI-Addon/releases?per_page=100";
    private const string TbcApiReleasesUrl =
        "https://api.github.com/repos/RetreatUI/RetreatUI-TBC/releases?per_page=100";
    private const string PackageApiReleasesUrl =
        "https://api.github.com/repos/RetreatUI/RetreatUI-Launcher-Releases/releases?per_page=100";
    private const string CloudflareR2Host =
        "pub-1f3b72d79f1d4138945f7bd13e131def.r2.dev";

    private static readonly string[] TbcAssetPrefixes =
    {
        "RetreatUI_TBC_v",
        "RetreatUI-TBC-v"
    };

    private const string BuffManagerAssetPrefix = "RetreatUI_BuffManager_v";

    private readonly HttpClient _httpClient;

    public GitHubReleaseService()
    {
        string launcherVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.3.11";
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RetreatUI-Launcher", launcherVersion));
    }

    public async Task<GitHubRelease?> GetLatestReleaseAsync(GameEdition edition, bool includeBeta, CancellationToken cancellationToken = default)
    {
        List<GitHubRelease> releases = await LoadReleasesAsync(cancellationToken);
        IEnumerable<GitHubRelease> candidates = releases.Where(release => !release.Draft);
        if (!includeBeta) candidates = candidates.Where(release => !release.Prerelease);
        return candidates
            .Where(release => FindRetreatUiAsset(release, edition) is not null)
            .OrderByDescending(release => AddonVersion.Parse(GetAssetVersion(release, edition)))
            .ThenByDescending(release => release.PublishedAt)
            .FirstOrDefault();
    }

    public static GitHubAsset? FindRetreatUiAsset(GitHubRelease release, GameEdition edition) =>
        release.Assets.FirstOrDefault(asset => IsCompatibleAsset(asset, edition));

    public static GitHubAsset? FindBuffManagerAsset(GitHubRelease release) =>
        release.Assets.FirstOrDefault(asset =>
            IsVerifiedReleaseAsset(asset)
            && asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && asset.Name.StartsWith(BuffManagerAssetPrefix, StringComparison.OrdinalIgnoreCase));

    public static string GetAssetVersion(GitHubRelease release, GameEdition edition)
    {
        GitHubAsset? asset = FindRetreatUiAsset(release, edition);
        if (asset is not null)
        {
            string fileName = Path.GetFileNameWithoutExtension(asset.Name);
            if (edition == GameEdition.CoA && fileName.StartsWith("RetreatUI_v", StringComparison.OrdinalIgnoreCase))
                return NormalizeVersion(fileName["RetreatUI_v".Length..]);
            foreach (string prefix in TbcAssetPrefixes)
                if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return NormalizeVersion(fileName[prefix.Length..]);
        }
        return NormalizeVersion(release.TagName);
    }

    public static string GetBuffManagerAssetVersion(GitHubRelease release)
    {
        GitHubAsset? asset = FindBuffManagerAsset(release);
        if (asset is null) return string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(asset.Name);
        return fileName.StartsWith(BuffManagerAssetPrefix, StringComparison.OrdinalIgnoreCase)
            ? NormalizeVersion(fileName[BuffManagerAssetPrefix.Length..])
            : string.Empty;
    }

    public async Task DownloadAssetAsync(GitHubAsset asset, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsVerifiedReleaseAsset(asset)) throw new InvalidOperationException("The selected package is not a verified RetreatUI release asset.");
        using HttpResponseMessage response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
            if (total is > 0) progress?.Report(received * 100d / total.Value);
        }
        if (received <= 0) throw new InvalidOperationException("The downloaded addon package was empty.");
    }

    private async Task<List<GitHubRelease>> LoadReleasesAsync(CancellationToken cancellationToken)
    {
        Task<List<GitHubRelease>?> primaryFeedTask = TryLoadFeedAsync(PrimaryReleaseFeedUrl, cancellationToken);
        Task<List<GitHubRelease>?> legacyFeedTask = TryLoadFeedAsync(LegacyReleaseFeedUrl, cancellationToken);
        Task<List<GitHubRelease>?> coaTask = TryLoadApiAsync(CoAApiReleasesUrl, cancellationToken);
        Task<List<GitHubRelease>?> tbcTask = TryLoadApiAsync(TbcApiReleasesUrl, cancellationToken);
        Task<List<GitHubRelease>?> packageTask = TryLoadApiAsync(PackageApiReleasesUrl, cancellationToken);
        await Task.WhenAll(primaryFeedTask, legacyFeedTask, coaTask, tbcTask, packageTask);

        List<GitHubRelease>? primaryFeedReleases = await primaryFeedTask;
        List<GitHubRelease>? legacyFeedReleases = await legacyFeedTask;
        List<GitHubRelease>? coaReleases = await coaTask;
        List<GitHubRelease>? tbcReleases = await tbcTask;
        List<GitHubRelease>? packageReleases = await packageTask;
        if (primaryFeedReleases is null && legacyFeedReleases is null && coaReleases is null && tbcReleases is null && packageReleases is null)
            throw new HttpRequestException("All RetreatUI release sources were unavailable.");

        Dictionary<string, GitHubRelease> merged = new(StringComparer.OrdinalIgnoreCase);

        // GitHub remains a fallback. The R2 feed is added last so a matching
        // release from our primary distribution source wins deterministically.
        AddReleases(merged, coaReleases);
        AddReleases(merged, tbcReleases);
        AddReleases(merged, packageReleases);
        AddReleases(merged, legacyFeedReleases);
        AddReleases(merged, primaryFeedReleases);
        return merged.Values.ToList();
    }

    private async Task<List<GitHubRelease>?> TryLoadFeedAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            string feedUrl = $"{url}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            using HttpRequestMessage request = new(HttpMethod.Get, feedUrl);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await DeserializeReleasesAsync(response, cancellationToken);
        }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    private async Task<List<GitHubRelease>?> TryLoadApiAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await DeserializeReleasesAsync(response, cancellationToken);
        }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    private static void AddReleases(IDictionary<string, GitHubRelease> target, IEnumerable<GitHubRelease>? releases)
    {
        if (releases is null) return;
        foreach (GitHubRelease release in releases)
        {
            if (string.IsNullOrWhiteSpace(release.TagName)) continue;
            string key = release.TagName;
            if (FindRetreatUiAsset(release, GameEdition.CoA) is GitHubAsset coaAsset) key += "|coa|" + coaAsset.Name;
            else if (FindRetreatUiAsset(release, GameEdition.Tbc) is GitHubAsset tbcAsset) key += "|tbc|" + tbcAsset.Name;
            target[key] = release;
        }
    }

    private static async Task<List<GitHubRelease>> DeserializeReleasesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream, cancellationToken: cancellationToken) ?? new List<GitHubRelease>();
    }

    private static bool IsCompatibleAsset(GitHubAsset asset, GameEdition edition)
    {
        if (!IsVerifiedReleaseAsset(asset) || !asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        if (edition == GameEdition.Tbc)
            return TbcAssetPrefixes.Any(prefix => asset.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return asset.Name.StartsWith("RetreatUI_v", StringComparison.OrdinalIgnoreCase)
               && !asset.Name.Contains("TBC", StringComparison.OrdinalIgnoreCase)
               && !asset.Name.StartsWith(BuffManagerAssetPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVerifiedReleaseAsset(GitHubAsset asset)
    {
        if (asset is null || asset.Size <= 0 || string.IsNullOrWhiteSpace(asset.Name) || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)) return false;
        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;

        if (string.Equals(uri.Host, CloudflareR2Host, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;
        return uri.AbsolutePath.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase)
               && !uri.AbsolutePath.Contains("/archive/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string version) => version.Trim().TrimStart('v', 'V');
}