using System.Windows;
using System.Windows.Input;

namespace MineRailMonitor.Pages;

public enum UnsavedSettingsChangesResult
{
    Cancel,
    SaveAndContinue,
    Discard
}

public partial class UnsavedSettingsChangesDialog : Window
{
    public UnsavedSettingsChangesDialog()
    {
        InitializeComponent();
    }

    public UnsavedSettingsChangesResult Result { get; private set; } = UnsavedSettingsChangesResult.Cancel;

    public void SetReason(string reason)
    {
        MessageText.Text = string.IsNullOrWhiteSpace(reason)
            ? "系统设置存在未保存修改，是否保存？"
            : $"系统设置存在未保存修改。\n当前操作：{reason}。";
    }

    private void OnSaveAndContinueClick(object sender, RoutedEventArgs e)
    {
        Result = UnsavedSettingsChangesResult.SaveAndContinue;
        DialogResult = true;
    }

    private void OnDiscardClick(object sender, RoutedEventArgs e)
    {
        Result = UnsavedSettingsChangesResult.Discard;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = UnsavedSettingsChangesResult.Cancel;
        DialogResult = false;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
