using System.Diagnostics;
using Microsoft.Win32;
using Perch.Updates;

namespace Perch.Platform.Windows;

/// <summary>
/// Updates whichever way Perch arrived. An installed copy (the installer, or WinGet, which
/// runs the same installer) runs the new installer silently, which closes this copy,
/// upgrades in place and starts Perch again. A portable exe swaps itself for the new one:
/// Windows lets a running exe be renamed, just not overwritten.
/// </summary>
public sealed class WindowsSelfUpdate : ISelfUpdate
{
    /// <summary>Where Inno Setup records the install; the GUID is the AppId in perch.iss.</summary>
    const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8E4B1C22-7F3A-4B6E-9C1D-2A5F0E7D3B91}_is1";

    enum Kind
    {
        None,
        Installed,
        Portable
    }

    readonly Lazy<Kind> _kind = new(Detect);

    public string? AssetName(string version) => _kind.Value switch
    {
        Kind.Installed => $"Perch-{version}-setup.exe",
        Kind.Portable => $"Perch-{version}-win-x64.exe",
        _ => null
    };

    public Task<string> PrepareAsync(string downloaded, CancellationToken ct) => Task.FromResult(downloaded);

    public void Apply(string prepared)
    {
        if (_kind.Value == Kind.Installed)
        {
            // /SILENT shows only a progress bar, so the few seconds without Perch do not look
            // like a crash. /RELAUNCH is perch.iss's own switch for starting Perch afterwards.
            var setup = new ProcessStartInfo(prepared) { UseShellExecute = false };
            foreach (var arg in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/RELAUNCH=1" })
                setup.ArgumentList.Add(arg);
            Process.Start(setup)?.Dispose();
            return;
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Could not work out where Perch is.");
        var old = exe + ".old";
        File.Delete(old);
        File.Move(exe, old);
        try
        {
            File.Move(prepared, exe);
        }
        catch
        {
            File.Move(old, exe);
            throw;
        }

        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        start.ArgumentList.Add(Updater.WaitArgument);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(start)?.Dispose();
    }

    /// <summary>The exe a portable update renamed out of the way; it can go once that copy has exited.</summary>
    public void CleanUp()
    {
        if (Environment.ProcessPath is { } exe && File.Exists(exe + ".old")) File.Delete(exe + ".old");
    }

    static Kind Detect()
    {
        var exe = Environment.ProcessPath;
        var folder = Path.GetDirectoryName(exe);
        if (string.IsNullOrEmpty(exe) || folder is null) return Kind.None;

        // Scoop keeps its own copy and its own idea of the version; it updates it itself.
        if (exe.Contains(@"\scoop\", StringComparison.OrdinalIgnoreCase)) return Kind.None;

        if (IsInstallFolder(folder)) return Kind.Installed;

        // A single-file exe has no assembly files beside it, which is how the portable
        // build differs from a development build run out of bin\.
        return string.IsNullOrEmpty(typeof(WindowsSelfUpdate).Assembly.Location) ? Kind.Portable : Kind.None;
    }

    static bool IsInstallFolder(string folder)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") is string location &&
                   string.Equals(Path.GetFullPath(location).TrimEnd('\\'), Path.GetFullPath(folder).TrimEnd('\\'),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
