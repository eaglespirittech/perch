using Avalonia.Controls;
using Avalonia.Interactivity;
using Perch.App.Controls;

namespace Perch.App.Views;

/// <summary>A small message box. Avalonia has none built in, and this one matches the app.</summary>
public sealed partial class NoticeWindow : Window
{
    readonly Action? _action;

    /// <summary>For the XAML previewer only.</summary>
    public NoticeWindow() : this("Perch", string.Empty)
    {
    }

    public NoticeWindow(string heading, string body)
    {
        InitializeComponent();
        Heading.Text = heading;
        Body.Text = body;
        Icon = AppIcon.WindowIcon();
    }

    /// <summary>A question: <paramref name="action"/> runs the thing, and the other button puts it off.</summary>
    public NoticeWindow(string heading, string body, string action, Action onAction, string dismiss = "Later")
        : this(heading, body)
    {
        _action = onAction;
        ActionButton.Content = action;
        ActionButton.IsVisible = true;
        ActionButton.IsDefault = true;
        OkButton.Content = dismiss;
        OkButton.IsDefault = false;
        OkButton.Classes.Remove("primary");
    }

    void OnOk(object? sender, RoutedEventArgs e) => Close();

    void OnAction(object? sender, RoutedEventArgs e)
    {
        Close();
        _action?.Invoke();
    }
}
