using Perch.Bluetooth;

namespace Perch.Platform;

/// <summary>The composition root: the one place that knows which OS it was built for.</summary>
public static class PlatformServices
{
    /// <summary>
    /// The platform for this build. PERCH_SIMULATOR=1 swaps in a pretend desk, which is how
    /// to work on the UI or the CLI without one.
    /// </summary>
    public static IPlatform Current { get; } = Create();

    public static bool IsSimulated => SimulatedBluetooth.Requested;

    static IPlatform Create()
    {
#if WINDOWS
        IPlatform platform = new Windows.WindowsPlatform();
#else
        IPlatform platform = OperatingSystem.IsMacOS()
            ? new MacOS.MacPlatform()
            : throw new PlatformNotSupportedException("Perch runs on Windows and macOS.");
#endif
        return IsSimulated ? new Simulated(platform) : platform;
    }

    /// <summary>The real platform in every respect except the desk.</summary>
    sealed class Simulated(IPlatform inner) : IPlatform
    {
        public string Name => inner.Name;
        public IBluetooth Bluetooth { get; } = new SimulatedBluetooth();
        public IAutoStart AutoStart => inner.AutoStart;
        public string TrayName => inner.TrayName;
        public void RevealFolder(string path) => inner.RevealFolder(path);
    }
}
