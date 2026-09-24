using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Pages;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private readonly RfidSettings _settings;
    private readonly AdminModeService _adminModeService;
    private readonly bool _stationsEditable;
    private readonly Func<string, bool> _isRfidStationBound;
    private readonly IReadOnlyList<RfidStationConfig> _configuredRfidStations;
    private readonly IReadOnlyList<StationConfig> _yardConfigs;
    private readonly Func<StationConfig, Task<bool>>? _bindingSaveRequested;
    private readonly Action<string, string>? _viewMapPointRequested;
    private readonly ObservableCollection<YardCommunicationEditorRow> _yardCommunicationRows = new();
    private readonly IReadOnlyList<string> _stationIdOptions;
    private readonly IReadOnlyList<RfidStationYardOption> _yardOptions;
    private readonly IReadOnlyList<RfidStationYardOption> _yardFilterOptions;
    private readonly ObservableCollection<RfidStationEditorRow> _stationRows = new();
    private readonly ObservableCollection<RfidStationEditorRow> _visibleStationRows = new();
    private readonly ObservableCollection<YardCommunicationEditorRow> _visibleYardCommunicationRows = new();
    private readonly IReadOnlyList<string> _configurationErrors;
    private bool _isStationListRefreshInProgress;
    private SettingsDraft _savedDraft = null!;

    public SettingsPage(
        RfidSettings settings,
        AdminModeService adminModeService,
        IEnumerable<RfidStationConfig>? stations = null,
        bool stationsEditable = true,
        Func<string, bool>? isRfidStationBound = null,
        IEnumerable<StationConfig>? yards = null,
        Func<StationConfig, Task<bool>>? bindingSaveRequested = null,
        Action<string, string>? viewMapPointRequested = null,
        IEnumerable<YardCommunicationConfig>? yardCommunications = null,
        IEnumerable<YardAlarmForwardConfig>? yardAlarmForwards = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _adminModeService = adminModeService ?? throw new ArgumentNullException(nameof(adminModeService));
        _stationsEditable = stationsEditable;
        _isRfidStationBound = isRfidStationBound ?? (_ => false);
        _bindingSaveRequested = bindingSaveRequested;
        _viewMapPointRequested = viewMapPointRequested;
        InitializeComponent();
        PollIntervalTextBox.Text = settings.PollIntervalMs.ToString(CultureInfo.InvariantCulture);
        ExpectedVehicleCountTextBox.Text = settings.ExpectedVehicleCount.ToString(CultureInfo.InvariantCulture);
        InterVehicleTimeoutTextBox.Text = settings.InterVehicleTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        var configuredStations = (stations ?? Array.Empty<RfidStationConfig>()).ToArray();
        _configuredRfidStations = configuredStations;
        _yardConfigs = (yards ?? Array.Empty<StationConfig>())
            .Where(item => item is not null)
            .ToArray();
        _stationIdOptions = BuildStationIdOptions(configuredStations);
        _yardOptions = BuildYardOptions(_yardConfigs);
        _yardFilterOptions = BuildYardFilterOptions(_yardOptions);
        foreach (var row in BuildYardCommunicationRows(
                     yardCommunications ?? Array.Empty<YardCommunicationConfig>(),
                     yardAlarmForwards ?? Array.Empty<YardAlarmForwardConfig>(),
                     _yardConfigs))
        {
            _yardCommunicationRows.Add(row);
        }
        _configurationErrors = RfidStationConfigurationRules
            .FindDuplicateStationIds(configuredStations)
            .Select(stationId => $"RFID基站编号重复：{stationId}。")
            .ToArray();
        ViewRangeComboBox.ItemsSource = _yardFilterOptions;
        ViewRangeComboBox.SelectedItem = _yardFilterOptions[0];
        // LoadAsync rejects duplicate IDs. This first-by-ID projection is a
        // defensive UI boundary for callers that construct SettingsPage
        // directly; the diagnostic remains visible and blocks saving.
        foreach (var station in RfidStationConfigurationRules.TakeFirstByStationId(configuredStations))
        {
            var row = RfidStationEditorRow.FromConfig(station, _stationIdOptions, _yardOptions);
            row.DisplayIndex = _stationRows.Count + 1;
            _stationRows.Add(row);
        }

        StationsItemsControl.ItemsSource = _visibleStationRows;
        RefreshBindingOverview();
        _savedDraft = CaptureDraft();
        _adminModeService.PropertyChanged += OnAdminModePropertyChanged;
        UpdateStationEditorState();
        if (_configurationErrors.Count > 0)
        {
            SetSaveResult("项目配置错误：" + string.Join(Environment.NewLine, _configurationErrors), true);
        }
    }

    public event Func<RfidSettings, Task<bool>>? SaveRequested;

    public event Func<IReadOnlyList<RfidStationConfig>, Task<bool>>? StationsSaveRequested;

    public event Func<IReadOnlyList<YardCommunicationConfig>, Task<bool>>? SaveYardCommunicationsRequested;

    public event Func<IReadOnlyList<YardAlarmForwardConfig>, Task<bool>>? SaveYardAlarmForwardsRequested;

    public static readonly DependencyProperty BindingEditingEnabledProperty =
        DependencyProperty.Register(nameof(BindingEditingEnabled), typeof(bool), typeof(SettingsPage), new PropertyMetadata(false));

    public bool BindingEditingEnabled
    {
        get => (bool)GetValue(BindingEditingEnabledProperty);
        private set => SetValue(BindingEditingEnabledProperty, value);
    }

    public static readonly DependencyProperty StationEditingEnabledProperty =
        DependencyProperty.Register(nameof(StationEditingEnabled), typeof(bool), typeof(SettingsPage), new PropertyMetadata(false));

    public bool StationEditingEnabled
    {
        get => (bool)GetValue(StationEditingEnabledProperty);
        private set => SetValue(StationEditingEnabledProperty, value);
    }

    public static readonly DependencyProperty CanAddStationProperty =
        DependencyProperty.Register(nameof(CanAddStation), typeof(bool), typeof(SettingsPage), new PropertyMetadata(false));

    public bool CanAddStation
    {
        get => (bool)GetValue(CanAddStationProperty);
        private set => SetValue(CanAddStationProperty, value);
    }

    public static readonly DependencyProperty RfidSettingsEditingEnabledProperty =
        DependencyProperty.Register(nameof(RfidSettingsEditingEnabled), typeof(bool), typeof(SettingsPage), new PropertyMetadata(false));

    public bool RfidSettingsEditingEnabled
    {
        get => (bool)GetValue(RfidSettingsEditingEnabledProperty);
        private set => SetValue(RfidSettingsEditingEnabledProperty, value);
    }

    public static readonly DependencyProperty YardCommunicationEditingEnabledProperty =
        DependencyProperty.Register(nameof(YardCommunicationEditingEnabled), typeof(bool), typeof(SettingsPage), new PropertyMetadata(false));

    public bool YardCommunicationEditingEnabled
    {
        get => (bool)GetValue(YardCommunicationEditingEnabledProperty);
        private set => SetValue(YardCommunicationEditingEnabledProperty, value);
    }

    public bool HasUnsavedChanges => !AreDraftsEqual(_savedDraft, CaptureDraft());

    public void RefreshBindingOverview()
    {
        if (_isStationListRefreshInProgress)
        {
            return;
        }

        _isStationListRefreshInProgress = true;
        try
        {
            foreach (var row in _stationRows)
            {
                var bindings = RfidStationBindingRules.FindBindings(_yardConfigs, row.StationId);
                var ownedYardId = NormalizeYardId(row.YardId);
                var conflictingBindings = ownedYardId is null
                    ? Array.Empty<RfidStationBindingLocation>()
                    : bindings.Where(binding => !string.Equals(
                        binding.YardId,
                        ownedYardId,
                        StringComparison.OrdinalIgnoreCase)).ToArray();
                row.HasYardBindingConflict = conflictingBindings.Length > 0;
                row.YardDisplayName = GetYardDisplayName(row.YardId);
                row.BindingStatusText = conflictingBindings.Length > 0
                    ? "⚠ 归属冲突"
                    : bindings.Count switch
                    {
                        0 => "未绑定",
                        1 => "已绑定",
                        _ => $"重复绑定（{bindings.Count}）"
                    };
                row.BindingDetailText = bindings.Count == 0
                    ? string.Empty
                    : string.Join("、", bindings.Select(binding => binding.DisplayName));
                row.BindingFullDetails = BuildBindingFullDetails(row, bindings, conflictingBindings);
                row.BindingDisplay = row.BindingStatusText;
                row.BindingLocations = bindings.Count == 0
                    ? string.Empty
                    : string.Join("、", bindings.Select(binding => binding.DisplayName));
                row.HasBinding = bindings.Count > 0;
                row.HasDuplicateBinding = bindings.Count > 1;
            }

            RebuildVisibleStationRows();
            RebuildVisibleYardCommunicationRows();
            StationsItemsControl.Items.Refresh();
        }
        finally
        {
            _isStationListRefreshInProgress = false;
        }
    }

    public void SetSelectedYard(string? yardId)
    {
        var selectedYardId = string.IsNullOrWhiteSpace(yardId)
            ? RfidStationYardOption.AllId
            : yardId!.Trim();
        var selectedOption = _yardFilterOptions.FirstOrDefault(option =>
                string.Equals(option.Id, selectedYardId, StringComparison.OrdinalIgnoreCase))
            ?? _yardFilterOptions.FirstOrDefault(option =>
                string.Equals(option.Id, RfidStationYardOption.AllId, StringComparison.OrdinalIgnoreCase));

        if (selectedOption is null)
        {
            return;
        }

        ViewRangeComboBox.SelectedItem = selectedOption;
        UpdateStationEditorState();
        ApplyStationFilter();
    }

    public async Task<bool> TryLeaveAsync(string reason)
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var dialog = new UnsavedSettingsChangesDialog
        {
            Owner = Window.GetWindow(this)
        };
        dialog.SetReason(reason);
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        return dialog.Result switch
        {
            UnsavedSettingsChangesResult.SaveAndContinue => await SaveChangesAsync(),
            UnsavedSettingsChangesResult.Discard => RestoreSavedDraft(),
            _ => false
        };
    }

    private void OnEnterAdminModeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AdminPasswordDialog(_adminModeService)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        if (!_adminModeService.IsAdmin)
        {
            return;
        }

        var defaults = new RfidSettings();
        PollIntervalTextBox.Text = defaults.PollIntervalMs.ToString(CultureInfo.InvariantCulture);
        ExpectedVehicleCountTextBox.Text = defaults.ExpectedVehicleCount.ToString(CultureInfo.InvariantCulture);
        InterVehicleTimeoutTextBox.Text = defaults.InterVehicleTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private void OnRefreshStationsClick(object sender, RoutedEventArgs e)
    {
        RefreshBindingOverview();
    }

    private void OnConfigureYardCommunicationClick(object sender, RoutedEventArgs e)
    {
        var dialog = new YardCommunicationConfigDialog(
            _visibleYardCommunicationRows,
            YardCommunicationEditingEnabled)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || !YardCommunicationEditingEnabled)
        {
            return;
        }

        foreach (var editedRow in dialog.EditedRows)
        {
            var row = _yardCommunicationRows.FirstOrDefault(item =>
                string.Equals(item.YardId, editedRow.YardId, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                continue;
            }

            row.ListenIp = editedRow.ListenIp;
            row.ListenPort = editedRow.ListenPort;
            row.Enabled = editedRow.Enabled;
            row.AlarmForwardIp = editedRow.AlarmForwardIp;
            row.AlarmForwardPort = editedRow.AlarmForwardPort;
            row.AlarmForwardEnabled = editedRow.AlarmForwardEnabled;
        }

        RefreshBindingOverview();
    }

    public void SetSaveResult(string message, bool isError)
    {
        ValidationText.Text = message;
        ValidationText.Foreground = new System.Windows.Media.SolidColorBrush(
            isError ? System.Windows.Media.Color.FromRgb(255, 135, 149) : System.Windows.Media.Color.FromRgb(118, 229, 178));
    }

    private void OnAdminModePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdminModeService.IsAdmin))
        {
            UpdateStationEditorState();
        }
    }

    private void UpdateStationEditorState()
    {
        StationEditorPanel.IsEnabled = true;
        RfidSettingsEditingEnabled = _adminModeService.IsAdmin;
        StationEditingEnabled = _stationsEditable && _adminModeService.IsAdmin;
        BindingEditingEnabled = _stationsEditable && _adminModeService.IsAdmin && _bindingSaveRequested is not null;
        YardCommunicationEditingEnabled = _stationsEditable && _adminModeService.IsAdmin;
        CanAddStation = StationEditingEnabled && IsConcreteYardSelected();
    }

    private void OnViewRangeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateStationEditorState();
        ApplyStationFilter();
    }

    private void OnStationYardChanged(object sender, EventArgs e)
    {
        RefreshBindingOverview();
    }

    private void ApplyStationFilter()
    {
        if (StationsItemsControl is null || _isStationListRefreshInProgress)
        {
            return;
        }

        _isStationListRefreshInProgress = true;
        try
        {
            RebuildVisibleStationRows();
            RebuildVisibleYardCommunicationRows();
            StationsItemsControl.Items.Refresh();
        }
        finally
        {
            _isStationListRefreshInProgress = false;
        }
    }

    private void RebuildVisibleStationRows()
    {
        var selectedFilter = GetSelectedViewFilter();
        _visibleStationRows.Clear();
        var displayIndex = 0;
        foreach (var row in _stationRows)
        {
            var matches = selectedFilter switch
            {
                RfidStationYardOption.AllId => true,
                RfidStationYardOption.UnboundId => string.IsNullOrWhiteSpace(row.YardId) ||
                    string.Equals(row.YardId, RfidStationYardOption.UnboundId, StringComparison.OrdinalIgnoreCase),
                _ => string.Equals(row.YardId, selectedFilter, StringComparison.OrdinalIgnoreCase)
            };
            if (matches)
            {
                row.DisplayIndex = string.Equals(selectedFilter, RfidStationYardOption.AllId, StringComparison.OrdinalIgnoreCase)
                    ? _stationRows.IndexOf(row) + 1
                    : ++displayIndex;
                _visibleStationRows.Add(row);
            }
        }

        UpdateBindingDiagnosticSummary(selectedFilter);
    }

    private void RebuildVisibleYardCommunicationRows()
    {
        var selectedFilter = GetSelectedViewFilter();
        _visibleYardCommunicationRows.Clear();
        foreach (var row in _yardCommunicationRows)
        {
            if (string.Equals(selectedFilter, RfidStationYardOption.AllId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(row.YardId, selectedFilter, StringComparison.OrdinalIgnoreCase))
            {
                _visibleYardCommunicationRows.Add(row);
            }
        }

    }

    private string GetSelectedViewFilter() =>
        (ViewRangeComboBox?.SelectedItem as RfidStationYardOption)?.Id
        ?? ViewRangeComboBox?.SelectedValue?.ToString()
        ?? RfidStationYardOption.AllId;

    private bool IsConcreteYardSelected()
    {
        var selectedYardId = GetSelectedViewFilter();
        return !string.Equals(selectedYardId, RfidStationYardOption.AllId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(selectedYardId, RfidStationYardOption.UnboundId, StringComparison.OrdinalIgnoreCase)
            && _yardOptions.Any(option => string.Equals(option.Id, selectedYardId, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateBindingDiagnosticSummary(string selectedFilter)
    {
        if (BindingDiagnosticSummaryText is null)
        {
            return;
        }

        var visibleRows = _visibleStationRows.ToArray();
        var conflictCount = visibleRows.Count(row => row.HasYardBindingConflict);
        var duplicateBindingCount = visibleRows.Count(row => row.HasDuplicateBinding);
        var enabledCount = visibleRows.Count(row => row.Enabled);
        var boundCount = visibleRows.Count(row => row.HasBinding);
        var unboundCount = visibleRows.Count(row => !row.HasBinding);
        var pendingCount = visibleRows.Count(row =>
            !row.HasBinding || row.HasDuplicateBinding || row.HasYardBindingConflict);
        var rangeName = selectedFilter switch
        {
            RfidStationYardOption.AllId => "全部站场",
            RfidStationYardOption.UnboundId => "未归属",
            _ => _yardFilterOptions.FirstOrDefault(option =>
                    string.Equals(option.Id, selectedFilter, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? selectedFilter
        };

        BindingDiagnosticSummaryText.Text = $"当前查看：{rangeName}";
        BindingStationCountText.Text = visibleRows.Length.ToString(CultureInfo.InvariantCulture);
        BindingStationHelperText.Text = "当前配置设备";
        BindingEnabledCountText.Text = $"{enabledCount.ToString(CultureInfo.InvariantCulture)} / {visibleRows.Length.ToString(CultureInfo.InvariantCulture)}";
        BindingEnabledRateText.Text = $"启用率 {FormatPercentage(enabledCount, visibleRows.Length)}";
        BindingEnabledProgressBar.Value = GetRatio(enabledCount, visibleRows.Length);
        BindingBoundCountText.Text = $"{boundCount.ToString(CultureInfo.InvariantCulture)} / {visibleRows.Length.ToString(CultureInfo.InvariantCulture)}";
        BindingBoundRateText.Text = $"完成率 {FormatPercentage(boundCount, visibleRows.Length)}";
        BindingBoundProgressBar.Value = GetRatio(boundCount, visibleRows.Length);
        BindingPendingCountText.Text = pendingCount.ToString(CultureInfo.InvariantCulture);
        BindingPendingDetailsText.Text =
            $"未绑定 {unboundCount}  |  冲突 {conflictCount}  |  重复 {duplicateBindingCount}";

        var hasPending = pendingCount > 0;
        var warningBrush = (System.Windows.Media.Brush)FindResource("WarningBrush");
        var mutedTextBrush = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
        BindingPendingSummaryCard.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 32, 12));
        BindingPendingSummaryCard.BorderBrush = warningBrush;
        var statusBrush = warningBrush;
        BindingPendingSummaryIcon.Text = "\uE7BA";
        BindingPendingSummaryIcon.Foreground = statusBrush;
        BindingPendingSummaryIconContainer.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(73, 44, 14));
        BindingPendingCountText.Foreground = statusBrush;
        BindingPendingDetailsText.Foreground = mutedTextBrush;

        BindingDiagnosticWarningText.Text = conflictCount > 0
            ? $"当前存在 {conflictCount} 个通信归属与地图绑定冲突，请处理后保存。"
            : duplicateBindingCount > 0
                ? $"当前存在 {duplicateBindingCount} 个重复地图绑定，请处理后保存。"
                : unboundCount > 0
                    ? $"当前有 {unboundCount} 个基站未绑定地图点位，请处理后保存。"
                    : "当前配置无待处理项";
        BindingDiagnosticWarningPanel.Visibility = hasPending ? Visibility.Visible : Visibility.Collapsed;
        BindingDiagnosticWarningText.Foreground = hasPending
            ? (System.Windows.Media.Brush)FindResource("WarningBrush")
            : (System.Windows.Media.Brush)FindResource("SuccessBrush");
        BindingDiagnosticWarningPanel.BorderBrush = hasPending
            ? (System.Windows.Media.Brush)FindResource("WarningBrush")
            : (System.Windows.Media.Brush)FindResource("GridLineBrush");
        BindingDiagnosticWarningPanel.Background = hasPending
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 32, 12))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 31, 52));
    }

    private static double GetRatio(int value, int total)
    {
        if (total <= 0)
        {
            return 0d;
        }

        var ratio = (double)value / total;
        return ratio < 0d ? 0d : ratio > 1d ? 1d : ratio;
    }

    private static string FormatPercentage(int value, int total) =>
        $"{Math.Round(GetRatio(value, total) * 100d):0}%";

    private void OnAddStationClick(object sender, RoutedEventArgs e)
    {
        if (!_stationsEditable || !_adminModeService.IsAdmin)
        {
            return;
        }

        if (!IsConcreteYardSelected())
        {
            SetSaveResult("请选择具体站场后再新增基站。", true);
            return;
        }

        var selectedYardId = GetSelectedViewFilter();

        _stationRows.Add(new RfidStationEditorRow
        {
            DisplayIndex = GetNextDisplayIndex(selectedYardId),
            StationId = GetNextStationId(selectedYardId),
            ProtocolAddress = "03",
            StationIdOptions = _stationIdOptions,
            YardOptions = _yardOptions,
            YardId = selectedYardId
        });
        RefreshBindingOverview();
    }

    private void OnRemoveStationClick(object sender, RoutedEventArgs e)
    {
        if (!_stationsEditable || !_adminModeService.IsAdmin || !TryGetStationRow(sender, out var row))
        {
            return;
        }

        var bindings = RfidStationBindingRules.FindBindings(_yardConfigs, row.StationId);
        if (_isRfidStationBound(row.StationId) || bindings.Count > 0)
        {
            var dialog = new StyledMessageDialog(
                "无法删除 RFID 基站",
                $"基站“{row.StationId}”仍被地图标记引用：{Environment.NewLine}" +
                $"{string.Join(Environment.NewLine, bindings.Select(binding => $"• {binding.DisplayName}"))}" +
                $"{Environment.NewLine}请先解除地图绑定后再删除。",
                MessageDialogKind.Warning)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
            return;
        }

        _stationRows.Remove(row);
        for (var index = 0; index < _stationRows.Count; index++)
        {
            _stationRows[index].DisplayIndex = index + 1;
        }
        StationsItemsControl.Items.Refresh();
        RefreshBindingOverview();
    }

    private async void OnBindStationClick(object sender, RoutedEventArgs e)
    {
        if (!BindingEditingEnabled || !TryGetStationRow(sender, out var row))
        {
            return;
        }

        var existingBindings = RfidStationBindingRules.FindBindings(_yardConfigs, row.StationId);
        if (existingBindings.Count > 0)
        {
            ShowBindingMessage(
                "绑定已存在",
                $"RFID 基站“{row.StationId}”当前已绑定：{Environment.NewLine}" +
                string.Join(Environment.NewLine, existingBindings.Select(binding => $"• {binding.DisplayName}")) +
                $"{Environment.NewLine}请先解绑后再绑定。",
                MessageDialogKind.Warning);
            return;
        }

        var options = _yardConfigs
            .SelectMany(yard => (yard.Devices ?? Array.Empty<DeviceConfig>())
                .Where(device => device is not null &&
                                 device.Type == DeviceType.RfidStation &&
                                 string.IsNullOrWhiteSpace(device.RfidStationId))
                .Select(device => new RfidStationBindingOption(
                    yard.Id,
                    yard.Name,
                    device.Id,
                    device.Name)))
            .ToArray();
        if (options.Length == 0)
        {
            ShowBindingMessage("无法绑定", "当前没有未绑定的 RFID 地图点位。", MessageDialogKind.Warning);
            return;
        }

        var dialog = new RfidBindingDialog(row.StationId, options)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true || dialog.SelectedOption is null)
        {
            return;
        }

        var option = dialog.SelectedOption;
        var validation = RfidStationBindingRules.ValidateBinding(
            _yardConfigs,
            GetBindingStationConfigs(),
            option.YardId,
            option.DeviceId,
            row.StationId);
        if (!validation.Succeeded)
        {
            ShowBindingMessage("绑定被阻止", validation.Message, MessageDialogKind.Warning);
            RefreshBindingOverview();
            return;
        }

        var device = FindRfidMapDevice(option.YardId, option.DeviceId);
        var yard = _yardConfigs.FirstOrDefault(item =>
            string.Equals(item.Id, option.YardId, StringComparison.OrdinalIgnoreCase));
        if (device is null || yard is null || _bindingSaveRequested is null)
        {
            return;
        }

        var previous = device.RfidStationId;
        device.RfidStationId = row.StationId.Trim();
        if (!await _bindingSaveRequested(yard))
        {
            device.RfidStationId = previous;
            RefreshBindingOverview();
            return;
        }

        RefreshBindingOverview();
    }

    private async void OnUnbindStationClick(object sender, RoutedEventArgs e)
    {
        if (!BindingEditingEnabled || !TryGetStationRow(sender, out var row))
        {
            return;
        }

        var bindings = RfidStationBindingRules.FindBindings(_yardConfigs, row.StationId);
        if (bindings.Count == 0)
        {
            return;
        }

        if (bindings.Count > 1)
        {
            ShowBindingMessage(
                "发现重复绑定",
                $"基站“{row.StationId}”存在历史重复绑定，未自动删除任何数据：{Environment.NewLine}" +
                string.Join(Environment.NewLine, bindings.Select(binding => $"• {binding.DisplayName}")) +
                $"{Environment.NewLine}请在地图点位中逐个解绑。",
                MessageDialogKind.Warning);
            return;
        }

        var binding = bindings[0];
        var device = FindRfidMapDevice(binding.YardId, binding.DeviceId);
        var yard = _yardConfigs.FirstOrDefault(item =>
            string.Equals(item.Id, binding.YardId, StringComparison.OrdinalIgnoreCase));
        if (device is null || yard is null || _bindingSaveRequested is null)
        {
            return;
        }

        var previous = device.RfidStationId;
        device.RfidStationId = null;
        if (!await _bindingSaveRequested(yard))
        {
            device.RfidStationId = previous;
            RefreshBindingOverview();
            return;
        }

        RefreshBindingOverview();
    }

    private void OnMoreActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        foreach (var item in button.ContextMenu.Items)
        {
            if (item is not MenuItem menuItem)
            {
                continue;
            }

            menuItem.IsEnabled = menuItem.Header?.ToString() switch
            {
                "解绑" => BindingEditingEnabled,
                "移除" => StationEditingEnabled,
                _ => true
            };
        }

        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void OnLocateMapPointClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetStationRow(sender, out var row))
        {
            return;
        }

        var binding = RfidStationBindingRules.FindBindings(_yardConfigs, row.StationId).FirstOrDefault();
        if (binding is null)
        {
            ShowBindingMessage("未绑定地图点位", $"基站“{row.StationId}”当前没有地图点位。", MessageDialogKind.Information);
            return;
        }

        if (_viewMapPointRequested is null)
        {
            ShowBindingMessage("地图绑定详情", row.BindingFullDetails, MessageDialogKind.Information);
            return;
        }

        _viewMapPointRequested.Invoke(binding.YardId, binding.DeviceId);
    }

    private void OnViewMapPointClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetStationRow(sender, out var row))
        {
            return;
        }

        var binding = RfidStationBindingRules.FindBindings(_yardConfigs, row.StationId).FirstOrDefault();
        if (binding is null)
        {
            ShowBindingMessage("未绑定地图点位", $"基站“{row.StationId}”当前没有地图点位。", MessageDialogKind.Information);
            return;
        }

        ShowBindingMessage("地图绑定详情", row.BindingFullDetails, MessageDialogKind.Information);
    }

    private static bool TryGetStationRow(object sender, out RfidStationEditorRow row)
    {
        row = ((sender as FrameworkElement)?.DataContext as RfidStationEditorRow)!;
        return row is not null;
    }

    private string BuildBindingFullDetails(
        RfidStationEditorRow row,
        IReadOnlyList<RfidStationBindingLocation> bindings,
        IReadOnlyList<RfidStationBindingLocation> conflictingBindings)
    {
        var communicationYard = GetYardDisplayName(row.YardId);
        if (bindings.Count == 0)
        {
            return $"通信归属：{communicationYard}{Environment.NewLine}地图绑定：未绑定";
        }

        var mapBindings = string.Join("、", bindings.Select(binding => binding.DisplayName));
        var details =
            $"通信归属：{communicationYard}{Environment.NewLine}" +
            $"地图绑定：{mapBindings}";
        if (conflictingBindings.Count > 0)
        {
            details += Environment.NewLine + "处理建议：请调整通信归属，或解除地图绑定后重新绑定到正确站场。";
        }

        return details;
    }

    private string GetYardDisplayName(string? yardId)
    {
        var normalizedYardId = NormalizeYardId(yardId);
        if (normalizedYardId is null)
        {
            return "未归属";
        }

        return _yardOptions.FirstOrDefault(option =>
                   string.Equals(option.Id, normalizedYardId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? normalizedYardId;
    }

    private DeviceConfig? FindRfidMapDevice(string yardId, string deviceId) =>
        _yardConfigs
            .FirstOrDefault(item => string.Equals(item.Id, yardId, StringComparison.OrdinalIgnoreCase))?
            .Devices
            .FirstOrDefault(item => item is not null &&
                                    item.Type == DeviceType.RfidStation &&
                                    string.Equals(item.Id, deviceId, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<RfidStationConfig> GetBindingStationConfigs() =>
        _configuredRfidStations
            .Concat(_stationRows.Select(row => new RfidStationConfig
            {
                StationId = row.StationId.Trim()
            }))
            .Where(item => !string.IsNullOrWhiteSpace(item.StationId))
            .GroupBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private void ShowBindingMessage(string title, string message, MessageDialogKind kind)
    {
        var dialog = new StyledMessageDialog(title, message, kind)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveChangesAsync();

    private async Task<bool> SaveChangesAsync()
    {
        if (!_adminModeService.IsAdmin)
        {
            SetSaveResult("请先进入管理员模式后再修改 RFID 识别参数。", true);
            return false;
        }

        if (_configurationErrors.Count > 0)
        {
            SetSaveResult("项目配置错误：" + string.Join(Environment.NewLine, _configurationErrors), true);
            return false;
        }

        if (!int.TryParse(PollIntervalTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) ||
            !int.TryParse(ExpectedVehicleCountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vehicleCount) ||
            !int.TryParse(InterVehicleTimeoutTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout))
        {
            SetSaveResult("三个参数都必须是整数。", true);
            return false;
        }

        var candidate = new RfidSettings
        {
            PollIntervalMs = interval,
            ExpectedVehicleCount = vehicleCount,
            InterVehicleTimeoutSeconds = timeout,
            EmptyRfidValue = _settings.EmptyRfidValue
        };
        var errors = candidate.Validate().ToList();
        var stations = _stationsEditable && _adminModeService.IsAdmin
            ? TryBuildStations(errors)
            : Array.Empty<RfidStationConfig>();
        var yardCommunications = _stationsEditable && SaveYardCommunicationsRequested is not null
            ? TryBuildYardCommunications(errors)
            : Array.Empty<YardCommunicationConfig>();
        var yardAlarmForwards = _stationsEditable && SaveYardAlarmForwardsRequested is not null
            ? TryBuildYardAlarmForwards(errors)
            : Array.Empty<YardAlarmForwardConfig>();
        if (errors.Count > 0 || stations is null || yardCommunications is null || yardAlarmForwards is null)
        {
            SetSaveResult(string.Join(Environment.NewLine, errors), true);
            return false;
        }

        try
        {
            if (_stationsEditable && _adminModeService.IsAdmin && StationsSaveRequested is not null &&
                !await StationsSaveRequested(stations))
            {
                return false;
            }
            if (_stationsEditable && _adminModeService.IsAdmin && SaveYardCommunicationsRequested is not null &&
                !await SaveYardCommunicationsRequested(yardCommunications))
            {
                return false;
            }
            if (_stationsEditable && _adminModeService.IsAdmin && SaveYardAlarmForwardsRequested is not null &&
                !await SaveYardAlarmForwardsRequested(yardAlarmForwards))
            {
                return false;
            }
            if (SaveRequested is not null && !await SaveRequested(candidate))
            {
                return false;
            }
        }
        catch (Exception exception)
        {
            SetSaveResult($"保存失败：{exception.Message}", true);
            return false;
        }

        _savedDraft = CaptureDraft();
        return true;
    }

    private bool RestoreSavedDraft()
    {
        PollIntervalTextBox.Text = _savedDraft.PollIntervalText;
        ExpectedVehicleCountTextBox.Text = _savedDraft.ExpectedVehicleCountText;
        InterVehicleTimeoutTextBox.Text = _savedDraft.InterVehicleTimeoutText;
        _stationRows.Clear();
        foreach (var station in _savedDraft.Stations)
        {
            _stationRows.Add(station.ToRow(_stationIdOptions, _yardOptions, _stationRows.Count + 1));
        }
        _yardCommunicationRows.Clear();
        foreach (var communication in _savedDraft.YardCommunications)
        {
            _yardCommunicationRows.Add(communication.ToRow(_yardOptions));
        }
        StationsItemsControl.Items.Refresh();
        RefreshBindingOverview();
        ValidationText.Text = string.Empty;
        return true;
    }

    private SettingsDraft CaptureDraft() => new(
        PollIntervalTextBox?.Text ?? string.Empty,
        ExpectedVehicleCountTextBox?.Text ?? string.Empty,
        InterVehicleTimeoutTextBox?.Text ?? string.Empty,
        _stationRows.Select(StationDraft.FromRow).ToArray(),
        _yardCommunicationRows.Select(YardCommunicationDraft.FromRow).ToArray());

    private static bool AreDraftsEqual(SettingsDraft left, SettingsDraft right)
    {
        if (!string.Equals(left.PollIntervalText, right.PollIntervalText, StringComparison.Ordinal) ||
            !string.Equals(left.ExpectedVehicleCountText, right.ExpectedVehicleCountText, StringComparison.Ordinal) ||
            !string.Equals(left.InterVehicleTimeoutText, right.InterVehicleTimeoutText, StringComparison.Ordinal) ||
            left.Stations.Count != right.Stations.Count ||
            left.YardCommunications.Count != right.YardCommunications.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Stations.Count; index++)
        {
            if (!left.Stations[index].IsSameAs(right.Stations[index]))
            {
                return false;
            }
        }

        for (var index = 0; index < left.YardCommunications.Count; index++)
        {
            if (!left.YardCommunications[index].IsSameAs(right.YardCommunications[index]))
            {
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<YardCommunicationConfig>? TryBuildYardCommunications(ICollection<string> errors)
    {
        var configurations = new List<YardCommunicationConfig>();
        foreach (var row in _yardCommunicationRows)
        {
            if (!int.TryParse(row.ListenPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var listenPort))
            {
                errors.Add($"站场 {row.YardId} 的监听端口无效：{row.ListenPort}。");
                continue;
            }

            configurations.Add(new YardCommunicationConfig
            {
                YardId = row.YardId.Trim(),
                ListenIp = row.ListenIp.Trim(),
                ListenPort = listenPort,
                Enabled = row.Enabled
            });
        }

        foreach (var configuration in configurations)
        {
            foreach (var validationError in configuration.Validate())
            {
                errors.Add(validationError);
            }
        }

        foreach (var duplicate in configurations
                     .Where(configuration => configuration.Enabled && configuration.TryResolveEndpoint(out _))
                     .GroupBy(configuration =>
                         configuration.TryResolveEndpoint(out var endpoint)
                             ? $"{endpoint.Address}|{endpoint.Port}"
                             : string.Empty,
                         StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"启用的站场监听端点重复：{duplicate.First().ListenIp}:{duplicate.First().ListenPort}。");
        }

        return configurations;
    }

    private IReadOnlyList<YardAlarmForwardConfig>? TryBuildYardAlarmForwards(ICollection<string> errors)
    {
        var configurations = new List<YardAlarmForwardConfig>();
        foreach (var row in _yardCommunicationRows)
        {
            var targetPort = 0;
            if (!string.IsNullOrWhiteSpace(row.AlarmForwardPort) &&
                !int.TryParse(row.AlarmForwardPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out targetPort))
            {
                errors.Add($"站场 {row.YardId} 的报警转发目标端口无效：{row.AlarmForwardPort}。");
                continue;
            }

            configurations.Add(new YardAlarmForwardConfig
            {
                YardId = row.YardId.Trim(),
                Enabled = row.AlarmForwardEnabled,
                TargetIp = row.AlarmForwardIp.Trim(),
                TargetPort = targetPort
            });
        }

        foreach (var configuration in configurations)
        {
            foreach (var validationError in configuration.Validate())
            {
                errors.Add(validationError);
            }
        }

        return configurations;
    }

    private IReadOnlyList<RfidStationConfig>? TryBuildStations(ICollection<string> errors)
    {
        if (!_stationsEditable)
        {
            return Array.Empty<RfidStationConfig>();
        }

        var stations = new List<RfidStationConfig>();
        foreach (var row in _stationRows)
        {
            if (!row.Enabled)
            {
                stations.Add(row.ToConfigWithoutValidation());
                continue;
            }

            if (!byte.TryParse(NormalizeProtocolText(row.ProtocolAddress), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var protocolAddress))
            {
                errors.Add($"RFID基站协议地址无效：{row.StationId}。");
                continue;
            }
            if (!int.TryParse(row.Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
            {
                errors.Add($"RFID基站端口无效：{row.StationId}。");
                continue;
            }

            stations.Add(new RfidStationConfig
            {
                StationId = row.StationId.Trim(),
                Name = row.Name.Trim(),
                YardId = NormalizeYardId(row.YardId),
                IpAddress = row.IpAddress.Trim(),
                Port = port,
                ProtocolAddress = protocolAddress,
                Enabled = true,
                Mode = 0x04,
                CommandBytes = new byte[4],
                RequestPayload = new byte[28],
                DestinationEndpoint = IPAddress.TryParse(row.IpAddress, out var address) && port is >= 1 and <= 65535
                    ? new IPEndPoint(address, port)
                    : new IPEndPoint(IPAddress.None, 0)
            });
        }

        foreach (var duplicateError in RfidStationConfig.FindDuplicateCommunicationKeys(stations))
        {
            errors.Add(duplicateError);
        }

        return stations;
    }

    private static string NormalizeProtocolText(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring(2) : trimmed;
    }

    private string GetNextStationId(string yardId)
    {
        return RfidStationIdentity.CreateScopedId(yardId, GetNextLocalNumber(yardId));
    }

    private int GetNextDisplayIndex(string yardId) => GetNextLocalNumber(yardId);

    private int GetNextLocalNumber(string yardId)
    {
        for (var localNumber = 1; localNumber <= 9999; localNumber++)
        {
            if (!IsLocalNumberUsed(yardId, localNumber))
            {
                return localNumber;
            }
        }

        return _stationRows.Count + 1;
    }

    private bool IsLocalNumberUsed(string yardId, int localNumber) =>
        _stationRows
            .Where(row => string.Equals(row.YardId, yardId, StringComparison.OrdinalIgnoreCase))
            .Any(row =>
                TryGetStationNumber(row, out var stationNumber) && stationNumber == localNumber);

    private static bool TryGetStationNumber(RfidStationEditorRow row, out int localNumber) =>
        RfidStationIdentity.TryGetLocalNumber(row.StationId, row.YardId, out localNumber) ||
        TryGetLegacyStationNumber(row.StationId, out localNumber);

    private static bool TryGetLegacyStationNumber(string stationId, out int localNumber)
    {
        localNumber = 0;
        var normalizedStationId = stationId.Trim();
        const string prefix = "RFID-";
        if (!normalizedStationId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(
                   normalizedStationId.Substring(prefix.Length),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out localNumber)
            && localNumber > 0;
    }

    private static IReadOnlyList<string> BuildStationIdOptions(IEnumerable<RfidStationConfig> stations)
    {
        var options = stations
            .Where(station => station is not null && !string.IsNullOrWhiteSpace(station.StationId))
            .Select(station => station.StationId.Trim())
            .ToList();

        for (var index = 1; index <= 999; index++)
        {
            options.Add($"RFID-{index:00}");
        }

        return options.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<RfidStationYardOption> BuildYardOptions(IEnumerable<StationConfig> yards) =>
        yards
            .Where(yard => yard is not null && !string.IsNullOrWhiteSpace(yard.Id))
            .Select(yard => new RfidStationYardOption(yard.Id, yard.Name))
            .GroupBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Concat(new[] { new RfidStationYardOption(RfidStationYardOption.UnboundId, "未归属") })
            .ToArray();

    private static IReadOnlyList<RfidStationYardOption> BuildYardFilterOptions(IReadOnlyList<RfidStationYardOption> yards) =>
        new[] { new RfidStationYardOption(RfidStationYardOption.AllId, "全部站场") }
            .Concat(yards.Where(option => !string.Equals(option.Id, RfidStationYardOption.UnboundId, StringComparison.OrdinalIgnoreCase)))
            .Concat(new[] { new RfidStationYardOption(RfidStationYardOption.UnboundId, "未归属") })
            .ToArray();

    private static IReadOnlyList<YardCommunicationEditorRow> BuildYardCommunicationRows(
        IEnumerable<YardCommunicationConfig> configurations,
        IEnumerable<YardAlarmForwardConfig> alarmForwards,
        IReadOnlyList<StationConfig> yards)
    {
        var configured = configurations
            .Where(configuration => configuration is not null && !string.IsNullOrWhiteSpace(configuration.YardId))
            .GroupBy(configuration => configuration.YardId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var configuredAlarmForwards = alarmForwards
            .Where(configuration => configuration is not null && !string.IsNullOrWhiteSpace(configuration.YardId))
            .GroupBy(configuration => configuration.YardId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return yards
            .Where(yard => yard is not null && !string.IsNullOrWhiteSpace(yard.Id))
            .Select(yard =>
            {
                configured.TryGetValue(yard.Id.Trim(), out var configuration);
                configuredAlarmForwards.TryGetValue(yard.Id.Trim(), out var alarmForward);
                return new YardCommunicationEditorRow
                {
                    YardId = yard.Id.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(yard.Name) ? yard.Id.Trim() : yard.Name,
                    ListenIp = configuration?.ListenIp ?? string.Empty,
                    ListenPort = configuration is not null && configuration.ListenPort > 0
                        ? configuration.ListenPort.ToString(CultureInfo.InvariantCulture)
                        : string.Empty,
                    Enabled = configuration?.Enabled ?? true,
                    AlarmForwardEnabled = alarmForward?.Enabled ?? false,
                    AlarmForwardIp = alarmForward?.TargetIp ?? string.Empty,
                    AlarmForwardPort = alarmForward is not null && alarmForward.TargetPort > 0
                        ? alarmForward.TargetPort.ToString(CultureInfo.InvariantCulture)
                        : string.Empty
                };
            })
            .ToArray();
    }

    private static string? NormalizeYardId(string? yardId) =>
        string.IsNullOrWhiteSpace(yardId) || string.Equals(yardId, RfidStationYardOption.UnboundId, StringComparison.OrdinalIgnoreCase)
            ? null
            : yardId!.Trim();

    private sealed class SettingsDraft
    {
        public SettingsDraft(
            string pollIntervalText,
            string expectedVehicleCountText,
            string interVehicleTimeoutText,
            IReadOnlyList<StationDraft> stations,
            IReadOnlyList<YardCommunicationDraft> yardCommunications)
        {
            PollIntervalText = pollIntervalText;
            ExpectedVehicleCountText = expectedVehicleCountText;
            InterVehicleTimeoutText = interVehicleTimeoutText;
            Stations = stations;
            YardCommunications = yardCommunications;
        }

        public string PollIntervalText { get; }

        public string ExpectedVehicleCountText { get; }

        public string InterVehicleTimeoutText { get; }

        public IReadOnlyList<StationDraft> Stations { get; }

        public IReadOnlyList<YardCommunicationDraft> YardCommunications { get; }
    }

    private sealed class YardCommunicationDraft
    {
        public YardCommunicationDraft(
            string yardId,
            string listenIp,
            string listenPort,
            bool enabled,
            string alarmForwardIp,
            string alarmForwardPort,
            bool alarmForwardEnabled)
        {
            YardId = yardId;
            ListenIp = listenIp;
            ListenPort = listenPort;
            Enabled = enabled;
            AlarmForwardIp = alarmForwardIp;
            AlarmForwardPort = alarmForwardPort;
            AlarmForwardEnabled = alarmForwardEnabled;
        }

        public string YardId { get; }

        public string ListenIp { get; }

        public string ListenPort { get; }

        public bool Enabled { get; }

        public string AlarmForwardIp { get; }

        public string AlarmForwardPort { get; }

        public bool AlarmForwardEnabled { get; }

        public static YardCommunicationDraft FromRow(YardCommunicationEditorRow row) => new(
            row.YardId,
            row.ListenIp,
            row.ListenPort,
            row.Enabled,
            row.AlarmForwardIp,
            row.AlarmForwardPort,
            row.AlarmForwardEnabled);

        public YardCommunicationEditorRow ToRow(IReadOnlyList<RfidStationYardOption> yardOptions) => new()
        {
            YardId = YardId,
            DisplayName = yardOptions.FirstOrDefault(option =>
                               string.Equals(option.Id, YardId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? YardId,
            ListenIp = ListenIp,
            ListenPort = ListenPort,
            Enabled = Enabled,
            AlarmForwardIp = AlarmForwardIp,
            AlarmForwardPort = AlarmForwardPort,
            AlarmForwardEnabled = AlarmForwardEnabled
        };

        public bool IsSameAs(YardCommunicationDraft other) =>
            string.Equals(YardId, other.YardId, StringComparison.Ordinal) &&
            string.Equals(ListenIp, other.ListenIp, StringComparison.Ordinal) &&
            string.Equals(ListenPort, other.ListenPort, StringComparison.Ordinal) &&
            Enabled == other.Enabled &&
            string.Equals(AlarmForwardIp, other.AlarmForwardIp, StringComparison.Ordinal) &&
            string.Equals(AlarmForwardPort, other.AlarmForwardPort, StringComparison.Ordinal) &&
            AlarmForwardEnabled == other.AlarmForwardEnabled;
    }

    private sealed class StationDraft
    {
        public StationDraft(
            string stationId,
            string name,
            string ipAddress,
            string port,
            string protocolAddress,
            string yardId,
            bool enabled)
        {
            StationId = stationId;
            Name = name;
            IpAddress = ipAddress;
            Port = port;
            ProtocolAddress = protocolAddress;
            YardId = yardId;
            Enabled = enabled;
        }

        public string StationId { get; }

        public string Name { get; }

        public string IpAddress { get; }

        public string Port { get; }

        public string ProtocolAddress { get; }

        public string YardId { get; }

        public bool Enabled { get; }

        public static StationDraft FromRow(RfidStationEditorRow row) => new(
            row.StationId,
            row.Name,
            row.IpAddress,
            row.Port,
            row.ProtocolAddress,
            row.YardId,
            row.Enabled);

        public RfidStationEditorRow ToRow(
            IReadOnlyList<string> stationIdOptions,
            IReadOnlyList<RfidStationYardOption> yardOptions,
            int displayIndex) => new()
        {
            DisplayIndex = displayIndex,
            StationIdOptions = stationIdOptions,
            YardOptions = yardOptions,
            StationId = StationId,
            Name = Name,
            IpAddress = IpAddress,
            Port = Port,
            ProtocolAddress = ProtocolAddress,
            YardId = YardId,
            Enabled = Enabled
        };

        public bool IsSameAs(StationDraft other) =>
            string.Equals(StationId, other.StationId, StringComparison.Ordinal) &&
            string.Equals(Name, other.Name, StringComparison.Ordinal) &&
            string.Equals(IpAddress, other.IpAddress, StringComparison.Ordinal) &&
            string.Equals(Port, other.Port, StringComparison.Ordinal) &&
            string.Equals(ProtocolAddress, other.ProtocolAddress, StringComparison.Ordinal) &&
            string.Equals(YardId, other.YardId, StringComparison.Ordinal) &&
            Enabled == other.Enabled;
    }
}

public sealed class YardCommunicationEditorRow
{
    public string YardId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ListenIp { get; set; } = string.Empty;

    public string ListenPort { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string AlarmForwardIp { get; set; } = string.Empty;

    public string AlarmForwardPort { get; set; } = string.Empty;

    public bool AlarmForwardEnabled { get; set; }
}

public sealed class RfidStationBindingOption
{
    public RfidStationBindingOption(
        string yardId,
        string yardName,
        string deviceId,
        string deviceName)
    {
        YardId = yardId;
        YardName = yardName;
        DeviceId = deviceId;
        DeviceName = deviceName;
    }

    public string YardId { get; }

    public string YardName { get; }

    public string DeviceId { get; }

    public string DeviceName { get; }

    public string DisplayName =>
        $"{(string.IsNullOrWhiteSpace(YardName) ? YardId : YardName)} / " +
        $"{(string.IsNullOrWhiteSpace(DeviceName) ? DeviceId : DeviceName)}";
}

public sealed class RfidStationYardOption
{
    public const string AllId = "__ALL_YARDS__";
    public const string UnboundId = "__UNBOUND_YARD__";

    public RfidStationYardOption(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}

public sealed class RfidStationEditorRow
{
    public int DisplayIndex { get; set; }

    public IReadOnlyList<string> StationIdOptions { get; set; } = Array.Empty<string>();

    public IReadOnlyList<RfidStationYardOption> YardOptions { get; set; } = Array.Empty<RfidStationYardOption>();

    public string StationId { get; set; } = string.Empty;

    public string DisplayStationId => RfidStationIdentity.GetDisplayId(StationId, YardId);

    public string Name { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string Port { get; set; } = string.Empty;

    public string ProtocolAddress { get; set; } = string.Empty;

    public string YardId { get; set; } = string.Empty;

    public string YardDisplayName { get; set; } = "未归属";

    public bool Enabled { get; set; }

    public bool HasBinding { get; set; }

    public bool HasDuplicateBinding { get; set; }

    public bool HasYardBindingConflict { get; set; }

    public string LastOnlineDisplay { get; set; } = "—";

    public string BindingStatusText { get; set; } = "未绑定";

    public string BindingDetailText { get; set; } = string.Empty;

    public string BindingFullDetails { get; set; } = string.Empty;

    public string BindingDisplay { get; set; } = "未绑定";

    public string BindingLocations { get; set; } = string.Empty;

    public static RfidStationEditorRow FromConfig(
        RfidStationConfig station,
        IReadOnlyList<string> stationIdOptions,
        IReadOnlyList<RfidStationYardOption> yardOptions) => new()
    {
        StationIdOptions = stationIdOptions,
        YardOptions = yardOptions,
        StationId = station.StationId,
        Name = station.Name,
        YardId = station.YardId ?? RfidStationYardOption.UnboundId,
        YardDisplayName = station.YardId ?? "未归属",
        IpAddress = station.IpAddress,
        Port = station.Port > 0 ? station.Port.ToString(CultureInfo.InvariantCulture) : string.Empty,
        ProtocolAddress = station.ProtocolAddress.ToString("X2"),
        Enabled = station.Enabled,
        BindingDisplay = "未绑定"
    };

    public RfidStationConfig ToConfigWithoutValidation() => new()
    {
        StationId = StationId.Trim(),
        Name = Name.Trim(),
        YardId = string.IsNullOrWhiteSpace(YardId) || string.Equals(YardId, RfidStationYardOption.UnboundId, StringComparison.OrdinalIgnoreCase)
            ? null
            : YardId.Trim(),
        IpAddress = IpAddress.Trim(),
        Port = int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 0,
        ProtocolAddress = byte.TryParse(NormalizeProtocolText(ProtocolAddress), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var protocolAddress) ? protocolAddress : (byte)0,
        Enabled = false,
        Mode = 0x04,
        CommandBytes = new byte[4],
        RequestPayload = new byte[28]
    };

    private static string NormalizeProtocolText(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring(2) : trimmed;
    }
}
