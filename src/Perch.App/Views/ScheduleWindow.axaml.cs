using Avalonia.Controls;
using Avalonia.Interactivity;
using Perch.App.Controls;
using Perch.App.ViewModels;
using Perch.Scheduling;

namespace Perch.App.Views;

/// <summary>
/// One row per weekday: switch it on, set the window it applies to, then the stand slot
/// inside each hour and the two heights. "Copy" clones a finished row onto the others.
/// Closes with the new <see cref="WeekSchedule"/>, or null when cancelled.
/// </summary>
public sealed partial class ScheduleWindow : Window
{
    readonly ScheduleViewModel _viewModel;

    /// <summary>For the XAML previewer only.</summary>
    public ScheduleWindow() : this(new WeekSchedule())
    {
    }

    public ScheduleWindow(WeekSchedule schedule)
    {
        InitializeComponent();
        _viewModel = new ScheduleViewModel(schedule);
        DataContext = _viewModel;
        Icon = AppIcon.WindowIcon();
    }

    void OnFieldLeft(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is DayRow row) row.Normalise();
    }

    void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DayRow source } anchor) return;

        var order = ScheduleViewModel.DisplayOrder;
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };

        void Add(string text, IEnumerable<DayOfWeek> targets)
        {
            var list = targets.Where(d => d != source.Day).ToArray();
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => _viewModel.CopyTo(source, list);
            menu.Items.Add(item);
        }

        Add("Copy to all other days", order);
        Add("Copy to weekdays (Mon-Fri)", order.Take(5));
        Add("Copy to weekend (Sat-Sun)", order.Skip(5));
        menu.Items.Add(new Separator());
        foreach (var day in order.Where(d => d != source.Day))
            Add($"Copy to {day}", new[] { day });

        menu.ShowAt(anchor);
    }

    void OnSave(object? sender, RoutedEventArgs e)
    {
        // Tidy whatever field still has focus before reading it.
        foreach (var row in _viewModel.Days) row.Normalise();

        if (_viewModel.TryBuild(out _) is { } schedule)
            Close(schedule);
    }

    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
