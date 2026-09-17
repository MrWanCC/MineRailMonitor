using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace MineRailMonitor.Pages;

public partial class YardCommunicationConfigDialog : Window
{
    private readonly List<YardCommunicationEditorRow> _rows;

    public YardCommunicationConfigDialog(
        IEnumerable<YardCommunicationEditorRow>? rows,
        bool isEditingEnabled)
    {
        _rows = (rows ?? Enumerable.Empty<YardCommunicationEditorRow>())
            .Select(CloneRow)
            .ToList();
        IsEditingEnabled = isEditingEnabled;
        InitializeComponent();
        YardCommunicationItemsControl.ItemsSource = _rows;
        ScopeText.Text = _rows.Count switch
        {
            0 => "当前筛选下暂无站场通信配置",
            1 => $"当前配置：{_rows[0].DisplayName}",
            _ => "当前配置：全部站场"
        };
        DialogHintText.Text = isEditingEnabled
            ? "保存后将应用于对应站场通信通道。"
            : "当前为查看模式，进入管理员模式后可修改。";
        SaveButton.Content = isEditingEnabled ? "保存配置" : "关闭";
    }

    public bool IsEditingEnabled { get; }

    public IReadOnlyList<YardCommunicationEditorRow> EditedRows => _rows;

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = IsEditingEnabled;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private static YardCommunicationEditorRow CloneRow(YardCommunicationEditorRow row) => new()
    {
        YardId = row.YardId,
        DisplayName = row.DisplayName,
        ListenIp = row.ListenIp,
        ListenPort = row.ListenPort,
        Enabled = row.Enabled
    };
}
