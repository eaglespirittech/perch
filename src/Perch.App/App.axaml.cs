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
using Perch.Updates;

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
    NativeMenuItem? _trayUpdate;
    Updater? _updater;

    /// <summary>The release last brought to the user's attention, so each one is mentioned once.</summary>
    string? _announced;
    bool _exiting;

    public Updater? Updater => _updater;

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

        _updater = new Updater(platform.SelfUpdate, typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0));
        _updater.Changed += OnUpdateChanged;

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

        Dispatcher.UIThread.Post(async () =>
        {
            await session.StartAsync();
            ReportFinishedUpdate();
            await _updater.RunAsync(() => session.Settings.CheckForUpdates);
        }, DispatcherPriority.Background);
    }

    // ---- tray / menu bar -----------------------------------------------------

    void BuildTrayIcon()
    {
        var open = new NativeMenuItem("Open Perch");
        open.Click += (_, _) => ShowWindow();
        _trayUpdate = new NativeMenuItem("Restart to update") { IsVisible = false };
        _trayUpdate.Click += (_, _) => RestartToUpdate();
        var exit = new NativeMenuItem("Quit Perch");
        exit.Click += (_, _) => Exit();

        _tray = new TrayIcon
        {
            ToolTipText = "Perch",
            Menu = new NativeMenu { Items = { open, _trayUpdate, new NativeMenuItemSeparator(), exit } }
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

    // ---- updates -------------------------------------------------------------

    void OnUpdateChanged()
    {
        var updater = _updater!;
        var session = _viewModel!.Session;
        var version = updater.Latest?.VersionText;

        if (_trayUpdate is not null)
        {
            _trayUpdate.IsVisible = updater.State == UpdateState.Ready;
            _trayUpdate.Header = $"Restart to update to {version}";
        }

        switch (updater.State)
        {
            case UpdateState.Downloading:
                session.SetStatus($"Downloading Perch {version}...");
                break;

            case UpdateState.Ready:
                session.SetStatus($"Perch {version} is ready. Restart Perch to finish updating.");
                if (_announced != version)
                {
                    _announced = version;
                    PromptRestart();
                }
                break;

            case UpdateState.Available:
                session.SetStatus($"Perch {version} is available.");
                if (_announced != version)
                {
                    _announced = version;
                    ShowAvailable();
                }
                break;
        }
    }

    void PromptRestart()
    {
        var version = _updater?.Latest?.VersionText;
        new NoticeWindow(
            $"Perch {version} is ready",
            $"The update has been downloaded. Restart Perch to start using {version}; it takes a few seconds " +
            "and Perch comes straight back.",
            "Restart now", RestartToUpdate).Show();
    }

    void ShowAvailable()
    {
        if (_updater?.Latest is not { } release) return;
        new NoticeWindow(
            $"Perch {release.VersionText} is available",
            "This copy of Perch cannot update itself, because of where or how it was installed. " +
            "The new version is on the releases page.",
            "Open releases page", () => _viewModel!.Platform.OpenLink(release.Page.ToString())).Show();
    }

    /// <summary>The window menu's "Check for updates", which, unlike the background check, always answers.</summary>
    public async Task CheckForUpdatesAsync()
    {
        if (_updater is not { } updater) return;
        var session = _viewModel!.Session;

        if (!updater.IsEnabled)
        {
            new NoticeWindow("Updates", "This is a development build, which does not update itself.").Show();
            return;
        }

        switch (updater.State)
        {
            case UpdateState.Ready:
                PromptRestart();
                return;
            case UpdateState.Checking or UpdateState.Downloading:
                return;
        }

        // Asked for, so say it again even if it was mentioned before.
        _announced = null;
        session.SetStatus("Checking for updates...");
        await updater.CheckAsync();

        switch (updater.State)
        {
            case UpdateState.UpToDate:
                session.SetStatus("Perch is up to date.");
                new NoticeWindow("Perch is up to date", $"Perch {updater.Current.ToString(3)} is the latest release.").Show();
                break;

            case UpdateState.Failed:
                session.SetStatus("Could not check for updates.");
                new NoticeWindow("Could not check for updates", updater.Error ?? "Something went wrong.").Show();
                break;
        }
    }

    public void RestartToUpdate()
    {
        if (_updater is not { State: UpdateState.Ready, Latest: { } release } updater) return;
        var session = _viewModel!.Session;

        if (session.IsBusy)
        {
            session.SetStatus("The desk is moving. Restart to update once it has stopped.");
            return;
        }

        session.Settings.PendingUpdate = release.VersionText;
        _viewModel.Persist();

        try
        {
            updater.Apply();
        }
        catch (Exception ex)
        {
            session.Settings.PendingUpdate = null;
            session.Settings.Save();
            new NoticeWindow("Could not install the update", ex.Message).Show();
            return;
        }

        Exit();
    }

    /// <summary>After quitting for an update, says whether the new version is the one now running.</summary>
    void ReportFinishedUpdate()
    {
        var settings = _viewModel!.Session.Settings;
        if (settings.PendingUpdate is not { } pending) return;

        settings.PendingUpdate = null;
        settings.Save();

        if (Release.ParseVersion(pending) == _updater!.Current)
            _viewModel.Session.SetStatus($"Updated to Perch {pending}.");
        else
            new NoticeWindow("The update did not install",
                $"Perch is still {_updater.Current.ToString(3)}, not {pending}. It will try again later, or you can " +
                "get the new version from the releases page.").Show();
    }

    void Shutdown()
    {
        _updater?.Dispose();
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
