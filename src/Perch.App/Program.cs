using Avalonia;

namespace Perch.App;

static class Program
{
    [STAThread]
    static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also what the Avalonia previewer uses; keep it free of side effects.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
