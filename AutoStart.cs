using Microsoft.Win32;

namespace Perch;

/// <summary>
/// Start-with-Windows, via the per-user Run key. No admin rights, no scheduled task, and
/// the user can always see and revoke it in Task Manager's Startup tab.
/// </summary>
public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Perch";

    /// <summary>The exe as Windows should launch it. Null when running from a host such as dotnet.exe.</summary>
    public static string? ExePath
    {
        get
        {
            var path = Environment.ProcessPath;
            return string.IsNullOrEmpty(path) ? null : path;
        }
    }

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Returns the error to show the user, or null when it worked.</summary>
    public static string? SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return "Could not open the Windows startup settings.";

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return null;
            }

            if (ExePath is not { } exe) return "Could not work out which file to start.";
            key.SetValue(ValueName, $"\"{exe}\"");
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Keeps the registered path pointing at wherever the exe lives now, so moving it
    /// does not leave a dead startup entry behind.
    /// </summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            if (ExePath is not { } exe) return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not string current) return;

            var wanted = $"\"{exe}\"";
            if (!string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, wanted);
        }
        catch
        {
            // Startup registration is a convenience; never block launch over it.
        }
    }
}
