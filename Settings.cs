using System.IO;
using System.Text.Json;

namespace IdasenDeskControl;

public sealed class Settings
{
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public double LastTarget { get; set; } = 72.0;
    public double Preset1 { get; set; } = 72.0;
    public double Preset2 { get; set; } = 110.0;
    public bool ScheduleEnabled { get; set; }
    public WeekSchedule Schedule { get; set; } = new();

    static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IdasenDeskControl",
        "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path));
                if (loaded is not null)
                {
                    loaded.Schedule = (loaded.Schedule ?? new WeekSchedule()).Normalized();
                    return loaded;
                }
            }
        }
        catch
        {
            // A corrupt settings file is not worth blocking startup over.
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
