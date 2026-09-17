using System.ComponentModel;

namespace MineRailMonitor.Core.Services;

public sealed class CurrentYardContext : INotifyPropertyChanged
{
    private string? _currentYardId;

    public CurrentYardContext(string? initialYardId = null)
    {
        _currentYardId = string.IsNullOrWhiteSpace(initialYardId) ? null : initialYardId!.Trim();
    }

    public string? CurrentYardId => _currentYardId;

    public bool IsGlobalOverview => _currentYardId is null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SelectYard(string yardId)
    {
        if (string.IsNullOrWhiteSpace(yardId))
        {
            throw new ArgumentException("站场编号不能为空。", nameof(yardId));
        }

        var normalizedYardId = yardId.Trim();
        if (string.Equals(_currentYardId, normalizedYardId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var wasGlobal = IsGlobalOverview;
        _currentYardId = normalizedYardId;
        OnPropertyChanged(nameof(CurrentYardId));
        if (wasGlobal)
        {
            OnPropertyChanged(nameof(IsGlobalOverview));
        }
    }

    public void SelectGlobal()
    {
        if (_currentYardId is null)
        {
            return;
        }

        _currentYardId = null;
        OnPropertyChanged(nameof(CurrentYardId));
        OnPropertyChanged(nameof(IsGlobalOverview));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
