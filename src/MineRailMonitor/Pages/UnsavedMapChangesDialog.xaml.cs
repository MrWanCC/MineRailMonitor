using System.Windows;

namespace MineRailMonitor.Pages;

public enum UnsavedMapChangesResult
{
    Cancel,
    SaveAndExit,
    Discard
}

public partial class UnsavedMapChangesDialog : Window
{
    public UnsavedMapChangesDialog()
    {
        InitializeComponent();
    }

    public UnsavedMapChangesResult Result { get; private set; } = UnsavedMapChangesResult.Cancel;

    public void SetReason(string reason)
    {
        MessageText.Text = string.IsNullOrWhiteSpace(reason)
            ? "地图标注存在未保存修改。"
            : $"地图标注存在未保存修改。\n当前操作：{reason}。";
    }

    private void OnSaveAndExitClick(object sender, RoutedEventArgs e)
    {
        Result = UnsavedMapChangesResult.SaveAndExit;
        DialogResult = true;
    }

    private void OnDiscardClick(object sender, RoutedEventArgs e)
    {
        Result = UnsavedMapChangesResult.Discard;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = UnsavedMapChangesResult.Cancel;
        DialogResult = false;
    }
}
