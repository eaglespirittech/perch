using System.IO;
using System.Text.Json;

namespace Perch;

public sealed class Settings
{
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public double LastTarget { get; set; } = 72.0;
    public double Preset1 { get; set; } = 72.0;
    public double Preset2 { get; set; } = 110.0;
    public bool ScheduleEnabled { get; set; }
    public WeekSchedule Schedule { get; set; } = new();

    static string PathIn(string folder) => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        folder,
        "settings.json");

    static readonly string Path = PathIn("Perch");

    /// <summary>The folder holding settings.json, for the "open settings folder" menu item.</summary>
    public static string Folder => System.IO.Path.GetDirectoryName(Path)!;

    /// <summary>Where settings lived before the app was called Perch. Read once, then left alone.</summary>
    static readonly string LegacyPath = PathIn("IdasenDeskControl");

    public static Settings Load()
    {
        foreach (var path in new[] { Path, LegacyPath })
        {
            try
            {
                if (!File.Exists(path)) continue;

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
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Losing presets is annoying, not fatal.
        }
    }
}
