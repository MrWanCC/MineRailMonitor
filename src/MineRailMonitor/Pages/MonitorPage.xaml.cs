using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using MineRailMonitor.Controls;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Recognition;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Infrastructure.Configuration;

namespace MineRailMonitor.Pages;

public partial class MonitorPage : UserControl
{
    private const int RfidTaskPageSize = 6;
    private readonly ObservableCollection<DashboardEvent> _runtimeEvents = new();
    private readonly ObservableCollection<RfidTaskCardViewModel> _rfidTaskCards = new();
    private readonly ObservableCollection<RfidAlarmDisplayItem> _rfidAlarmItems = new();
    private IReadOnlyList<PassageRecord> _recentAlarmRecords = Array.Empty<PassageRecord>();
    private readonly IProjectConfigService _configService;
    private readonly string _projectDirectory;
    private readonly AdminModeService _adminModeService;
    private readonly IReadOnlyList<StationConfig> _allYards;
    private string? _currentStationId;
    private string _currentStationDisplayName = "-";
    private DeviceConfig? _selectedDevice;
    private string? _selectedRfidStationId;
    private StationConfig? _currentStation;
    private ImageSource? _currentImageSource;
    private MapAnnotationEditor? _annotationEditor;
    private MapAnnotationKind? _selectedAnnotationKind;
    private string? _selectedAnnotationId;
    private MapAnnotationKind? _pendingAnnotationKind;
    private bool _isNewAnnotation;
    private MapAnnotationTool _annotationTool = MapAnnotationTool.Select;
    private bool _isMapAnnotationEditing;
    private IReadOnlyList<StationRuntimeState> _rfidRuntimeStates = Array.Empty<StationRuntimeState>();
    private IReadOnlyList<RfidStationConfig> _rfidStations = Array.Empty<RfidStationConfig>();
    private HashSet<string> _visibleRfidStationIds = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<RfidTaskCardViewModel> _allRfidTaskCards = Array.Empty<RfidTaskCardViewModel>();
    private int _rfidTaskPageIndex;
    private int _pollIntervalMs = 200;
    private DateTimeOffset? _lastPollAt;
    private bool _databaseHealthy = true;
    private bool _rfidListenerHealthy;
    private bool _externalInterfaceAvailable;

    private enum SystemHealthState
    {
        Healthy,
        Warning,
        Error,
        Unavailable
    }

    public MonitorPage(
        IProjectConfigService configService,
        string projectDirectory,
        AdminModeService adminModeService,
        IEnumerable<StationConfig>? allYards = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _projectDirectory = projectDirectory ?? throw new ArgumentNullException(nameof(projectDirectory));
        _adminModeService = adminModeService ?? throw new ArgumentNullException(nameof(adminModeService));
        _allYards = allYards?.Where(item => item is not null).ToArray() ?? Array.Empty<StationConfig>();
        InitializeComponent();
        RuntimeEventsGrid.ItemsSource = _runtimeEvents;
        RfidTaskItemsControl.ItemsSource = _rfidTaskCards;
        RfidAlarmItemsControl.ItemsSource = _rfidAlarmItems;
        AnnotationRfidStationComboBox.ItemsSource = new ObservableCollection<RfidStationChoice>();
        Map.DeviceSelected += OnDeviceSelected;
        Map.AnnotationClicked += OnAnnotationClicked;
        Map.CanvasClicked += OnCanvasClicked;
        Map.AnnotationDragged += OnAnnotationDragged;
        UpdateAdminMapControls();
        RefreshRfidOverviewMetrics();
        RefreshRfidTaskCards();
        RefreshRfidAlarmDisplay();
        ApplySelectedRfidRuntimeState();
    }

    public MapViewport Viewport => Map;

    public bool HasUnsavedMapChanges => _annotationEditor?.HasUnsavedChanges == true;

    public event EventHandler? ResetRequested;

    public event EventHandler? AlarmMoreRequested;

    public event EventHandler? MapAnnotationsSaved;

    public void SetSystemHealth(bool databaseHealthy, bool externalInterfaceAvailable)
    {
        _databaseHealthy = databaseHealthy;
        _externalInterfaceAvailable = externalInterfaceAvailable;
        UpdateSystemStatusIndicators();
        RefreshRfidOverviewMetrics();
    }

    public void SetSystemRfidStatus(bool listenerHealthy)
    {
        _rfidListenerHealthy = listenerHealthy;
        UpdateSystemStatusIndicators();
    }

