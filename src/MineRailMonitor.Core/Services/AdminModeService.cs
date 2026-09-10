using System.ComponentModel;

namespace MineRailMonitor.Core.Services;

public sealed class AdminModeService : INotifyPropertyChanged
{
    private readonly string? _adminPassword;
    private bool _isAdmin;

    public AdminModeService(string? adminPassword = null)
    {
        _adminPassword = string.IsNullOrWhiteSpace(adminPassword) ? null : adminPassword;
    }

    public bool IsAdmin => _isAdmin;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool EnterAdminMode(string password)
    {
        if (_adminPassword is null || password != _adminPassword)
        {
            return false;
        }

        if (!_isAdmin)
        {
            _isAdmin = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAdmin)));
        }

        return true;
    }

    public void ExitAdminMode()
    {
        if (!_isAdmin)
        {
            return;
        }

        _isAdmin = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAdmin)));
    }
}
