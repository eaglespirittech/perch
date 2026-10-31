using Perch.Bluetooth;

namespace Perch.Platform;

/// <summary>
/// Everything the app and the CLI need from the operating system, gathered in one place.
/// Each OS provides one implementation (Perch.Platform picks it at build time); the
/// rest of the code only ever sees this interface.
/// </summary>
public interface IPlatform
{
    /// <summary>"Windows" or "macOS", for messages.</summary>
    string Name { get; }

    IBluetooth Bluetooth { get; }

    IAutoStart AutoStart { get; }

    ISelfUpdate SelfUpdate { get; }

    /// <summary>Opens a folder in Explorer or Finder.</summary>
    void RevealFolder(string path);

    /// <summary>Opens a web page in the default browser.</summary>
    void OpenLink(string url);

    /// <summary>Where the app lives while its window is closed: "notification area" or "menu bar".</summary>
    string TrayName { get; }
}

/// <summary>Starting the app when the user signs in.</summary>
public interface IAutoStart
{
    /// <summary>The menu label, in the OS's own words: "Start with Windows", "Open at Login".</summary>
    string Label { get; }

    bool IsEnabled { get; }

    /// <summary>Returns the error to show the user, or null when it worked.</summary>
    string? SetEnabled(bool enabled);

    /// <summary>Keeps an existing registration pointing at wherever the app now lives.</summary>
    void RefreshIfEnabled();
}

/// <summary>
/// Replacing the running app with a newer release. <see cref="Updates.Updater"/> finds and
/// downloads the release; this is the part that differs by OS and by how Perch was installed.
/// </summary>
public interface ISelfUpdate
{
    /// <summary>
    /// The release file this copy updates from, such as "Perch-1.2.3-setup.exe", or null
    /// when it cannot replace itself: a development build, a copy a package manager looks
    /// after, or one in a folder this user cannot write to.
    /// </summary>
    string? AssetName(string version);

    /// <summary>
    /// Gets a downloaded release ready to install without touching the running app, and
    /// returns what <see cref="Apply"/> should install. Throws when the file is unusable.
    /// </summary>
    Task<string> PrepareAsync(string downloaded, CancellationToken ct);

    /// <summary>
    /// Installs what <see cref="PrepareAsync"/> returned and arranges for the new version to
    /// start once this process has gone. The caller quits straight afterwards. Throws, leaving
    /// the running app as it was, when it cannot.
    /// </summary>
    void Apply(string prepared);

    /// <summary>Removes whatever an earlier update left behind.</summary>
    void CleanUp();
}
