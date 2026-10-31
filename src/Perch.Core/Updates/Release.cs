using System.Text.Json;

namespace Perch.Updates;

/// <summary>One file attached to a GitHub release.</summary>
public sealed record ReleaseAsset(string Name, Uri Url, long Size);

/// <summary>A published release, as the GitHub API describes it.</summary>
public sealed record Release(Version Version, string VersionText, Uri Page, IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? Asset(string name) =>
        Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads the API's answer for one release. Drafts, prereleases and tags that are not a
    /// version (the release workflow only publishes v1.2.3 tags) come back as null.
    /// </summary>
    internal static Release? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (Flag(root, "draft") || Flag(root, "prerelease")) return null;
        if (!root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } tagName) return null;

        var text = tagName.TrimStart('v', 'V');
        if (ParseVersion(text) is not { } version) return null;

        var page = root.TryGetProperty("html_url", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var u)
            ? u
            : new Uri(Updater.ReleasesPage);

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in list.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name) && name.GetString() is { } n &&
                    asset.TryGetProperty("browser_download_url", out var download) &&
                    Uri.TryCreate(download.GetString(), UriKind.Absolute, out var link) &&
                    link.Scheme == Uri.UriSchemeHttps)
                {
                    var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
                    assets.Add(new ReleaseAsset(n, link, size));
                }
            }
        }

        return new Release(version, text, page, assets);
    }

    /// <summary>
    /// "1.2.3" or "1.2.3.4", padded to four parts so it compares cleanly with an assembly
    /// version, which always has four.
    /// </summary>
    public static Version? ParseVersion(string text) =>
        Version.TryParse(text, out var v) ? Normalize(v) : null;

    public static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