    public void SetRfidStations(IEnumerable<RfidStationConfig> stations)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));

        _rfidStations = stations
            .Where(station => station is not null && !string.IsNullOrWhiteSpace(station.StationId))
            .GroupBy(station => station.StationId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        _visibleRfidStationIds = new HashSet<string>(
            _rfidStations.Select(station => station.StationId),
            StringComparer.OrdinalIgnoreCase);
        var selectedRfidStationId = _selectedRfidStationId;
        var selectedId = AnnotationRfidStationComboBox.SelectedValue as string;
        AnnotationRfidStationComboBox.ItemsSource = new ObservableCollection<RfidStationChoice>(
            _rfidStations.Select(station => new RfidStationChoice(
                station.StationId,
                string.IsNullOrWhiteSpace(station.Name) ? station.StationId : station.Name,
                station.YardId,
                station.Enabled)));
        AnnotationRfidStationComboBox.SelectedValue = selectedId;
        Map.SetRfidStations(_rfidStations);
        Map.SetRfidRuntimeStates(_rfidRuntimeStates);
        UpdateSystemStatusIndicators();
        RefreshRfidOverviewMetrics();
        RefreshRfidTaskCards();
        if (!string.IsNullOrWhiteSpace(selectedRfidStationId) &&
            !_visibleRfidStationIds.Contains(selectedRfidStationId!))
        {
            ClearDetails();
        }
        else
        {
            ApplySelectedRfidRuntimeState();
        }
    }

    public void SetRfidDisplayScope(IEnumerable<RfidStationConfig> stations) => SetRfidStations(stations);

    public void SetRfidPollingInfo(int pollIntervalMs, IEnumerable<RfidStationPollingStatus> statuses)
    {
        _pollIntervalMs = pollIntervalMs > 0 ? pollIntervalMs : 200;
        var lastPoll = statuses?
            .Where(status => status is not null)
            .Select(status => status.LastRequestAt)
            .Where(value => value.HasValue)
            .OrderByDescending(value => value)
            .FirstOrDefault();
        _lastPollAt = lastPoll;
        RefreshRfidOverviewMetrics();
    }

    public void SetStation(StationConfig station, ImageSource? imageSource)
    {
        if (!_currentStationId?.Equals(station.Id, StringComparison.OrdinalIgnoreCase) ?? true)
        {
            AddSystemEvent("站场", $"切换至 {FormatStationName(station)}");
        }

        _currentStationId = station.Id;
        _currentStation = station;
        _currentImageSource = imageSource;
        _currentStationDisplayName = FormatStationName(station);
        MapStationTitle.Text = $"{_currentStationDisplayName}站场  实时监控";
        ExitMapAnnotationEditModeWithoutPrompt();
        RefreshRfidTaskCards();
        Map.SetStation(station, imageSource);
        Map.SetRfidRuntimeStates(_rfidRuntimeStates);
        SetRecognitionStatus(null, isAlarm: false);
        RecognitionSnapshotText.Text = "当前列车：-";
        ApplyDashboardSnapshot(new DashboardSnapshot(Array.Empty<DashboardTrain>(), Array.Empty<DashboardEvent>(), null));
    }

    public bool SelectRfidStationDevice(string deviceId)
    {
        if (_currentStation is null)
        {
            return false;
        }

        var device = _currentStation.Devices.FirstOrDefault(item =>
            item is not null &&
            item.Type == DeviceType.RfidStation &&
            string.Equals(item.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return false;
        }

        var state = _rfidRuntimeStates.FirstOrDefault(item =>
            string.Equals(item.StationId, device.RfidStationId, StringComparison.OrdinalIgnoreCase));
        var status = state?.LifecycleState == PassageLifecycleState.Alarm
            ? DeviceStatus.Alarm
            : state?.CommunicationState == StationCommunicationState.Online
                ? DeviceStatus.Normal
                : DeviceStatus.Offline;
        SelectRfidDevice(device, status);
        return true;
    }

    public async Task<bool> TryLeaveMapEditingAsync(string reason)
    {
        if (_annotationEditor is null)
        {
            return true;
        }

        if (!_annotationEditor.HasUnsavedChanges)
        {
            ExitMapAnnotationEditModeWithoutPrompt();
            return true;
        }

        var dialog = new UnsavedMapChangesDialog
        {
            Owner = Window.GetWindow(this)
        };
        dialog.SetReason(reason);
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        switch (dialog.Result)
        {
            case UnsavedMapChangesResult.SaveAndExit:
                if (!await SaveMapAnnotationsAsync())
                {
                    return false;
                }

                ExitMapAnnotationEditModeWithoutPrompt();
                return true;
            case UnsavedMapChangesResult.Discard:
                if (_adminModeService.IsAdmin)
                {
                    _annotationEditor.DiscardChanges();
                }

                ExitMapAnnotationEditModeWithoutPrompt();
                return true;
            default:
                return false;
        }
    }

    public void HandleAdminModeChanged(bool isAdmin)
    {
        if (!isAdmin)
        {
            ExitMapAnnotationEditModeWithoutPrompt();
        }

        UpdateAdminMapControls();
    }

    public void AddSystemEvent(string message) => AddSystemEvent("系统", message);

    public void AddSystemEvent(string type, string message)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _runtimeEvents.Insert(0, new DashboardEvent(DateTime.Now.ToString("HH:mm:ss"), "系统", type, message, "已处理"));
        while (_runtimeEvents.Count > 8)
        {
            _runtimeEvents.RemoveAt(_runtimeEvents.Count - 1);
        }
        RefreshRfidAlarmDisplay();
    }

    public void SetRecentAlarmRecords(IEnumerable<PassageRecord> records)
    {
        if (records is null) throw new ArgumentNullException(nameof(records));

        _recentAlarmRecords = records
            .Where(record => record.IsAlert)
            .OrderByDescending(record => record.CompletedAt)
            .Take(6)
            .ToArray();
        RefreshRfidAlarmDisplay();
    }

    public void SetRecognitionStatus(string? message, bool isAlarm)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            RecognitionNotice.Visibility = Visibility.Collapsed;
            RecognitionNoticeText.Text = string.Empty;
            return;
        }

        RecognitionNotice.Visibility = Visibility.Visible;
        RecognitionNotice.Background = new SolidColorBrush(isAlarm
            ? Color.FromArgb(90, 133, 24, 40)
            : Color.FromArgb(90, 126, 91, 18));
        RecognitionNotice.BorderBrush = new SolidColorBrush(isAlarm
            ? Color.FromRgb(255, 135, 149)
            : Color.FromRgb(255, 226, 138));
        RecognitionNoticeText.Foreground = new SolidColorBrush(isAlarm
            ? Color.FromRgb(255, 135, 149)
            : Color.FromRgb(255, 226, 138));
        RecognitionNoticeText.Text = message;
    }

    public void SetRecognitionSnapshot(RfidStationFrame frame, StationRecognitionSession session)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (session is null) throw new ArgumentNullException(nameof(session));

        RecognitionSnapshotText.Text =
            $"基站 {frame.StationAddress:X2} · 上报 {frame.ReportedCardCount} · 实际 {frame.ActualNonZeroSlotCount} · 当前列车：{session.DetectedVehicleCount}/{session.ExpectedVehicleCount}";
    }

    public void SetRfidRuntimeStates(IEnumerable<StationRuntimeState> states)
    {
        if (states is null) throw new ArgumentNullException(nameof(states));

        var snapshot = states.ToArray();
        _rfidRuntimeStates = snapshot;
        var onlineCount = snapshot.Count(state => state.CommunicationState == StationCommunicationState.Online);
        var recognizingCount = snapshot.Count(state => state.LifecycleState == PassageLifecycleState.Recognizing);
        var alarmCount = snapshot.Count(IsUncouplingAlarm);
        RealtimeStationSummaryText.Text = $"在线基站 {onlineCount}/{snapshot.Length}";
        RecognizingSummaryText.Text = $"正在识别 {recognizingCount}";
        AlarmSummaryText.Text = $"当前报警 {alarmCount}";
        RefreshRfidOverviewMetrics();
        RefreshRfidTaskCards();
        RefreshRfidAlarmDisplay();
        Map.SetRfidRuntimeStates(snapshot);
        UpdateSystemStatusIndicators();
        ApplySelectedRfidRuntimeState();

        var active = snapshot
            .OrderByDescending(state => state.VisualState == RfidStationVisualState.Alarm)
            .ThenByDescending(state => state.VisualState == RfidStationVisualState.Warning)
            .ThenByDescending(state => state.LifecycleState != PassageLifecycleState.Idle)
            .FirstOrDefault();
        if (active is null)
        {
            RecognitionSnapshotText.Text = "当前列车：-";
            return;
        }

        var head = active.CurrentHeadRfid?.ToString("X4") ?? "-";
        RecognitionSnapshotText.Text =
            $"基站 {active.StationAddress:X2} · {GetRuntimeStateText(active)} · 当前列车：{head} · {active.DetectedVehicleCount}/{active.ExpectedVehicleCount}";
    }

    private void UpdateSystemStatusIndicators()
    {
        ApplySystemIndicator(
            SystemDatabaseStatusDot,
            SystemDatabaseStatusText,
            _databaseHealthy ? "数据库 正常" : "数据库 异常",
            _databaseHealthy ? SystemHealthState.Healthy : SystemHealthState.Error);

        var enabledStationCount = _rfidStations.Count(station => station.Enabled);
        var totalStationCount = _rfidRuntimeStates.Count > 0
            ? _rfidRuntimeStates.Count
            : enabledStationCount;
        var onlineStationCount = _rfidRuntimeStates.Count(state => state.CommunicationState == StationCommunicationState.Online);
        var rfidState = !_rfidListenerHealthy
            ? SystemHealthState.Error
            : totalStationCount == 0
                ? SystemHealthState.Unavailable
                : onlineStationCount == totalStationCount
                    ? SystemHealthState.Healthy
                    : SystemHealthState.Warning;
        var rfidText = !_rfidListenerHealthy
            ? "RFID监听 异常"
            : totalStationCount == 0
                ? "RFID基站 未配置"
                : $"RFID基站 在线 {onlineStationCount}/{totalStationCount}";
        ApplySystemIndicator(SystemRfidStatusDot, SystemRfidStatusText, rfidText, rfidState);

        ApplySystemIndicator(
            SystemExternalStatusDot,
            SystemExternalStatusText,
            _externalInterfaceAvailable ? "外部接口 正常" : "外部接口 未配置",
            _externalInterfaceAvailable ? SystemHealthState.Healthy : SystemHealthState.Unavailable);
    }

    private static void ApplySystemIndicator(
        Ellipse dot,
        TextBlock text,
        string label,
        SystemHealthState state)
    {
        var brush = GetSystemStatusBrush(state);
        dot.Fill = brush;
        dot.Stroke = brush;
        dot.Effect = new DropShadowEffect
        {
            Color = brush is SolidColorBrush solid ? solid.Color : Colors.Gray,
            BlurRadius = 8,
            ShadowDepth = 0,
            Opacity = 0.65
        };
        text.Text = label;
        text.Foreground = brush;
    }

    private static Brush GetSystemStatusBrush(SystemHealthState state)
    {
        var resourceKey = state switch
        {
            SystemHealthState.Healthy => "SuccessBrush",
            SystemHealthState.Warning => "WarningBrush",
            SystemHealthState.Error => "AlarmBrush",
            _ => "OfflineBrush"
        };
        return Application.Current?.TryFindResource(resourceKey) as Brush ?? Brushes.Gray;
    }

    public void SetHistoricalStatistics(PassageStatistics statistics)
    {
        if (statistics is null) throw new ArgumentNullException(nameof(statistics));

        TodayPassageSummaryText.Text = $"今日通过 {statistics.TodayPassageCount}";
        TodayNormalSummaryText.Text = $"今日正常 {statistics.TodayNormalCount}";
        TodayAlarmSummaryText.Text = $"今日脱节 {statistics.TodayAlarmCount}";
        OverviewTodayPassageText.Text = statistics.TodayPassageCount.ToString(CultureInfo.InvariantCulture);
        OverviewTodayAlarmText.Text = statistics.TodayAlarmCount.ToString(CultureInfo.InvariantCulture);
        var stationIds = _rfidStations.Select(station => station.StationId)
            .Concat(_rfidRuntimeStates.Select(state => state.StationId))
            .Concat(statistics.ByStation.Keys)
            .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(stationId => stationId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        StationHistoricalSummaryText.Text = stationIds.Length == 0
            ? "按基站通过：-"
            : $"按基站通过：{string.Join(" · ", stationIds.Select(stationId => $"{stationId} {(statistics.ByStation.TryGetValue(stationId, out var count) ? count : 0)}"))}";
    }

    private void RefreshRfidOverviewMetrics()
    {
        var configuredCount = _rfidStations.Count;
        var totalCount = configuredCount > 0 ? configuredCount : _rfidRuntimeStates.Count;
        var onlineCount = _rfidRuntimeStates.Count(state => state.CommunicationState == StationCommunicationState.Online);
        var alarmCount = _rfidRuntimeStates.Count(IsUncouplingAlarm);
        var enabledCount = _rfidStations.Count(station => station.Enabled);
        var offlineCount = configuredCount > 0
            ? Math.Max(0, enabledCount - onlineCount)
            : _rfidRuntimeStates.Count(state => state.CommunicationState == StationCommunicationState.Offline);

        OverviewTotalCountText.Text = totalCount.ToString(CultureInfo.InvariantCulture);
        OverviewOnlineCountText.Text = onlineCount.ToString(CultureInfo.InvariantCulture);
        OverviewAlarmCountText.Text = alarmCount.ToString(CultureInfo.InvariantCulture);
        OverviewOfflineCountText.Text = offlineCount.ToString(CultureInfo.InvariantCulture);
        OverviewPollIntervalText.Text = $"{_pollIntervalMs.ToString(CultureInfo.InvariantCulture)}ms";
        OverviewLastPollText.Text = _lastPollAt?.ToLocalTime().ToString("HH:mm:ss") ?? "-";
        OverviewDatabaseDisplayText.Text = _databaseHealthy ? "正常" : "异常";
        OverviewDatabaseDisplayText.Foreground = GetSystemStatusBrush(
            _databaseHealthy ? SystemHealthState.Healthy : SystemHealthState.Error);
        OverviewExternalDisplayText.Text = _externalInterfaceAvailable ? "正常" : "未配置";
        OverviewExternalDisplayText.Foreground = GetSystemStatusBrush(
            _externalInterfaceAvailable ? SystemHealthState.Healthy : SystemHealthState.Unavailable);
    }

    private void RefreshRfidTaskCards()
    {
        var statesById = _rfidRuntimeStates
            .Where(state => !string.IsNullOrWhiteSpace(state.StationId))
            .GroupBy(state => state.StationId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var configurationsById = _rfidStations
            .Where(station => !string.IsNullOrWhiteSpace(station.StationId))
            .ToDictionary(station => station.StationId, StringComparer.OrdinalIgnoreCase);
        var stationIds = configurationsById.Keys
            .Concat(statesById.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var cards = stationIds
            .Select(stationId => BuildRfidTaskCard(
                configurationsById.TryGetValue(stationId, out var configuration) ? configuration : null,
                statesById.TryGetValue(stationId, out var state) ? state : null))
            .ToArray();
        _allRfidTaskCards = cards;
        RfidTaskItemsControl.Tag = cards.Length;
        ApplyTaskCardFilter();

        RfidTaskSummaryText.Text = cards.Length.ToString(CultureInfo.InvariantCulture);
        RfidTaskRunningCountText.Text = cards.Count(card => card.TaskCategory == "运行中").ToString(CultureInfo.InvariantCulture);
        RfidTaskPendingCountText.Text = cards.Count(card => card.TaskCategory == "待发").ToString(CultureInfo.InvariantCulture);
        RfidTaskAlarmCountText.Text = cards.Count(card => card.TaskCategory == "异常").ToString(CultureInfo.InvariantCulture);
        RfidTaskOfflineCountText.Text = cards.Count(card => card.TaskCategory == "离线").ToString(CultureInfo.InvariantCulture);
    }

    private void ApplyTaskCardFilter()
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(_allRfidTaskCards.Count / (double)RfidTaskPageSize));
        _rfidTaskPageIndex = Math.Min(_rfidTaskPageIndex, pageCount - 1);

        _rfidTaskCards.Clear();
        foreach (var card in _allRfidTaskCards
                     .Skip(_rfidTaskPageIndex * RfidTaskPageSize)
                     .Take(RfidTaskPageSize))
        {
            _rfidTaskCards.Add(card);
        }

        RfidTaskPaginationPanel.Visibility = pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        RfidTaskPageText.Text = $"{_rfidTaskPageIndex + 1} / {pageCount}";
        RfidTaskPreviousPageButton.IsEnabled = _rfidTaskPageIndex > 0;
        RfidTaskNextPageButton.IsEnabled = _rfidTaskPageIndex < pageCount - 1;
    }

    private void OnRfidTaskPreviousPageClick(object sender, RoutedEventArgs e)
    {
        if (_rfidTaskPageIndex <= 0)
        {
            return;
        }

        _rfidTaskPageIndex--;
        ApplyTaskCardFilter();
    }

    private void OnRfidTaskNextPageClick(object sender, RoutedEventArgs e)
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(_allRfidTaskCards.Count / (double)RfidTaskPageSize));
        if (_rfidTaskPageIndex >= pageCount - 1)
        {
            return;
        }

        _rfidTaskPageIndex++;
        ApplyTaskCardFilter();
    }

    private RfidTaskCardViewModel BuildRfidTaskCard(RfidStationConfig? configuration, StationRuntimeState? state)
    {
        var stationId = configuration?.StationId ?? state?.StationId ?? "-";
        var stationDisplayId = configuration is null
            ? stationId
            : RfidStationIdentity.GetDisplayId(configuration.StationId, configuration.YardId);
        var statusText = GetTaskStatusText(configuration, state);
        var statusBrush = GetTaskStatusBrush(statusText);
        var expected = state?.ExpectedVehicleCount ?? 0;
        var detected = state?.DetectedVehicleCount ?? 0;
        var isOffline = state is null || state.CommunicationState == StationCommunicationState.Offline;
        var hasPassage = state is not null && state.LifecycleState != PassageLifecycleState.Idle;
        var progressValue = !isOffline && hasPassage && expected > 0 ? Math.Min(100, detected * 100d / expected) : 0;
        var latestAt = state?.LastNewVehicleAt ?? state?.LastResponseAt;
        var isActive = state is not null && state.CommunicationState == StationCommunicationState.Online &&
                       state.LifecycleState != PassageLifecycleState.Idle;

        return new RfidTaskCardViewModel(
            stationId,
            stationDisplayId,
            GetRfidMapPointName(stationId),
            statusText,
            hasPassage ? state?.CurrentHeadRfid?.ToString("X4") ?? "-" : "--",
            hasPassage && expected > 0 ? $"{detected} / {expected}" : "--",
            progressValue,
            isOffline ? "等待连接" : latestAt?.ToLocalTime().ToString("HH:mm:ss") ?? "-",
            isActive,
            isOffline || !hasPassage ? Visibility.Collapsed : Visibility.Visible,
            statusBrush,
            CreateStatusBackground(statusBrush),
            statusBrush);
    }

    private string GetRfidMapPointName(string stationId)
    {
        var binding = _currentStation is null
            ? null
            : RfidStationBindingRules.FindBindings(new[] { _currentStation }, stationId).FirstOrDefault();
        if (binding is not null)
        {
            return string.IsNullOrWhiteSpace(binding.DeviceName) ? binding.DeviceId : binding.DeviceName;
        }

        return "未绑定地图点位";
    }

    private void RefreshRfidAlarmDisplay()
    {
        var alarms = new List<RfidAlarmDisplayItem>();
        alarms.AddRange(_rfidRuntimeStates
            .Where(IsAlertState)
            .Select(CreateRuntimeAlertItem));
        alarms.AddRange(_recentAlarmRecords
            .Where(record => _visibleRfidStationIds.Contains(record.StationId))
            .Select(record => new RfidAlarmDisplayItem(
                record.CompletedAt.ToLocalTime().ToString("HH:mm:ss"),
                GetDisplayRfidStationId(record.StationId),
                record.IsWarningOnly ? "警告" : record.AlarmMessage ?? "报警",
                record.IsWarningOnly
                    ? $"{string.Join("；", record.WarningMessages)}；已识别 {record.DetectedVehicleCount}/{record.ExpectedVehicleCount} 节"
                    : $"已识别 {record.DetectedVehicleCount}/{record.ExpectedVehicleCount} 节，缺少 {Math.Max(record.ExpectedVehicleCount - record.DetectedVehicleCount, 0)} 节",
                record.CompletedAt,
                record.PassageId)));
        alarms.AddRange(_runtimeEvents
            .Where(IsAlarmEvent)
            .Select(runtimeEvent => new RfidAlarmDisplayItem(
                runtimeEvent.Time,
                string.IsNullOrWhiteSpace(runtimeEvent.TrainNumber) || runtimeEvent.TrainNumber == "系统" ? "-" : runtimeEvent.TrainNumber,
                runtimeEvent.Type,
                runtimeEvent.Message,
                DateTimeOffset.MinValue)));

        _rfidAlarmItems.Clear();
        var seenPassages = new HashSet<Guid>();
        foreach (var alarm in alarms.OrderByDescending(item => item.SortAt))
        {
            if (alarm.PassageId.HasValue && !seenPassages.Add(alarm.PassageId.Value))
            {
                continue;
            }

            _rfidAlarmItems.Add(alarm);
            if (_rfidAlarmItems.Count >= 6)
            {
                break;
            }
        }

        RfidAlarmCountText.Text = _rfidAlarmItems.Count.ToString(CultureInfo.InvariantCulture);
        RfidAlarmEmptyText.Visibility = _rfidAlarmItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsAlarmEvent(DashboardEvent runtimeEvent) =>
        runtimeEvent.Type.IndexOf("报警", StringComparison.OrdinalIgnoreCase) >= 0 ||
        runtimeEvent.Message.IndexOf("报警", StringComparison.OrdinalIgnoreCase) >= 0 ||
        runtimeEvent.Message.IndexOf("脱节", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsAlertState(StationRuntimeState state) =>
        IsUncouplingAlarm(state) || state.WarningMessages.Count > 0;

    private static RfidAlarmDisplayItem CreateRuntimeAlertItem(StationRuntimeState state)
    {
        var isAlarm = IsUncouplingAlarm(state);
        var warningText = string.Join("；", state.WarningMessages);
        return new RfidAlarmDisplayItem(
            state.LastResponseAt?.ToLocalTime().ToString("HH:mm:ss") ?? "-",
            state.StationName,
            isAlarm ? state.AlarmMessage ?? "报警" : "警告",
            isAlarm
                ? string.IsNullOrWhiteSpace(state.AlarmMessage) ? "状态进入报警" : state.AlarmMessage!
                : warningText,
            state.LastResponseAt ?? state.SessionStartedAt ?? DateTimeOffset.MinValue,
            state.LastPassageRecord?.PassageId);
    }

    private void OnRfidAlarmMoreClick(object sender, RoutedEventArgs e)
    {
        AlarmMoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string GetTaskStatusText(RfidStationConfig? configuration, StationRuntimeState? state)
    {
        if (configuration?.Enabled == false)
        {
            return "已禁用";
        }

        if (state is not null && IsUncouplingAlarm(state))
        {
            return "脱节报警";
        }

        if (state is null || state.CommunicationState == StationCommunicationState.Offline)
        {
            return "离线";
        }

        return state.LifecycleState switch
        {
            PassageLifecycleState.Recognizing => state.WarningMessages.Count > 0 ? "识别不完整" : "识别中",
            PassageLifecycleState.Alarm => "脱节报警",
            PassageLifecycleState.Finalizing => "记录保存中",
            PassageLifecycleState.Clearing => "正在清除",
            PassageLifecycleState.WaitForEmpty => "等待清空确认",
            PassageLifecycleState.Completed => "已完成",
            _ => "空闲"
        };
    }

    private static string GetTaskCategory(string statusText, bool isActive)
    {
        if (statusText is "脱节报警" or "识别不完整")
        {
            return "异常";
        }

        if (statusText is "离线" or "已禁用")
        {
            return "离线";
        }

        return isActive ? "运行中" : "待发";
    }

    private static Brush GetTaskStatusBrush(string statusText) => statusText switch
    {
        "识别中" => GetResourceBrush("AccentBrush"),
        "识别不完整" or "正在清除" => GetResourceBrush("WarningBrush"),
        "脱节报警" => GetResourceBrush("AlarmBrush"),
        "离线" or "已禁用" or "等待清空确认" => GetResourceBrush("OfflineBrush"),
        _ => GetResourceBrush("SuccessBrush")
    };

    private static Brush GetResourceBrush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

    private static bool IsUncouplingAlarm(StationRuntimeState state) =>
        state.VisualState == RfidStationVisualState.Alarm ||
        state.LifecycleState == PassageLifecycleState.Alarm ||
        !string.IsNullOrWhiteSpace(state.AlarmMessage) ||
        state.LastPassageRecord?.Outcome == PassageOutcome.UncouplingAlarm;

    private static Brush CreateStatusBackground(Brush brush)
    {
        var color = brush is SolidColorBrush solid ? solid.Color : Colors.Gray;
        return new SolidColorBrush(Color.FromArgb(55, color.R, color.G, color.B));
    }

    private sealed class RfidTaskCardViewModel
    {
        public RfidTaskCardViewModel(
            string stationId,
            string stationDisplayId,
            string mapPointName,
            string statusText,
            string headRfid,
            string progressText,
            double progressValue,
            string latestRecognitionTime,
            bool isActive,
            Visibility progressVisibility,
            Brush borderBrush,
            Brush statusBackground,
            Brush statusForeground)
        {
            StationId = stationId;
            StationDisplayId = stationDisplayId;
            MapPointName = mapPointName;
            StatusText = statusText;
            HeadRfid = headRfid;
            ProgressText = progressText;
            ProgressValue = progressValue;
            ProgressPercentageText = progressVisibility == Visibility.Visible ? $"{Math.Round(progressValue):0}%" : string.Empty;
            LatestRecognitionTime = latestRecognitionTime;
            IsActive = isActive;
            ProgressVisibility = progressVisibility;
            TaskCategory = GetTaskCategory(statusText, isActive);
            BorderBrush = borderBrush;
            StatusBackground = statusBackground;
            StatusForeground = statusForeground;
            ProgressBrush = statusForeground;
        }

        public string StationId { get; }

        public string StationDisplayId { get; }

        public string MapPointName { get; }

        public string StatusText { get; }

        public string HeadRfid { get; }

        public string ProgressText { get; }

        public double ProgressValue { get; }

        public string ProgressPercentageText { get; }

        public string LatestRecognitionTime { get; }

        public bool IsActive { get; }

        public Visibility ProgressVisibility { get; }

        public string TaskCategory { get; }

        public Brush BorderBrush { get; }

        public Brush StatusBackground { get; }

        public Brush StatusForeground { get; }

        public Brush ProgressBrush { get; }
    }

    private sealed class RfidAlarmDisplayItem
    {
        public RfidAlarmDisplayItem(
            string time,
            string station,
            string alarmType,
            string alarmDescription,
            DateTimeOffset sortAt,
            Guid? passageId = null)
        {
            Time = time;
            Station = station;
            AlarmType = alarmType;
            AlarmDescription = alarmDescription;
            SortAt = sortAt;
            PassageId = passageId;
        }

        public string Time { get; }

        public string Station { get; }

        public string AlarmType { get; }

        public string AlarmDescription { get; }

        public DateTimeOffset SortAt { get; }

        public Guid? PassageId { get; }
    }

    private void OnDeviceSelected(object? sender, DeviceSelectedEventArgs e)
    {
        if (_isMapAnnotationEditing)
        {
            SelectAnnotation(MapAnnotationKind.RfidStation, e.Device.Id);
            return;
        }

        SelectRfidDevice(e.Device, e.Status);
    }

    private void SelectRfidDevice(DeviceConfig device, DeviceStatus status)
    {
        _selectedDevice = device;
        _selectedRfidStationId = device.Type == DeviceType.RfidStation
            ? RfidMapBindingResolver.Resolve(device, _rfidStations).Configuration?.StationId ?? device.RfidStationId
            : null;
        SelectedDeviceTitle.Text = GetEffectiveRfidName(device);
        SelectedDeviceId.Text = device.Id;
        SelectedDeviceStation.Text = GetDeviceLocation(device.Id);
        SelectedDeviceCadX.Text = device.CadX.ToString("0.####");
        SelectedDeviceCadY.Text = device.CadY.ToString("0.####");
        SelectedDeviceStatus.Text = GetStatusText(status);
        ApplySelectedRfidRuntimeState();
        Map.SetSelectedAnnotation(MapAnnotationKind.RfidStation, device.Id);
        AddSystemEvent("选择", $"选择 {device.Id}");
    }

    private void OnRfidTaskCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isMapAnnotationEditing || (sender as FrameworkElement)?.DataContext is not RfidTaskCardViewModel card)
        {
            return;
        }

        _selectedRfidStationId = card.StationId;
        _selectedDevice = _currentStation?.Devices.FirstOrDefault(device =>
            device.Type == DeviceType.RfidStation &&
            string.Equals(device.RfidStationId, card.StationId, StringComparison.OrdinalIgnoreCase));
        if (_selectedDevice is not null)
        {
            SelectedDeviceTitle.Text = GetEffectiveRfidName(_selectedDevice);
            SelectedDeviceId.Text = _selectedDevice.Id;
            SelectedDeviceCadX.Text = _selectedDevice.CadX.ToString("0.####", CultureInfo.InvariantCulture);
            SelectedDeviceCadY.Text = _selectedDevice.CadY.ToString("0.####", CultureInfo.InvariantCulture);
            Map.SetSelectedAnnotation(MapAnnotationKind.RfidStation, _selectedDevice.Id);
        }

        ApplySelectedRfidRuntimeState();
        AddSystemEvent("选择", $"选择 {card.StationId}");
        e.Handled = true;
    }

    private void OnAnnotationClicked(object? sender, MapAnnotationHitEventArgs e)
    {
        if (!_isMapAnnotationEditing || !_adminModeService.IsAdmin)
        {
            ShowReadOnlyAnnotation(e);
            return;
        }

        if (_annotationTool == MapAnnotationTool.Delete)
        {
            _ = DeleteAnnotationAsync(e.Kind, e.AnnotationId);
            return;
        }

        SelectAnnotation(e.Kind, e.AnnotationId);
    }

    private void OnCanvasClicked(object? sender, MapCanvasPointEventArgs e)
    {
        if (!_isMapAnnotationEditing || !_adminModeService.IsAdmin || !IsAddTool(_annotationTool))
        {
            return;
        }

        var kind = _annotationTool switch
        {
            MapAnnotationTool.MapPoint => MapAnnotationKind.MapPoint,
            MapAnnotationTool.RfidStation => MapAnnotationKind.RfidStation,
            MapAnnotationTool.MapLabel => MapAnnotationKind.MapLabel,
            _ => (MapAnnotationKind?)null
        };
        if (kind.HasValue)
        {
            BeginNewAnnotation(kind.Value, e.CadPoint);
        }
    }

    private void OnAnnotationDragged(object? sender, MapAnnotationDragEventArgs e)
    {
        if (!_isMapAnnotationEditing || !_adminModeService.IsAdmin || _annotationEditor is null ||
            _annotationTool != MapAnnotationTool.Select || !_annotationEditor.TryMove(e.Kind, e.AnnotationId, e.CadPoint))
        {
            return;
        }

        SelectAnnotation(e.Kind, e.AnnotationId);
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Map.ZoomIn();

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Map.ZoomOut();

    private void OnResetClick(object sender, RoutedEventArgs e) => ResetRequested?.Invoke(this, EventArgs.Empty);

    private void OnMapAnnotationClick(object sender, RoutedEventArgs e)
    {
        if (!_adminModeService.IsAdmin || _currentStation is null)
        {
            return;
        }

        if (!_isMapAnnotationEditing)
        {
            _annotationEditor = new MapAnnotationEditor(_currentStation, _adminModeService, _allYards, _rfidStations);
            _isMapAnnotationEditing = true;
            SetAnnotationTool(MapAnnotationTool.Select);
            AnnotationToolbar.Visibility = Visibility.Visible;
            MapAnnotationButton.Visibility = Visibility.Collapsed;
            SetAnnotationEditorVisibility(false);
            AddSystemEvent("地图标注", "已进入地图标注编辑模式");
        }
    }

    private void OnMapToolClick(object sender, RoutedEventArgs e)
    {
        if (!_isMapAnnotationEditing || !_adminModeService.IsAdmin || sender is not Button button ||
            button.Tag is not string toolName || !Enum.TryParse<MapAnnotationTool>(toolName, out var tool))
        {
            return;
        }

        SetAnnotationTool(tool);
        if (tool != MapAnnotationTool.Select)
        {
            SetAnnotationEditorVisibility(false);
        }
    }

    private async void OnSaveAnnotationsClick(object sender, RoutedEventArgs e) => await SaveMapAnnotationsAsync();

    private async void OnExitMapAnnotationEditClick(object sender, RoutedEventArgs e) => await TryLeaveMapEditingAsync("退出编辑");

    private async void OnDeleteAnnotationClick(object sender, RoutedEventArgs e)
    {
        var selectedId = _selectedAnnotationId;
        if (_selectedAnnotationKind.HasValue && !string.IsNullOrWhiteSpace(selectedId))
        {
            await DeleteAnnotationAsync(_selectedAnnotationKind.Value, selectedId!);
        }
    }

    private void OnAnnotationApplyClick(object sender, RoutedEventArgs e)
    {
        if (!_isMapAnnotationEditing || !_adminModeService.IsAdmin || _annotationEditor is null ||
            !_pendingAnnotationKind.HasValue || string.IsNullOrWhiteSpace(_selectedAnnotationId))
        {
            return;
        }

        if (!TryReadAnnotationFields(out var name, out var cadX, out var cadY, out var rotation, out var textHeight, out var rfidStationId))
        {
            return;
        }

        var kind = _pendingAnnotationKind.Value;
        var changed = _isNewAnnotation
            ? AddAnnotation(kind, name, cadX, cadY, rotation, textHeight, rfidStationId)
            : UpdateAnnotation(kind, _selectedAnnotationId!, name, cadX, cadY, rotation, textHeight, rfidStationId);
        if (!changed)
        {
            if (_annotationEditor.LastBindingError is not null)
            {
                ShowMessageDialog("绑定被阻止", _annotationEditor.LastBindingError, MessageDialogKind.Warning);
            }
            return;
        }

        _isNewAnnotation = false;
        SetAnnotationTool(MapAnnotationTool.Select);
        RefreshAnnotationMap();
        SelectAnnotation(kind, _selectedAnnotationId!);
    }

    private void OnAnnotationCancelClick(object sender, RoutedEventArgs e)
    {
        SetAnnotationEditorVisibility(false);
        _pendingAnnotationKind = null;
        _isNewAnnotation = false;
        _selectedAnnotationKind = null;
        _selectedAnnotationId = null;
        Map.SetSelectedAnnotation(null, null);
        SetAnnotationTool(MapAnnotationTool.Select);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isMapAnnotationEditing)
        {
            OnAnnotationCancelClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && _isMapAnnotationEditing && _adminModeService.IsAdmin &&
            Keyboard.FocusedElement is not TextBox && _selectedAnnotationKind.HasValue &&
            _selectedAnnotationId is not null)
        {
            _ = DeleteAnnotationAsync(_selectedAnnotationKind.Value, _selectedAnnotationId);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && _isMapAnnotationEditing &&
            _adminModeService.IsAdmin && _annotationEditor is not null && Keyboard.FocusedElement is not TextBox &&
            _annotationEditor.TryUndo())
        {
            RefreshAnnotationMap();
            var selectedKind = _selectedAnnotationKind;
            var selectedId = _selectedAnnotationId;
            if (selectedKind.HasValue && selectedId is not null &&
                FindAnnotation(_annotationEditor.WorkingCopy, selectedKind.Value, selectedId) is not null)
            {
                SelectAnnotation(selectedKind.Value, selectedId);
            }
            else
            {
                _selectedAnnotationKind = null;
                _selectedAnnotationId = null;
                SetAnnotationEditorVisibility(false);
                Map.SetSelectedAnnotation(null, null);
            }

            e.Handled = true;
        }
    }

    private void BeginNewAnnotation(MapAnnotationKind kind, CadPoint point)
    {
        _pendingAnnotationKind = kind;
        _selectedAnnotationKind = kind;
        _selectedAnnotationId = $"map-{Guid.NewGuid():N}";
        _isNewAnnotation = true;
        AnnotationEditorTitle.Text = kind switch
        {
            MapAnnotationKind.MapPoint => "新增普通点位",
            MapAnnotationKind.RfidStation => "新增 RFID 基站",
            MapAnnotationKind.MapLabel => "新增文字标注",
            _ => "地图标注属性"
        };
        AnnotationTypeText.Text = GetAnnotationTypeText(kind);
        AnnotationNameBox.Text = kind == MapAnnotationKind.RfidStation ? "临时RFID" : "Y6-18";
        AnnotationTextBox.Text = kind == MapAnnotationKind.MapLabel ? "新文字" : string.Empty;
        AnnotationCadXBox.Text = point.X.ToString("0.####", CultureInfo.InvariantCulture);
        AnnotationCadYBox.Text = point.Y.ToString("0.####", CultureInfo.InvariantCulture);
        AnnotationRfidStationComboBox.SelectedValue = null;
        AnnotationRotationBox.Text = "0";
        AnnotationTextHeightBox.Text = "默认";
        AnnotationEnabledCheckBox.IsChecked = true;
        UpdateAnnotationFieldVisibility(kind);
        SetAnnotationEditorVisibility(true);
        Map.SetSelectedAnnotation(null, null);
    }

    private void SelectAnnotation(MapAnnotationKind kind, string id)
    {
        if (_annotationEditor is null)
        {
            return;
        }

        var annotation = FindAnnotation(_annotationEditor.WorkingCopy, kind, id);
        if (annotation is null)
        {
            return;
        }

        _selectedAnnotationKind = kind;
        _selectedAnnotationId = id;
        _pendingAnnotationKind = kind;
        _isNewAnnotation = false;
        SetAnnotationTool(MapAnnotationTool.Select);
        AnnotationEditorTitle.Text = "地图标注属性";
        AnnotationTypeText.Text = GetAnnotationTypeText(kind);
        switch (annotation)
        {
            case MapPoint point:
                AnnotationRfidStationComboBox.SelectedValue = null;
                AnnotationNameBox.Text = point.Name;
                AnnotationCadXBox.Text = point.CadX.ToString("0.####", CultureInfo.InvariantCulture);
                AnnotationCadYBox.Text = point.CadY.ToString("0.####", CultureInfo.InvariantCulture);
                AnnotationEnabledCheckBox.IsChecked = point.Enabled;
                break;
            case DeviceConfig device:
                var binding = RfidMapBindingResolver.Resolve(device, _rfidStations);
                AnnotationNameBox.Text = string.IsNullOrWhiteSpace(device.Name) ? binding.EffectiveName : device.Name;
                AnnotationCadXBox.Text = device.CadX.ToString("0.####", CultureInfo.InvariantCulture);
                AnnotationCadYBox.Text = device.CadY.ToString("0.####", CultureInfo.InvariantCulture);
                AnnotationRfidStationComboBox.SelectedValue = device.RfidStationId;
                AnnotationEnabledCheckBox.IsChecked = device.Enabled;
                break;
            case MapLabel label:
                AnnotationRfidStationComboBox.SelectedValue = null;
                AnnotationTextBox.Text = label.Text;
                AnnotationCadXBox.Text = label.CadX.ToString("0.####", CultureInfo.InvariantCulture);
                AnnotationCadYBox.Text = label.CadY.ToString("0.####", CultureInfo.InvariantCulture);
                AnnotationRotationBox.Text = label.Rotation.ToString("0.####", CultureInfo.InvariantCulture);
                AnnotationTextHeightBox.Text = label.TextHeight.ToString("0.####", CultureInfo.InvariantCulture);
                AnnotationEnabledCheckBox.IsChecked = label.Enabled;
                break;
        }

        UpdateAnnotationFieldVisibility(kind);
        SetAnnotationEditorVisibility(true);
        Map.SetSelectedAnnotation(kind, id);
    }

    private bool AddAnnotation(MapAnnotationKind kind, string name, double cadX, double cadY, double rotation, double textHeight, string? rfidStationId)
    {
        var id = _selectedAnnotationId!;
        return kind switch
        {
            MapAnnotationKind.MapPoint => _annotationEditor!.TryAddMapPoint(new MapPoint
            {
                Id = id, Name = name, CadX = cadX, CadY = cadY, Enabled = AnnotationEnabledCheckBox.IsChecked != false
            }),
            MapAnnotationKind.RfidStation => _annotationEditor!.TryAddRfidStation(new DeviceConfig
            {
                Id = id,
                Name = FindRfidStation(rfidStationId)?.Name ?? name,
                Type = DeviceType.RfidStation,
                StationId = _currentStation!.Id,
                RfidStationId = rfidStationId,
                CadX = cadX,
                CadY = cadY,
                Enabled = AnnotationEnabledCheckBox.IsChecked != false
            }),
            MapAnnotationKind.MapLabel => _annotationEditor!.TryAddMapLabel(new MapLabel
            {
                Id = id, Text = name, CadX = cadX, CadY = cadY, Rotation = rotation, TextHeight = textHeight,
                Enabled = AnnotationEnabledCheckBox.IsChecked != false
            }),
            _ => false
        };
    }

    private bool UpdateAnnotation(MapAnnotationKind kind, string id, string name, double cadX, double cadY, double rotation, double textHeight, string? rfidStationId)
    {
        var enabled = AnnotationEnabledCheckBox.IsChecked != false;
        return kind switch
        {
            MapAnnotationKind.MapPoint => _annotationEditor!.TryUpdateMapPoint(id, name, cadX, cadY, enabled),
            MapAnnotationKind.RfidStation => _annotationEditor!.TryUpdateRfidStation(
                id,
                name,
                cadX,
                cadY,
                rfidStationId,
                enabled),
            MapAnnotationKind.MapLabel => _annotationEditor!.TryUpdateMapLabel(id, name, cadX, cadY, rotation, textHeight, enabled),
            _ => false
        };
    }

    private async Task DeleteAnnotationAsync(MapAnnotationKind kind, string id)
    {
        if (!_isMapAnnotationEditing || !_adminModeService.IsAdmin || _annotationEditor is null)
        {
            return;
        }

        var annotation = FindAnnotation(_annotationEditor.WorkingCopy, kind, id);
        if (annotation is null)
        {
            return;
        }

        var displayName = annotation switch
        {
            MapPoint point => point.Name,
            DeviceConfig device => GetEffectiveRfidName(device),
            MapLabel label => label.Text,
            _ => id
        };
        var dialog = new DeleteMapAnnotationDialog(displayName)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true || !dialog.Confirmed || !_annotationEditor.TryDelete(kind, id))
        {
            return;
        }

        _selectedAnnotationKind = null;
        _selectedAnnotationId = null;
        SetAnnotationEditorVisibility(false);
        Map.SetSelectedAnnotation(null, null);
        RefreshAnnotationMap();
        AddSystemEvent("地图标注", $"已删除 {displayName}");
        await Task.CompletedTask;
    }

    private async Task<bool> SaveMapAnnotationsAsync()
    {
        if (!_adminModeService.IsAdmin || _annotationEditor is null || _currentStation is null ||
            !_annotationEditor.TryGetSaveSnapshot(out var snapshot))
        {
            return false;
        }

        var save = await _configService.SaveStationAsync(_projectDirectory, snapshot);
        if (!save.Succeeded)
        {
            ShowMessageDialog("保存失败", string.Join(Environment.NewLine, save.Errors), MessageDialogKind.Error);
            return false;
        }

        CopyStationData(_currentStation, snapshot);
        _annotationEditor.MarkSaved();
        MapAnnotationsSaved?.Invoke(this, EventArgs.Empty);
        RefreshAnnotationMap();
        AddSystemEvent("地图标注", "地图标注已保存");
        ShowMessageDialog("地图标注", "地图标注已保存", MessageDialogKind.Information);
        return true;
    }

    private void ExitMapAnnotationEditModeWithoutPrompt()
    {
        _isMapAnnotationEditing = false;
        _annotationTool = MapAnnotationTool.Select;
        _annotationEditor = null;
        _selectedAnnotationKind = null;
        _selectedAnnotationId = null;
        _pendingAnnotationKind = null;
        _isNewAnnotation = false;
        AnnotationToolbar.Visibility = Visibility.Collapsed;
        SetAnnotationEditorVisibility(false);
        Map.SetSelectedAnnotation(null, null);
        Map.SetAnnotationEditMode(false, MapAnnotationTool.Select);
        UpdateAdminMapControls();
    }

    private void RefreshAnnotationMap()
    {
        var station = _annotationEditor?.WorkingCopy ?? _currentStation;
        if (station is null)
        {
            return;
        }

        Map.SetStation(station, _currentImageSource);
        Map.SetAnnotationEditMode(_isMapAnnotationEditing, _annotationTool);
        Map.SetSelectedAnnotation(_selectedAnnotationKind, _selectedAnnotationId);
    }

    private void UpdateAdminMapControls()
    {
        MapAnnotationButton.Visibility = _adminModeService.IsAdmin && !_isMapAnnotationEditing ? Visibility.Visible : Visibility.Collapsed;
        AnnotationToolbar.Visibility = _adminModeService.IsAdmin && _isMapAnnotationEditing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateRightPanelMode()
    {
        if (_isMapAnnotationEditing)
        {
            RfidOverviewCard.Visibility = Visibility.Collapsed;
            RfidAlarmCard.Visibility = Visibility.Collapsed;
            RfidDetailView.Visibility = Visibility.Visible;
            RfidDetailView.Margin = new Thickness(0);
            RfidDetailStationBadge.Visibility = Visibility.Collapsed;
            Grid.SetRow(RfidDetailView, 0);
            Grid.SetRowSpan(RfidDetailView, 2);
            RfidDetailHeaderText.Text = "地图标注编辑";
            return;
        }

        RfidOverviewCard.Visibility = Visibility.Visible;
        RfidAlarmCard.Visibility = Visibility.Visible;
        RfidDetailView.Margin = new Thickness(0, 318, 0, 8);
        Grid.SetRow(RfidDetailView, 0);
        Grid.SetRowSpan(RfidDetailView, 1);
        RfidDetailHeaderText.Text = "RFID基站详情";
    }

    private void SetAnnotationEditorVisibility(bool visible)
    {
        AnnotationEditor.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SelectedDeviceInfo.Visibility = _isMapAnnotationEditing || visible ? Visibility.Collapsed : Visibility.Visible;
        AnnotationEditEmptyState.Visibility = _isMapAnnotationEditing && !visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateRightPanelMode();
        if (!_isMapAnnotationEditing)
        {
            UpdateRfidSelectionLayout(_selectedDevice?.Type == DeviceType.RfidStation || !string.IsNullOrWhiteSpace(_selectedRfidStationId));
        }
    }

    private void SetAnnotationTool(MapAnnotationTool tool)
    {
        _annotationTool = tool;
        Map.SetAnnotationEditMode(_isMapAnnotationEditing, tool);
    }

    private bool TryReadAnnotationFields(out string name, out double cadX, out double cadY, out double rotation, out double textHeight, out string? rfidStationId)
    {
        name = _pendingAnnotationKind == MapAnnotationKind.MapLabel ? AnnotationTextBox.Text.Trim() : AnnotationNameBox.Text.Trim();
        cadX = 0;
        cadY = 0;
        rfidStationId = _pendingAnnotationKind == MapAnnotationKind.RfidStation
            ? AnnotationRfidStationComboBox.SelectedValue as string
            : null;
        rotation = 0;
        textHeight = 0;
        if (string.IsNullOrWhiteSpace(name) ||
            !double.TryParse(AnnotationCadXBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out cadX) ||
            !double.TryParse(AnnotationCadYBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out cadY))
        {
            ShowMessageDialog("填写有误", "名称/文字、CAD X、CAD Y 必须填写有效值。", MessageDialogKind.Warning);
            return false;
        }

        if (_pendingAnnotationKind == MapAnnotationKind.RfidStation && FindRfidStation(rfidStationId) is null)
        {
            ShowMessageDialog("填写有误", "请选择要绑定的RFID基站。", MessageDialogKind.Warning);
            return false;
        }

        if (_pendingAnnotationKind == MapAnnotationKind.MapLabel &&
            (!double.TryParse(AnnotationRotationBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out rotation) ||
             (!string.Equals(AnnotationTextHeightBox.Text.Trim(), "默认", StringComparison.OrdinalIgnoreCase) &&
              !double.TryParse(AnnotationTextHeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out textHeight))))
        {
            ShowMessageDialog("填写有误", "旋转和字号必须填写有效数值。", MessageDialogKind.Warning);
            return false;
        }

        return true;
    }

    private void UpdateAnnotationFieldVisibility(MapAnnotationKind kind)
    {
        var isLabel = kind == MapAnnotationKind.MapLabel;
        var isRfid = kind == MapAnnotationKind.RfidStation;
        AnnotationNameLabel.Visibility = isLabel ? Visibility.Collapsed : Visibility.Visible;
        AnnotationNameBox.Visibility = isLabel ? Visibility.Collapsed : Visibility.Visible;
        AnnotationTextLabel.Visibility = isLabel ? Visibility.Visible : Visibility.Collapsed;
        AnnotationTextBox.Visibility = isLabel ? Visibility.Visible : Visibility.Collapsed;
        AnnotationRfidStationLabel.Visibility = isRfid ? Visibility.Visible : Visibility.Collapsed;
        AnnotationRfidStationComboBox.Visibility = isRfid ? Visibility.Visible : Visibility.Collapsed;
        AnnotationLabelPropertiesGrid.Visibility = isLabel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMessageDialog(string title, string message, MessageDialogKind kind)
    {
        var dialog = new StyledMessageDialog(title, message, kind);
        var owner = Window.GetWindow(this);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
    }

    private void ShowReadOnlyAnnotation(MapAnnotationHitEventArgs e)
    {
        var annotation = _currentStation is null ? null : FindAnnotation(_currentStation, e.Kind, e.AnnotationId);
        if (annotation is null)
        {
            return;
        }

        var displayName = annotation switch
        {
            MapPoint point => point.Name,
            DeviceConfig device => GetEffectiveRfidName(device),
            MapLabel label => label.Text,
            _ => e.AnnotationId
        };
        _selectedDevice = annotation as DeviceConfig;
        _selectedRfidStationId = _selectedDevice?.Type == DeviceType.RfidStation
            ? RfidMapBindingResolver.Resolve(_selectedDevice, _rfidStations).Configuration?.StationId ?? _selectedDevice.RfidStationId
            : null;
        SelectedDeviceTitle.Text = GetAnnotationTypeText(e.Kind);
        SelectedDeviceId.Text = displayName;
        SelectedDeviceStation.Text = $"CAD X={e.CadPoint.X:0.####}  Y={e.CadPoint.Y:0.####}";
        SelectedDeviceCadX.Text = e.CadPoint.X.ToString("0.####");
        SelectedDeviceCadY.Text = e.CadPoint.Y.ToString("0.####");
        SelectedDeviceStatus.Text = "只读";
        ApplySelectedRfidRuntimeState();
        Map.SetSelectedAnnotation(e.Kind, e.AnnotationId);
        AddSystemEvent("选择", $"查看 {displayName}");
    }

    private void OnStatusSimulationClick(object sender, RoutedEventArgs e)
    {
        if (_selectedDevice is null || sender is not Button button || button.Tag is not string statusName ||
            !Enum.TryParse<DeviceStatus>(statusName, out var status))
        {
            return;
        }

        if (Map.SetDeviceStatus(_selectedDevice.Id, status))
        {
            SelectedDeviceStatus.Text = GetStatusText(status);
            AddSystemEvent("模拟", $"{_selectedDevice.Id} 状态切换为 {GetStatusText(status)}");
        }
    }

    private void ClearDetails()
    {
        _selectedDevice = null;
        _selectedRfidStationId = null;
        SelectedDeviceTitle.Text = "未选择点位";
        SelectedDeviceId.Text = "-";
        SelectedDeviceStation.Text = "-";
        SelectedDeviceCadX.Text = "-";
        SelectedDeviceCadY.Text = "-";
        SelectedDeviceStatus.Text = "-";
        ResetSelectedRfidRuntimeFields();
        UpdateRfidSelectionLayout(false);
        Map.SetSelectedAnnotation(null, null);
    }

    private void ApplySelectedRfidRuntimeState()
    {
        ResetSelectedRfidRuntimeFields();
        var isMapRfidSelection = _selectedDevice?.Type == DeviceType.RfidStation;
        var binding = isMapRfidSelection
            ? RfidMapBindingResolver.Resolve(_selectedDevice!, _rfidStations)
            : null;
        var configuration = binding?.Configuration ?? FindRfidStation(_selectedRfidStationId);
        if (!isMapRfidSelection && configuration is null)
        {
            _selectedRfidStationId = null;
            UpdateRfidSelectionLayout(false);
            return;
        }

        UpdateRfidSelectionLayout(true);
        if (configuration is not null)
        {
            _selectedRfidStationId = configuration.StationId;
            var displayStationId = RfidStationIdentity.GetDisplayId(configuration.StationId, configuration.YardId);
            RfidDetailStationBadgeText.Text = displayStationId;
            RfidDetailStationBadge.Visibility = Visibility.Visible;
            SelectedRfidStationNameText.Text = string.IsNullOrWhiteSpace(configuration.Name)
                ? displayStationId
                : configuration.Name;
            SelectedRfidStationIdText.Text = displayStationId;
            SelectedRfidEndpointText.Text = configuration.TryResolveEndpoint(out var endpoint)
                ? $"{endpoint.Address}:{endpoint.Port}"
                : "-";
            SelectedRfidProtocolAddressText.Text = configuration.ProtocolAddress.ToString("X2");
        }
        else if (binding is not null)
        {
            SelectedRfidStationNameText.Text = binding.EffectiveName;
            SelectedRfidStationIdText.Text = GetDisplayRfidStationId(_selectedDevice?.RfidStationId);
        }

        if (binding is not null && (binding.State != RfidMapBindingState.Offline || binding.Configuration is null))
        {
            var bindingStateText = GetMapBindingStateText(binding.State);
            SelectedDeviceStatus.Text = bindingStateText;
            SelectedRfidCommunicationText.Text = bindingStateText;
            SelectedRfidBusinessStateText.Text = "-";
            return;
        }

        if (configuration is null)
        {
            SelectedRfidCommunicationText.Text = "配置不存在";
            SelectedRfidBusinessStateText.Text = "-";
            return;
        }

        var state = _rfidRuntimeStates.FirstOrDefault(item =>
            string.Equals(item.StationId, configuration.StationId, StringComparison.OrdinalIgnoreCase));
        if (state is null)
        {
            SelectedDeviceStatus.Text = "离线";
            SelectedRfidCommunicationText.Text = "离线";
            SelectedRfidBusinessStateText.Text = "-";
            return;
        }

        SelectedDeviceStatus.Text = GetRuntimeStateText(state);
        SelectedRfidCommunicationText.Text = state.CommunicationState == StationCommunicationState.Online ? "在线" : "离线";
        SelectedRfidBusinessStateText.Text = GetRuntimeStateText(state);
        SelectedDeviceHeadRfid.Text = state.CurrentHeadRfid?.ToString("X4") ?? "-";
        SelectedDeviceProgress.Text = $"{state.DetectedVehicleCount} / {state.ExpectedVehicleCount}";
        var latestRfid = state.ObservedVehicleSequence.LastOrDefault(value => value != state.EmptyRfidValue);
        SelectedDeviceLatestRfid.Text = latestRfid == state.EmptyRfidValue ? "-" : latestRfid.ToString("X4");
        SelectedDeviceLastCommunication.Text = state.LastResponseAt?.ToLocalTime().ToString("HH:mm:ss") ?? "-";
        SelectedRfidProgressText.Text = $"{state.DetectedVehicleCount} / {state.ExpectedVehicleCount}";
        SelectedRfidLastCommunicationText.Text = state.LastResponseAt?.ToLocalTime().ToString("HH:mm:ss") ?? "-";
        SelectedRfidValidCountText.Text = state.ObservedVehicleSequence
            .Count(value => value != state.EmptyRfidValue)
            .ToString(CultureInfo.InvariantCulture);
    }

    private void ResetSelectedRfidRuntimeFields()
    {
        SelectedDeviceHeadRfid.Text = "-";
        SelectedDeviceProgress.Text = "-";
        SelectedDeviceLatestRfid.Text = "-";
        SelectedDeviceLastCommunication.Text = "-";
        SelectedRfidStationNameText.Text = "-";
        SelectedRfidStationIdText.Text = "-";
        RfidDetailStationBadgeText.Text = "-";
        RfidDetailStationBadge.Visibility = Visibility.Collapsed;
        SelectedRfidCommunicationText.Text = "-";
        SelectedRfidBusinessStateText.Text = "-";
        SelectedRfidEndpointText.Text = "-";
        SelectedRfidProtocolAddressText.Text = "-";
        SelectedRfidLastCommunicationText.Text = "-";
        SelectedRfidProgressText.Text = "-";
        SelectedRfidValidCountText.Text = "-";
    }

    private void UpdateRfidSelectionLayout(bool hasRfidSelection)
    {
        if (_isMapAnnotationEditing)
        {
            UpdateRightPanelMode();
            return;
        }

        UpdateRightPanelMode();
        RfidOverviewContent.Visibility = Visibility.Visible;
        OverviewEmptyState.Visibility = hasRfidSelection ? Visibility.Collapsed : Visibility.Visible;
        RfidDetailView.Visibility = hasRfidSelection ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyDashboardSnapshot(DashboardSnapshot snapshot)
    {
        ResetSelectedRfidRuntimeFields();
        VehicleGrid.ItemsSource = snapshot.Trains;
        _runtimeEvents.Clear();
        foreach (var runtimeEvent in snapshot.Events)
        {
            _runtimeEvents.Add(runtimeEvent);
        }

        if (snapshot.SelectedTrain is null)
        {
            ClearDetails();
            return;
        }

        SelectedDeviceTitle.Text = "Y6-12";
        SelectedDeviceId.Text = "Y6-12";
        SelectedDeviceStation.Text = snapshot.SelectedTrain.CurrentDevice;
        SelectedDeviceCadX.Text = "-";
        SelectedDeviceCadY.Text = "-";
        SelectedDeviceStatus.Text = "异常";
    }

    private static object? FindAnnotation(StationConfig station, MapAnnotationKind kind, string id) => kind switch
    {
        MapAnnotationKind.MapPoint => station.Points.FirstOrDefault(point => point.Id == id),
        MapAnnotationKind.RfidStation => station.Devices.FirstOrDefault(device => device.Type == DeviceType.RfidStation && device.Id == id),
        MapAnnotationKind.MapLabel => station.Labels.FirstOrDefault(label => label.Id == id),
        _ => null
    };

    private static void CopyStationData(StationConfig target, StationConfig source)
    {
        target.Id = source.Id;
        target.Name = source.Name;
        target.BackgroundImage = source.BackgroundImage;
        target.CadMinX = source.CadMinX;
        target.CadMaxX = source.CadMaxX;
        target.CadMinY = source.CadMinY;
        target.CadMaxY = source.CadMaxY;
        target.Points = source.Points;
        target.Labels = source.Labels;
        target.Devices = source.Devices;
    }

    private static string GetAnnotationTypeText(MapAnnotationKind kind) => kind switch
    {
        MapAnnotationKind.MapPoint => "普通点位",
        MapAnnotationKind.RfidStation => "RFID基站",
        MapAnnotationKind.MapLabel => "文字标注",
        _ => "地图标注"
    };

    private static bool IsAddTool(MapAnnotationTool tool) => tool is MapAnnotationTool.MapPoint or MapAnnotationTool.RfidStation or MapAnnotationTool.MapLabel;

    private string GetDeviceLocation(string deviceId) => deviceId switch
    {
        "Y6-10" => $"{_currentStationDisplayName}  上部股道",
        "Y6-11" => $"{_currentStationDisplayName}  中部股道",
        "Y6-12" => $"{_currentStationDisplayName}  下部股道",
        _ => _currentStationDisplayName
    };

    private static string FormatStationName(StationConfig station) => station.Name.Replace(" 站场", "水平");

    private static string GetStatusText(DeviceStatus status) => status switch
    {
        DeviceStatus.Normal => "正常",
        DeviceStatus.Identifying => "识别中",
        DeviceStatus.Alarm => "报警",
        DeviceStatus.Offline => "离线",
        _ => "未知"
    };

    private static string GetRuntimeStateText(StationRuntimeState state)
    {
        if (IsUncouplingAlarm(state))
        {
            return "脱节报警";
        }

        if (state.CommunicationState == StationCommunicationState.Offline)
        {
            return "离线";
        }

        return state.LifecycleState switch
        {
            PassageLifecycleState.Recognizing => state.WarningMessages.Count > 0 ? "识别不完整" : "识别中",
            PassageLifecycleState.Alarm => "脱节报警",
            PassageLifecycleState.Finalizing => "记录保存中",
            PassageLifecycleState.Clearing or PassageLifecycleState.WaitForEmpty => "清除/等待空槽",
            _ => state.WarningMessages.Count > 0 ? "识别不完整" : "空闲"
        };
    }

    private RfidStationConfig? FindRfidStation(string? stationId) =>
        string.IsNullOrWhiteSpace(stationId)
            ? null
            : _rfidStations.FirstOrDefault(station =>
                string.Equals(station.StationId, stationId!.Trim(), StringComparison.OrdinalIgnoreCase));

    private string GetDisplayRfidStationId(string? stationId)
    {
        var normalized = stationId?.Trim() ?? string.Empty;
        var station = FindRfidStation(normalized);
        return station is null
            ? normalized
            : RfidStationIdentity.GetDisplayId(station.StationId, station.YardId);
    }

    private string GetEffectiveRfidName(DeviceConfig device) =>
        RfidMapBindingResolver.Resolve(device, _rfidStations).EffectiveName;

    private static string GetMapBindingStateText(RfidMapBindingState state) => state switch
    {
        RfidMapBindingState.Unbound => "未绑定基站",
        RfidMapBindingState.MissingConfiguration => "基站配置不存在",
        RfidMapBindingState.Disabled => "基站已禁用",
        _ => "离线"
    };

    public sealed class RfidStationChoice
    {
        public RfidStationChoice(string stationId, string name, string? yardId, bool enabled)
        {
            StationId = stationId;
            var displayStationId = RfidStationIdentity.GetDisplayId(stationId, yardId);
            DisplayName = enabled ? $"{name}（{displayStationId}）" : $"{name}（{displayStationId}，已禁用）";
        }

        public string StationId { get; }

        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }
}
