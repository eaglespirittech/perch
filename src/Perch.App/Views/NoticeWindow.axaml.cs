using Avalonia.Controls;
using Avalonia.Interactivity;
using Perch.App.Controls;

namespace Perch.App.Views;

/// <summary>A small message box. Avalonia has none built in, and this one matches the app.</summary>
public sealed partial class NoticeWindow : Window
{
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

    void OnOk(object? sender, RoutedEventArgs e) => Close();
}
