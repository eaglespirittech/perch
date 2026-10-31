using System.Diagnostics;
using Avalonia;
using Perch.Updates;

namespace Perch.App;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        WaitForPreviousCopy(args);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also what the Avalonia previewer uses; keep it free of side effects.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    /// <summary>
    /// Started by an update while the old copy is still quitting: let it release the desk
    /// and the control pipe first, rather than racing it for them.
    /// </summary>
    static void WaitForPreviousCopy(string[] args)
    {
        var index = Array.IndexOf(args, Updater.WaitArgument);
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var pid)) return;

        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch
        {
            // Already gone.
        }
    }
}
