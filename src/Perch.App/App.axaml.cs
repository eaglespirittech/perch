using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Perch.App.Controls;
using Perch.App.Theme;
using Perch.App.ViewModels;
using Perch.App.Views;
using Perch.Desk;
using Perch.Ipc;
using Perch.Platform;

namespace Perch.App;

/// <summary>
/// The composition root: builds the shared session on this OS's platform services, then
/// the window and the tray (Windows) or menu bar (macOS) icon around it.
/// </summary>
public sealed class App : Application
{
    IClassicDesktopStyleApplicationLifetime? _desktop;
    MainViewModel? _viewModel;
    MainWindow? _window;
    ControlServer? _control;
    TrayIcon? _tray;
    bool _exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Before any window: every style refers to the palette's brushes.
        Palette.Initialize(this);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            Start(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // DeskSession captures this to run everything on the UI thread.
        AvaloniaSynchronizationContext.InstallIfNeeded();

        var platform = PlatformServices.Current;

        // If the app has moved since start-at-login was switched on, point it here.
        platform.AutoStart.RefreshIfEnabled();

        var session = new DeskSession(platform.Bluetooth, Settings.Load());
        _viewModel = new MainViewModel(session, platform);

        // While the app runs it owns the desk's single Bluetooth connection, so it
        // answers on behalf of perch-cli rather than making the CLI fight it for one.
        _control = new ControlServer(session.HandleControlAsync);

        _window = new MainWindow(_viewModel);
        _window.Closing += OnWindowClosing;

        // Closing the window parks Perch in the tray; only Exit ends it.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.Exit += (_, _) => Shutdown();

        // At sign-in the app is started with --minimized, and waits by the clock instead
        // of putting a window in your face.
        var hidden = desktop.Args?.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)) == true;
        if (!hidden) desktop.MainWindow = _window;

        BuildTrayIcon();

        // macOS: clicking the Dock icon with no window open should bring it back.
        if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen) ShowWindow();
            };

        Dispatcher.UIThread.Post(async () => await session.StartAsync(), DispatcherPriority.Background);
    }

    // ---- tray / menu bar -----------------------------------------------------

    void BuildTrayIcon()
    {
        var open = new NativeMenuItem("Open Perch");
        open.Click += (_, _) => ShowWindow();
        var exit = new NativeMenuItem("Quit Perch");
        exit.Click += (_, _) => Exit();

        _tray = new TrayIcon
        {
            ToolTipText = "Perch",
            Menu = new NativeMenu { Items = { open, new NativeMenuItemSeparator(), exit } }
        };
        _tray.Clicked += (_, _) => ShowWindow();

        UpdateTrayIcon();
        Palette.Changed += UpdateTrayIcon;

        _viewModel!.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.TrayText)) _tray.ToolTipText = _viewModel.TrayText;
        };

        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    void UpdateTrayIcon()
    {
        if (_tray is null) return;

        if (OperatingSystem.IsMacOS())
        {
            // A template image, which macOS tints to match the menu bar.
            using var bitmap = AppIcon.DrawTemplate(36);
            _tray.Icon = AppIcon.ToWindowIcon(bitmap);
            MacOSProperties.SetIsTemplateIcon(_tray, true);
        }
        else
        {
            _tray.Icon = AppIcon.WindowIcon(32);
        }
    }

    public void ShowWindow()
    {
        if (_window is null) return;
        _desktop!.MainWindow ??= _window;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Exit()
    {
        _exiting = true;
        _desktop?.Shutdown();
    }

    void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Closing the window parks the app in the tray rather than quitting, because the
        // schedule only runs while it does. Quit is on the tray menu and the window menu.
        if (_exiting || e.CloseReason != WindowCloseReason.WindowClosing || e.IsProgrammatic) return;

        e.Cancel = true;
        _window!.Hide();
        _viewModel!.Persist();

        var settings = _viewModel.Session.Settings;
        if (settings.TrayHintShown) return;
        settings.TrayHintShown = true;
        settings.Save();

        var where = _viewModel.Platform.TrayName;
        new NoticeWindow(
            "Perch is still running",
            $"It waits in the {where} so the schedule keeps working. Use the icon there to open it again, " +
            "or to quit.").Show();
    }

    void Shutdown()
    {
        _viewModel?.Persist();
        _viewModel?.Session.Dispose();
        _control?.Dispose();
        _tray?.Dispose();
    }

    void OnAbout(object? sender, EventArgs e)
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        new NoticeWindow($"Perch {version}",
            "Sit/stand desk control for the IKEA IDÅSEN.\nhttps://github.com/eaglespirittech/perch").Show();
    }
}
