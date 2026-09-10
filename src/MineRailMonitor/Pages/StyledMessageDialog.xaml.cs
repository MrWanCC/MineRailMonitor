using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MineRailMonitor.Pages;

public enum MessageDialogKind
{
    Information,
    Warning,
    Error
}

public partial class StyledMessageDialog : Window
{
    public StyledMessageDialog(string title, string message, MessageDialogKind kind)
    {
        InitializeComponent();
        DialogTitleText.Text = string.IsNullOrWhiteSpace(title) ? "提示" : title;
        MessageText.Text = string.IsNullOrWhiteSpace(message) ? "操作未返回详细信息。" : message;
        ApplyKind(kind);
    }

    private void ApplyKind(MessageDialogKind kind)
    {
        var accent = kind switch
        {
            MessageDialogKind.Error => Color.FromRgb(255, 135, 149),
            MessageDialogKind.Warning => Color.FromRgb(255, 226, 138),
            _ => Color.FromRgb(125, 205, 255)
        };
        var background = kind switch
        {
            MessageDialogKind.Error => Color.FromArgb(150, 91, 25, 39),
            MessageDialogKind.Warning => Color.FromArgb(150, 91, 72, 18),
            _ => Color.FromArgb(150, 18, 64, 94)
        };

        IconText.Text = kind == MessageDialogKind.Information ? "i" : "!";
        IconText.Foreground = new SolidColorBrush(accent);
        IconBorder.Background = new SolidColorBrush(background);
        IconBorder.BorderBrush = new SolidColorBrush(accent);
        DialogBorder.BorderBrush = new SolidColorBrush(accent);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
