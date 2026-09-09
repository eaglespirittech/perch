using Perch.Ui;

namespace Perch;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        Theme.Load();
        Theme.Watch();
        ToolStripManager.Renderer = new FluentMenuRenderer();

        // If the exe has been moved since startup was switched on, point the entry here.
        AutoStart.RefreshPathIfEnabled();

        // Windows launches us with --minimized at sign-in, so the app starts in the
        // notification area instead of putting a window in your face.
        var hidden = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        Application.Run(new MainForm(hidden));
    }
}
