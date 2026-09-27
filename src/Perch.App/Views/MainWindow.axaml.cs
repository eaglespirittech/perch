using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Perch.App.Controls;
using Perch.App.Theme;
using Perch.App.ViewModels;
using Perch.Desk;
using Perch.Scheduling;

namespace Perch.App.Views;

public sealed partial class MainWindow : Window
{
    readonly MainViewModel? _viewModel;

    /// <summary>For the XAML previewer only.</summary>
    public MainWindow() => InitializeComponent();

    public MainWindow(MainViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        TargetBox.LostFocus += (_, _) => viewModel.NormaliseTarget();
        viewModel.Session.ConnectionChanged += PaintPill;
        Palette.Changed += OnPaletteChanged;
        OnPaletteChanged();
    }

    void OnPaletteChanged()
    {
        Icon = AppIcon.WindowIcon();
        HeaderIcon.Source = AppIcon.Draw(72);
        PaintPill();
    }

    /// <summary>The connection pill: a tinted capsule with a green, amber or grey dot.</summary>
    void PaintPill()
    {
        if (_viewModel is null) return;

        var p = Palette.Current;
        var dot = _viewModel.Session.Connection switch
        {
            ConnectionState.Online => p.Success,
            ConnectionState.Connecting => p.Caution,
            _ => p.TextSecondary
        };

        PillDot.Fill = new SolidColorBrush(dot);
        Pill.Background = new SolidColorBrush(Palette.Mix(p.Window, dot, p.IsDark ? 0.14 : 0.10));
    }

    // ---- menu ----------------------------------------------------------------

    void OnMenu(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } vm) return;
        var session = vm.Session;
        var platform = vm.Platform;

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };

        var desks = new MenuItem { Header = "Desk" };
        if (session.Devices.Count == 0)
            desks.Items.Add(new MenuItem { Header = "No desks found", IsEnabled = false });

        foreach (var device in session.Devices)
        {
            var item = new MenuItem
            {
                Header = device.Name,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = device.Id == session.Selected?.Id
            };
            item.Click += async (_, _) => await session.SelectDeviceAsync(device);
            desks.Items.Add(item);
        }

        desks.Items.Add(new Separator());
        desks.Items.Add(Item("Scan again", () => session.ScanAsync(autoConnect: false)));
        menu.Items.Add(desks);

        menu.Items.Add(Item(session.Desk.IsConnected ? "Disconnect" : "Connect", session.ToggleConnectionAsync));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Nudge up 1 cm", () => session.NudgeAsync(+1.0)));
        menu.Items.Add(Item("Nudge down 1 cm", () => session.NudgeAsync(-1.0)));
        menu.Items.Add(new Separator());

        var startup = new MenuItem
        {
            Header = platform.AutoStart.Label,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = platform.AutoStart.IsEnabled
        };
        startup.Click += (_, _) => ToggleAutoStart(!platform.AutoStart.IsEnabled);
        menu.Items.Add(startup);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open settings folder", OpenSettingsFolder));
        menu.Items.Add(Item("Quit Perch", () => (Avalonia.Application.Current as App)?.Exit()));

        menu.ShowAt(MenuButton);
    }

    static MenuItem Item(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    void ToggleAutoStart(bool enabled)
    {
        var vm = _viewModel!;
        var error = vm.Platform.AutoStart.SetEnabled(enabled);
        vm.Session.SetStatus(error is not null
            ? $"Could not change the startup setting: {error}"
            : enabled
                ? $"Perch will start when you sign in to {vm.Platform.Name}."
                : "Perch will no longer start automatically.");
    }

    void OpenSettingsFolder()
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            _viewModel!.Platform.RevealFolder(Settings.Folder);
        }
        catch (Exception ex)
        {
            _viewModel!.Session.SetStatus(ex.Message);
        }
    }

    async void OnEditSchedule(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } vm) return;

        var dialog = new ScheduleWindow(vm.Session.Settings.Schedule);
        if (await dialog.ShowDialog<WeekSchedule?>(this) is { } schedule)
            vm.SetSchedule(schedule);
    }
}
