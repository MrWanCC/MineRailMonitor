using System.Linq;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Pages;

public partial class RunLogPage : UserControl
{
    private const int PageSize = 100;
    private readonly IPassageEventQueryService _passageEventQueryService;
    private readonly IOperationRecordStore _operationRecordStore;
    private readonly IPassageRecordStore _passageRecordStore;
    private readonly Func<IEnumerable<RfidStationPollingStatus>> _pollingStatusesProvider;
    private readonly AdminModeService? _adminModeService;
    private IReadOnlyList<RfidStationConfig> _rfidStations = Array.Empty<RfidStationConfig>();
    private HashSet<string>? _displayScopeStationIds;
    private string? _displayScopeYardId;
    private bool _isRefreshing;

    public RunLogPage(
        IPassageEventQueryService passageEventQueryService,
        IOperationRecordStore operationRecordStore,
        IPassageRecordStore passageRecordStore,
        Func<IEnumerable<RfidStationPollingStatus>> pollingStatusesProvider,
        AdminModeService? adminModeService = null)
    {
        _passageEventQueryService = passageEventQueryService ?? throw new ArgumentNullException(nameof(passageEventQueryService));
        _operationRecordStore = operationRecordStore ?? throw new ArgumentNullException(nameof(operationRecordStore));
        _passageRecordStore = passageRecordStore ?? throw new ArgumentNullException(nameof(passageRecordStore));
        _pollingStatusesProvider = pollingStatusesProvider ?? throw new ArgumentNullException(nameof(pollingStatusesProvider));
        _adminModeService = adminModeService;
        InitializeComponent();
        if (_adminModeService is not null)
        {
            _adminModeService.PropertyChanged += OnAdminModePropertyChanged;
        }

        SetAdminMode(_adminModeService?.IsAdmin == true);
    }

    public void SetAdminMode(bool isAdmin)
    {
        OperationRecordsTab.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        if (!isAdmin && OperationRecordsTab.IsSelected)
        {
            RunLogTabs.SelectedIndex = 0;
        }

        if (!isAdmin)
        {
            OperationGrid.ItemsSource = Array.Empty<OperationRecordDisplayItem>();
            OperationDetailsPanel.DataContext = null;
            OperationEmptyText.Visibility = Visibility.Visible;
        }
    }

    public void SetRfidStations(IEnumerable<RfidStationConfig>? stations)
    {
        _rfidStations = stations?.Where(station => station is not null).ToArray()
            ?? Array.Empty<RfidStationConfig>();
        if (IsVisible)
        {
            RefreshStationStatuses();
        }
    }

    public void SetDisplayScope(IEnumerable<string>? stationIds, string? yardId)
    {
        _displayScopeStationIds = stationIds is null
            ? null
            : new HashSet<string>(stationIds
                .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
                .Select(stationId => stationId.Trim()), StringComparer.OrdinalIgnoreCase);
        _displayScopeYardId = string.IsNullOrWhiteSpace(yardId) ? null : yardId?.Trim();
        if (IsVisible)
        {
            RefreshStationStatuses();
        }
    }

    public void Refresh()
    {
        var events = _passageEventQueryService.QueryPage(0, PageSize)
            .Select(PassageEventDisplayFormatter.Format)
            .ToArray();

        _isRefreshing = true;
        try
        {
            EventGrid.ItemsSource = events;
            EventEmptyText.Visibility = events.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            EventGrid.SelectedIndex = events.Length == 0 ? -1 : 0;
        }
        finally
        {
            _isRefreshing = false;
        }
        UpdateSelectedPassage();

        var operationRecords = _adminModeService?.IsAdmin == true
            ? _operationRecordStore.QueryPage(0, PageSize)
                .Select(OperationRecordDisplayFormatter.Format)
                .ToArray()
            : Array.Empty<OperationRecordDisplayItem>();
        _isRefreshing = true;
        try
        {
            OperationGrid.ItemsSource = operationRecords;
            OperationEmptyText.Visibility = operationRecords.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            OperationGrid.SelectedIndex = operationRecords.Length == 0 ? -1 : 0;
        }
        finally
        {
            _isRefreshing = false;
        }
        UpdateSelectedOperation();

        RefreshStationStatuses();
    }

