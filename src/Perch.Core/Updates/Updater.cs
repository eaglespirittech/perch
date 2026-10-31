using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Perch.Platform;

namespace Perch.Updates;

public enum UpdateState
{
    /// <summary>Nothing checked yet, or switched off.</summary>
    Idle,
    Checking,
    UpToDate,
    Downloading,

    /// <summary>Downloaded, verified and prepared; restarting installs it.</summary>
    Ready,

    /// <summary>
    /// Newer, but not something this copy can install by itself: it cannot replace itself,
    /// or the release has no signed checksums. Either way it is the user's to fetch.
    /// </summary>
    Available,
    Failed
}

/// <summary>
/// Watches GitHub for a newer release of Perch, downloads it, checks its SHA-256 against the
/// release's signed checksum list (see <see cref="ReleaseSignature"/>) and hands it to the
/// platform to prepare. Restarting is the
/// user's call: <see cref="Apply"/> installs it once they agree, and the app quits.
///
/// Start it on the UI thread; its continuations, and so <see cref="Changed"/>, come back there.
/// </summary>
public sealed class Updater : IDisposable
{
    public const string ReleasesPage = "https://github.com/eaglespirittech/perch/releases";
    static readonly Uri LatestRelease = new("https://api.github.com/repos/eaglespirittech/perch/releases/latest");

    /// <summary>
    /// Passed to the new copy with this process's id, so it waits for the old one to let go
    /// of the desk and the control pipe before starting.
    /// </summary>
    public const string WaitArgument = "--after-update";

    /// <summary>The first check waits for start-up to settle; after that, a few times a day.</summary>
    static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);
    static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    readonly ISelfUpdate _platform;
    readonly HttpClient _http;
    readonly CancellationTokenSource _stopping = new();
    readonly string _folder = Path.Combine(Path.GetTempPath(), "Perch-update");
    string? _prepared;

    public Updater(ISelfUpdate platform, Version current)
    {
        _platform = platform;
        Current = Release.Normalize(current);

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Perch", Current.ToString(3)));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public Version Current { get; }

    /// <summary>A development build (version 0.0.0) never updates itself.</summary>
    public bool IsEnabled => Current > new Version(0, 0, 0, 0);

    public UpdateState State { get; private set; }

    /// <summary>The newest release seen, once a check has found one newer than this copy.</summary>
    public Release? Latest { get; private set; }

    public string? Error { get; private set; }

    public bool IsBusy => State is UpdateState.Checking or UpdateState.Downloading;

    public event Action? Changed;

    /// <summary>Checks now and then for as long as the app runs, whenever <paramref name="enabled"/> says so.</summary>
    public async Task RunAsync(Func<bool> enabled)
    {
        if (!IsEnabled) return;

        try
        {
            _platform.CleanUp();
        }
        catch
        {
            // Leftovers are untidy, not harmful; the next start tries again.
        }

        try
        {
            await Task.Delay(FirstCheck, _stopping.Token);
            while (!_stopping.IsCancellationRequested)
            {
                if (enabled()) await CheckAsync();
                await Task.Delay(Interval, _stopping.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Looks for a newer release and, when this copy can install it, downloads it.</summary>
    public async Task CheckAsync()
    {
        if (!IsEnabled || IsBusy || State == UpdateState.Ready) return;

        var ct = _stopping.Token;
        Set(UpdateState.Checking);
        try
        {
            var release = await FetchLatestAsync(ct);
            if (release is null || release.Version <= Current)
            {
                Set(UpdateState.UpToDate);
                return;
            }

            Latest = release;
            if (_platform.AssetName(release.VersionText) is not { } name ||
                release.Asset(name) is not { } asset ||
                release.Asset(ReleaseSignature.ChecksumsName) is not { } checksums ||
                release.Asset(ReleaseSignature.SignatureName) is not { } signature)
            {
                Set(UpdateState.Available);
                return;
            }

            Set(UpdateState.Downloading);
            var expected = await TrustedChecksumAsync(asset, checksums, signature, ct);
            var file = await DownloadAsync(release, asset, expected, ct);
            _prepared = await _platform.PrepareAsync(file, ct);
            Set(UpdateState.Ready);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Error = ex is HttpRequestException or TaskCanceledException
                ? "Could not reach GitHub."
                : ex.Message;
            Set(UpdateState.Failed);
        }
    }

    /// <summary>
    /// Installs the downloaded release and starts it once this process exits. Throws when it
    /// cannot, in which case the running app is untouched and should carry on.
    /// </summary>
    public void Apply()
    {
        if (State != UpdateState.Ready || _prepared is null)
            throw new InvalidOperationException("There is no update ready to install.");

        _platform.Apply(_prepared);
    }

    async Task<Release?> FetchLatestAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(LatestRelease, ct);

        // No published release at all yet.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        return Release.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>The asset's hash, from a checksum list proven to come from the release workflow.</summary>
    async Task<string> TrustedChecksumAsync(ReleaseAsset asset, ReleaseAsset checksums, ReleaseAsset signature,
        CancellationToken ct)
    {
        var list = await _http.GetByteArrayAsync(checksums.Url, ct);
        var signed = await _http.GetByteArrayAsync(signature.Url, ct);

        if (!ReleaseSignature.Verify(list, signed))
            throw new InvalidDataException("The release's checksums are not signed with Perch's release key, so it was not installed.");

        return ReleaseSignature.FindChecksum(Encoding.UTF8.GetString(list), asset.Name)
               ?? throw new InvalidDataException($"The release's signed checksums do not list {asset.Name}.");
    }

    async Task<string> DownloadAsync(Release release, ReleaseAsset asset, string expected, CancellationToken ct)
    {
        // One folder per version, and older ones cleared out, so the downloads never pile up.
        var folder = Path.Combine(_folder, release.VersionText);
        if (Directory.Exists(_folder))
        {
            foreach (var old in Directory.EnumerateDirectories(_folder))
            {
                if (old == folder) continue;
                try { Directory.Delete(old, recursive: true); } catch { }
            }
        }
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, asset.Name);
        if (File.Exists(path) && await HashAsync(path, ct) == expected) return path;

        var partial = path + ".part";
        using (var response = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(partial);
            await source.CopyToAsync(target, ct);
        }

        if (await HashAsync(partial, ct) != expected)
        {
            File.Delete(partial);
            throw new InvalidDataException($"{asset.Name} did not match its signed checksum, so it was not installed.");
        }

        File.Move(partial, path, overwrite: true);
        return path;
    }

    static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    void Set(UpdateState state)
    {
        State = state;
        if (state != UpdateState.Failed) Error = null;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
        _http.Dispose();
    }
}
