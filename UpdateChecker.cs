using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace NextDnsDoh;

internal sealed class UpdateInfo
{
    public UpdateInfo(Version version, string displayVersion, string releaseNotes, string downloadUrl, string releasePageUrl, long size)
    {
        Version = version;
        DisplayVersion = displayVersion;
        ReleaseNotes = releaseNotes;
        DownloadUrl = downloadUrl;
        ReleasePageUrl = releasePageUrl;
        Size = size;
    }

    public Version Version { get; }
    public string DisplayVersion { get; }
    public string ReleaseNotes { get; }
    public string DownloadUrl { get; }
    public string ReleasePageUrl { get; }
    public long Size { get; }
}

internal static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/R0GGER/NextDNS-DoH/releases/latest";
    private const string ReleasesPage = "https://github.com/R0GGER/NextDNS-DoH/releases";
    private const string AssetPrefix = "NextDNS-DoH-";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly string[] AllowedDownloadHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    ];

    /// <summary>
    /// Returns the latest release when it is newer than <paramref name="currentVersion"/>.
    /// Anything unexpected (no network, rate limit, unusable release) returns null.
    /// </summary>
    public static UpdateInfo? Check(string currentVersion)
    {
        try
        {
            if (!TryParseVersion(currentVersion, out var installed))
            {
                return null;
            }

            var release = FetchLatestRelease();
            if (release is null || release.Draft || release.Prerelease)
            {
                return null;
            }

            if (!TryParseVersion(release.TagName, out var latest) || latest <= installed)
            {
                return null;
            }

            var asset = FindInstallerAsset(release.Assets);
            if (asset is null)
            {
                return null;
            }

            var page = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPage : release.HtmlUrl!;
            return new UpdateInfo(
                latest,
                Format(latest),
                (release.Body ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine).Trim(),
                asset.DownloadUrl!,
                page,
                asset.Size);
        }
        catch
        {
            // An update check must never disturb the app.
            return null;
        }
    }

    public static string ReleasesPageUrl => ReleasesPage;

    private static ReleaseJson? FetchLatestRelease()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

        var request = (HttpWebRequest)WebRequest.Create(LatestReleaseApi);
        request.Method = "GET";
        request.UserAgent = "nextdns-doh";
        request.Accept = "application/vnd.github+json";
        request.Timeout = (int)Timeout.TotalMilliseconds;
        request.ReadWriteTimeout = (int)Timeout.TotalMilliseconds;
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = (HttpWebResponse)request.GetResponse();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        using var stream = response.GetResponseStream();
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        var serializer = new DataContractJsonSerializer(typeof(ReleaseJson));
        return serializer.ReadObject(buffer) as ReleaseJson;
    }

    private static AssetJson? FindInstallerAsset(AssetJson[]? assets)
    {
        if (assets is null)
        {
            return null;
        }

        foreach (var asset in assets)
        {
            var name = asset?.Name ?? "";
            if (!name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsAllowedDownloadUrl(asset!.DownloadUrl))
            {
                return asset;
            }
        }

        return null;
    }

    private static bool IsAllowedDownloadUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        foreach (var host in AllowedDownloadHosts)
        {
            if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Format(Version version) =>
        version.Revision > 0 ? version.ToString(4) : version.ToString(3);

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0);
        var text = (value ?? "").Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(1);
        }

        // Ignore anything after the numeric part, for example "1.0.7-beta".
        var end = 0;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.'))
        {
            end++;
        }

        text = text.Substring(0, end).TrimEnd('.');
        if (!text.Contains("."))
        {
            return false;
        }

        if (!Version.TryParse(text, out var parsed))
        {
            return false;
        }

        // Normalize so 1.0.6 and 1.0.6.0 compare as equal.
        version = new Version(
            parsed.Major,
            parsed.Minor,
            Math.Max(parsed.Build, 0),
            Math.Max(parsed.Revision, 0));
        return true;
    }

    [DataContract]
    private sealed class ReleaseJson
    {
        [DataMember(Name = "tag_name")]
        public string? TagName { get; set; }

        [DataMember(Name = "html_url")]
        public string? HtmlUrl { get; set; }

        [DataMember(Name = "body")]
        public string? Body { get; set; }

        [DataMember(Name = "draft")]
        public bool Draft { get; set; }

        [DataMember(Name = "prerelease")]
        public bool Prerelease { get; set; }

        [DataMember(Name = "assets")]
        public AssetJson[]? Assets { get; set; }
    }

    [DataContract]
    private sealed class AssetJson
    {
        [DataMember(Name = "name")]
        public string? Name { get; set; }

        [DataMember(Name = "browser_download_url")]
        public string? DownloadUrl { get; set; }

        [DataMember(Name = "size")]
        public long Size { get; set; }
    }
}
