using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MineRailMonitor.Pages;

public partial class RfidBindingDialog : Window
{
    public RfidBindingDialog(string rfidStationId, IEnumerable<RfidStationBindingOption> options)
    {
        InitializeComponent();
        TitleText.Text = $"为 {rfidStationId} 选择地图点位";
        PointComboBox.ItemsSource = options?.ToArray() ?? Array.Empty<RfidStationBindingOption>();
        PointComboBox.SelectedIndex = PointComboBox.Items.Count == 0 ? -1 : 0;
    }

    public RfidStationBindingOption? SelectedOption => PointComboBox.SelectedItem as RfidStationBindingOption;

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (SelectedOption is null)
        {
            return;
        }

        DialogResult = true;
    }
}
