using System.Diagnostics;
using System.Runtime.InteropServices;
using Perch.Updates;

namespace Perch.Platform.MacOS;

/// <summary>
/// Updates Perch.app in place from the release's disk image: copies the new bundle out of
/// the image beside the old one, checks its signature, then swaps the two and reopens it.
/// macOS keeps a running app's files alive after they are replaced, as Unix does, so the
/// swap is safe while this copy is still running.
/// </summary>
public sealed class BundleSelfUpdate : ISelfUpdate
{
    const string Staged = ".Perch-update.app";
    const string Previous = ".Perch-previous.app";

    public string? AssetName(string version)
    {
        if (MacPlatform.BundlePath is not { } bundle || !CanWriteBeside(bundle)) return null;

        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return $"Perch-{version}-macos-{arch}.dmg";
    }

    public async Task<string> PrepareAsync(string downloaded, CancellationToken ct)
    {
        var bundle = MacPlatform.BundlePath ?? throw new InvalidOperationException("Perch is not running from Perch.app.");
        var staged = Path.Combine(Path.GetDirectoryName(bundle)!, Staged);
        var mount = Path.Combine(Path.GetTempPath(), $"perch-update-{Guid.NewGuid():N}");

        Delete(staged);
        Directory.CreateDirectory(mount);
        await RunAsync("/usr/bin/hdiutil", ct, "attach", "-nobrowse", "-noautoopen", "-readonly", "-mountpoint", mount, downloaded);
        try
        {
            var app = Directory.EnumerateDirectories(mount, "*.app").FirstOrDefault()
                      ?? throw new InvalidDataException("The disk image does not contain Perch.app.");
            await RunAsync("/usr/bin/ditto", ct, app, staged);
        }
        finally
        {
            await RunAsync("/usr/bin/hdiutil", CancellationToken.None, "detach", mount, "-force");
            try { Directory.Delete(mount); } catch { }
        }

        try
        {
            await RunAsync("/usr/bin/codesign", ct, "--verify", "--deep", "--strict", staged);
        }
        catch
        {
            Delete(staged);
            throw new InvalidDataException("The new Perch.app is not validly signed, so it was not installed.");
        }

        return staged;
    }

    public void Apply(string prepared)
    {
        var bundle = MacPlatform.BundlePath ?? throw new InvalidOperationException("Perch is not running from Perch.app.");
        var previous = Path.Combine(Path.GetDirectoryName(bundle)!, Previous);

        Delete(previous);
        Directory.Move(bundle, previous);
        try
        {
            Directory.Move(prepared, bundle);
        }
        catch
        {
            Directory.Move(previous, bundle);
            throw;
        }

        try { Delete(previous); } catch { }

        // -n: LaunchServices would otherwise just bring this copy, still running, forward.
        var open = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        foreach (var arg in new[] { "-n", "-a", bundle, "--args", Updater.WaitArgument, Environment.ProcessId.ToString() })
            open.ArgumentList.Add(arg);
        Process.Start(open)?.Dispose();
    }

    public void CleanUp()
    {
        if (MacPlatform.BundlePath is not { } bundle) return;
        var folder = Path.GetDirectoryName(bundle)!;
        Delete(Path.Combine(folder, Previous));
        Delete(Path.Combine(folder, Staged));
    }

    /// <summary>
    /// The swap renames the bundle within its folder, so that folder has to be writable;
    /// /Applications is for administrators, so a standard user gets told about updates instead.
    /// </summary>
    static bool CanWriteBeside(string bundle)
    {
        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(bundle)!, $".perch-write-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void Delete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    static async Task RunAsync(string tool, CancellationToken ct, params string[] args)
    {
        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not run {tool}.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        await Task.WhenAll(output, error);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(tool)} failed: {(await error).Trim()}");
    }
}
