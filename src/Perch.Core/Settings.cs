using System.Text.Json;
using Perch.Scheduling;

namespace Perch;

public sealed class Settings
{
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public double LastTarget { get; set; } = 72.0;
    public double Preset1 { get; set; } = 72.0;
    public double Preset2 { get; set; } = 110.0;
    public bool ScheduleEnabled { get; set; }

    /// <summary>Whether the "still running down here" hint has been shown once.</summary>
    public bool TrayHintShown { get; set; }
    public WeekSchedule Schedule { get; set; } = new();

    /// <summary>
    /// Where each OS expects per-user app data: %APPDATA%\Perch on Windows,
    /// ~/Library/Application Support/Perch on macOS, $XDG_CONFIG_HOME/perch elsewhere.
    /// </summary>
    public static string Folder { get; } = Bluetooth.SimulatedBluetooth.Requested ? RealFolder + " (simulator)" : RealFolder;

    static string RealFolder => OperatingSystem.IsWindows()
        ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Perch")
        : OperatingSystem.IsMacOS()
            // .NET maps LocalApplicationData to ~/Library/Application Support on macOS.
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Perch")
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "perch");

    static readonly string Path = System.IO.Path.Combine(Folder, "settings.json");

    /// <summary>Where settings lived before the app was called Perch. Read once, then left alone. Windows only.</summary>
    static readonly string? LegacyPath = OperatingSystem.IsWindows() && !Bluetooth.SimulatedBluetooth.Requested
        ? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IdasenDeskControl", "settings.json")
        : null;

    public static Settings Load()
    {
        foreach (var path in new[] { Path, LegacyPath })
        {
            try
            {
                if (path is null || !File.Exists(path)) continue;

                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
                if (loaded is null) continue;

                loaded.Schedule = (loaded.Schedule ?? new WeekSchedule()).Normalized();
                return loaded;
            }
            catch
            {
                // A corrupt settings file is not worth blocking startup over.
            }
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Losing presets is annoying, not fatal.
        }
    }
}
