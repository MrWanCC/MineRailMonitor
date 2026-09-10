using System.Windows.Controls;

namespace MineRailMonitor.Pages;

public partial class PlaceholderPage : UserControl
{
    public PlaceholderPage(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
    }
}
