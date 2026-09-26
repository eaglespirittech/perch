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

    /// <summary>Opens a folder in Explorer or Finder.</summary>
    void RevealFolder(string path);

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
