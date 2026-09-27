using System.Security;

namespace Perch.Platform.MacOS;

/// <summary>
/// Open-at-login through a per-user LaunchAgent. No admin rights, and macOS lists it under
/// System Settings > General > Login Items, where it can also be switched off.
/// </summary>
public sealed class LaunchAgentAutoStart : IAutoStart
{
    const string Label_ = "com.eaglespirit.perch";

    static readonly string PlistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", $"{Label_}.plist");

    public string Label => "Open at Login";

    public bool IsEnabled => File.Exists(PlistPath);

    public string? SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                File.Delete(PlistPath);
                return null;
            }

            if (Plist() is not { } plist) return "Could not work out which app to start.";
            Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
            File.WriteAllText(PlistPath, plist);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Keeps the agent pointing at wherever Perch.app now lives, so moving it does not break login.</summary>
    public void RefreshIfEnabled()
    {
        try
        {
            if (!IsEnabled || Plist() is not { } wanted) return;
            if (File.ReadAllText(PlistPath) != wanted) File.WriteAllText(PlistPath, wanted);
        }
        catch
        {
            // Startup registration is a convenience; never block launch over it.
        }
    }

    /// <summary>
    /// Launches through "open" when running from the bundle, which is how Finder would do
    /// it; --minimized keeps sign-in from putting a window in your face.
    /// </summary>
    static string? Plist()
    {
        string[] arguments;
        if (MacPlatform.BundlePath is { } bundle)
            arguments = new[] { "/usr/bin/open", "-a", bundle, "--args", "--minimized" };
        else if (Environment.ProcessPath is { Length: > 0 } exe)
            arguments = new[] { exe, "--minimized" };
        else
            return null;

        var items = string.Concat(arguments.Select(a => $"\n        <string>{SecurityElement.Escape(a)}</string>"));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>{Label_}</string>
                <key>ProgramArguments</key>
                <array>{items}
                </array>
                <key>RunAtLoad</key>
                <true/>
                <key>ProcessType</key>
                <string>Interactive</string>
            </dict>
            </plist>

            """;
    }
}
