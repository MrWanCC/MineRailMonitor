using System.Windows;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Pages;

public partial class AdminPasswordDialog : Window
{
    private readonly AdminModeService _adminModeService;

    public AdminPasswordDialog(AdminModeService adminModeService)
    {
        _adminModeService = adminModeService ?? throw new ArgumentNullException(nameof(adminModeService));
        InitializeComponent();
    }

    public string Password => PasswordInput.Password;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_adminModeService.EnterAdminMode(Password))
        {
            DialogResult = true;
            return;
        }

        ErrorText.Visibility = Visibility.Visible;
        PasswordInput.SelectAll();
        PasswordInput.Focus();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
