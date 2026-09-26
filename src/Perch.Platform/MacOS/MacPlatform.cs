using System.Diagnostics;
using Perch.Bluetooth;

namespace Perch.Platform.MacOS;

public sealed class MacPlatform : IPlatform
{
    public string Name => "macOS";
    public IBluetooth Bluetooth { get; } = new HelperBluetooth();
    public IAutoStart AutoStart { get; } = new LaunchAgentAutoStart();
    public string TrayName => "menu bar";

    public void RevealFolder(string path)
    {
        var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        start.ArgumentList.Add(path);
        Process.Start(start)?.Dispose();
    }

    /// <summary>
    /// The Perch.app bundle this process runs from, or null when it is not in one (a
    /// development build, or the CLI unpacked from its tarball).
    /// </summary>
    internal static string? BundlePath
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return null;

            // .../Perch.app/Contents/MacOS/Perch
            var macOs = Path.GetDirectoryName(exe);
            var contents = Path.GetDirectoryName(macOs);
            var bundle = Path.GetDirectoryName(contents);
            return bundle is not null &&
                   bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
                   Path.GetFileName(contents) == "Contents" &&
                   Path.GetFileName(macOs) == "MacOS"
                ? bundle
                : null;
        }
    }
}
