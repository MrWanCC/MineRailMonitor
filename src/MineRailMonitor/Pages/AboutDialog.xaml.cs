using System.Windows;
using System.Windows.Input;

namespace MineRailMonitor.Pages;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
    }

    public string DisplayVersion =>
        $"v{typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

}
