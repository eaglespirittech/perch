using System.Diagnostics;
using Perch.Bluetooth;

namespace Perch.Platform.Windows;

public sealed class WindowsPlatform : IPlatform
{
    public string Name => "Windows";
    public IBluetooth Bluetooth { get; } = new WinRtBluetooth();
    public IAutoStart AutoStart { get; } = new RunKeyAutoStart();
    public string TrayName => "notification area";

    public void RevealFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
}
