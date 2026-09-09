using Perch.Ui;

namespace Perch;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        Theme.Load();
        Theme.Watch();
        ToolStripManager.Renderer = new FluentMenuRenderer();

        // If the exe has been moved since startup was switched on, point the entry here.
        AutoStart.RefreshPathIfEnabled();

        Application.Run(new MainForm());
    }
}