    private void OnAdminModePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdminModeService.IsAdmin))
        {
            SetAdminMode(_adminModeService?.IsAdmin == true);
        }
    }

    private void OnRunLogTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, RunLogTabs) &&
            ReferenceEquals(RunLogTabs.SelectedItem, OperationRecordsTab) &&
            _adminModeService?.IsAdmin != true)
        {
            RunLogTabs.SelectedIndex = 0;
            return;
        }

        if (ReferenceEquals(e.OriginalSource, RunLogTabs) &&
            ReferenceEquals(RunLogTabs.SelectedItem, OperationRecordsTab))
        {
            Refresh();
        }
    }

    public void RefreshStationStatuses()
    {
        var statusesByStationId = _pollingStatusesProvider()
            .Where(status => status is not null && !string.IsNullOrWhiteSpace(status.StationId))
            .GroupBy(status => status.StationId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(status => status.LastReceivedAt).First(),
                StringComparer.OrdinalIgnoreCase);

        var rows = _rfidStations
            .Where(station => _displayScopeStationIds is null ||
                _displayScopeStationIds.Contains(station.StationId.Trim()))
            .Select(station => statusesByStationId.TryGetValue(station.StationId.Trim(), out var status)
                ? new RunLogStationRow(station, status)
                : new RunLogStationRow(station, null))
            .ToArray();

        CommunicationGrid.ItemsSource = rows;
        StationStatusEmptyText.Text = _displayScopeStationIds is not null && !string.IsNullOrWhiteSpace(_displayScopeYardId)
            ? "当前站场未配置 RFID 基站"
            : "暂无 RFID 基站配置";
        StationStatusEmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EventGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isRefreshing)
        {
            UpdateSelectedPassage();
        }
    }

    private void OperationGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isRefreshing)
        {
            UpdateSelectedOperation();
        }
    }

    private void UpdateSelectedOperation()
    {
        OperationDetailsPanel.DataContext = OperationGrid.SelectedItem as OperationRecordDisplayItem;
        OperationEmptyText.Visibility = OperationGrid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSelectedPassage()
    {
        if (EventGrid.SelectedItem is not PassageEventDisplayItem selectedEvent)
        {
            RunDetailsPanel.DataContext = null;
            TimelineEmptyText.Visibility = Visibility.Visible;
            return;
        }

        var passageEvents = _passageEventQueryService.QueryByPassageId(selectedEvent.PassageId);
        var passageRecord = _passageRecordStore.GetDetails(selectedEvent.PassageId);
        var details = PassageEventDisplayFormatter.FormatPassage(passageEvents, passageRecord);
        RunDetailsPanel.DataContext = details;
        TimelineEmptyText.Visibility = details is null || details.Timeline.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}

public sealed class RunLogStationRow
{
    public RunLogStationRow(RfidStationConfig station, RfidStationPollingStatus? status)
    {
        if (station is null) throw new ArgumentNullException(nameof(station));

        StationName = string.IsNullOrWhiteSpace(station.Name)
            ? RfidStationIdentity.GetDisplayId(station.StationId, station.YardId)
            : $"{RfidStationIdentity.GetDisplayId(station.StationId, station.YardId)} · {station.Name}";
        StationId = station.StationId;
        if (!station.TryResolveEndpoint(out var endpoint))
        {
            IpAddress = "端点无效";
            Port = "—";
        }
        else if (RfidStationStatusDisplayFormatter.HasEndpointMismatch(station, status))
        {
            IpAddress = "运行端点不一致";
            Port = "—";
        }
        else
        {
            IpAddress = endpoint.Address.ToString();
            Port = endpoint.Port.ToString(CultureInfo.InvariantCulture);
        }

        Status = RfidStationStatusDisplayFormatter.FormatStatus(station, status);
        LastReceivedTimeText = RfidStationStatusDisplayFormatter.FormatLastReceivedTime(status?.LastReceivedAt);
    }

    public string StationName { get; }
    public string StationId { get; }
    public string IpAddress { get; }
    public string Port { get; }
    public string Status { get; }
    public string LastReceivedTimeText { get; }
}
