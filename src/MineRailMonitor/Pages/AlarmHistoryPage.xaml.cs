using System.Collections.ObjectModel;
using System.Globalization;
using MineRailMonitor.Core.Communication;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Pages;

public partial class AlarmHistoryPage : UserControl
{
    private const int PageSize = 20;
    private readonly IPassageRecordStore _recordStore;
    private readonly Action<Guid, DateTimeOffset>? _acknowledgeAlarmRequested;
    private IReadOnlyList<RfidStationConfig> _stations;
    private IReadOnlyDictionary<string, string> _stationNames;
    private IReadOnlyList<StationConfig> _yardConfigs = Array.Empty<StationConfig>();
    private IReadOnlyList<RfidStationYardOption> _yardFilterOptions;
    private HashSet<string>? _displayScopeStationIds;
    private readonly ObservableCollection<AlarmRow> _rows = new();
    private readonly AlarmHistoryDateRangePolicy _dateRangePolicy = new();
    private bool _isYardFilterSync;
    private bool _isUpdatingDateRange;
    private int _pageIndex;
    private int _totalCount;

    public AlarmHistoryPage(
        IPassageRecordStore recordStore,
        IEnumerable<RfidStationConfig>? stations = null,
        Action<Guid, DateTimeOffset>? acknowledgeAlarmRequested = null)
    {
        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _acknowledgeAlarmRequested = acknowledgeAlarmRequested;
        _stations = (stations ?? Array.Empty<RfidStationConfig>()).Where(station => station is not null).ToArray();
        _stationNames = BuildStationNames(_stations);
        _yardFilterOptions = RfidYardFilter.BuildOptions(null, _stations);
        InitializeComponent();
        AlarmGrid.ItemsSource = _rows;
        YardFilter.ItemsSource = _yardFilterOptions;
        YardFilter.SelectedIndex = 0;
        RebuildStationFilter();
        StationFilter.SelectedIndex = 0;
        SetDefaultDateTimeFilter();
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        _pageIndex = 0;
        RefreshDefaultDateRange();
        ExecuteQuery();
    }

    public void SetRfidStations(IEnumerable<RfidStationConfig>? stations)
    {
        _stations = (stations ?? Array.Empty<RfidStationConfig>()).Where(station => station is not null).ToArray();
        _stationNames = BuildStationNames(_stations);
        _yardFilterOptions = RfidYardFilter.BuildOptions(_yardConfigs, _stations);
        SetYardFilterSelection(GetSelectedYardId());
        RebuildStationFilter();
    }

    public void SetYardOptions(IEnumerable<StationConfig>? yards)
    {
        _yardConfigs = (yards ?? Array.Empty<StationConfig>())
            .Where(yard => yard is not null)
            .ToArray();
        _yardFilterOptions = RfidYardFilter.BuildOptions(_yardConfigs, _stations);
        SetYardFilterSelection(GetSelectedYardId());
        RebuildStationFilter();
        Refresh();
    }

    public void SetDisplayScope(IEnumerable<string>? stationIds, string? yardId = null)
    {
        _displayScopeStationIds = stationIds is null
            ? null
            : new HashSet<string>(
                stationIds.Where(stationId => !string.IsNullOrWhiteSpace(stationId)).Select(stationId => stationId.Trim()),
                StringComparer.OrdinalIgnoreCase);
        SetYardFilterSelection(yardId ?? RfidYardFilter.ResolveYardId(stationIds, _stations) ?? RfidStationYardOption.AllId);
        RebuildStationFilter();
        Refresh();
    }

    private void OnQueryClick(object sender, RoutedEventArgs e)
    {
        _pageIndex = 0;
        ExecuteQuery();
    }

    private void OnYardFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isYardFilterSync)
        {
            return;
        }

        _displayScopeStationIds = RfidYardFilter.ResolveStationIds(GetSelectedYardId(), _stations);
        RebuildStationFilter();
        Refresh();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        SetDefaultDateTimeFilter();
        StationFilter.SelectedIndex = 0;
        HeadRfidFilter.Clear();
        _pageIndex = 0;
        ExecuteQuery();
    }

    private void OnPreviousPageClick(object sender, RoutedEventArgs e)
    {
        if (_pageIndex <= 0) return;
        _pageIndex--;
        ExecuteQuery();
    }

    private void OnNextPageClick(object sender, RoutedEventArgs e)
    {
        if ((_pageIndex + 1) * PageSize >= _totalCount) return;
        _pageIndex++;
        ExecuteQuery();
    }

    private void ExecuteQuery()
    {
        try
        {
            var result = _recordStore.Query(BuildAlarmQuery());
            _totalCount = result.TotalCount;
            _rows.Clear();
            foreach (var record in result.Items)
            {
                _rows.Add(new AlarmRow(record, FormatStation(record.StationId)));
            }

            QueryAlarmValue.Text = _totalCount.ToString(CultureInfo.InvariantCulture);
            StationCountValue.Text = _rows.Select(item => item.Record.StationId)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
                .ToString(CultureInfo.InvariantCulture);
            LatestAlarmStationValue.Text = _rows.Count == 0 ? "-" : _rows[0].StationText;

            ListSummaryText.Text = _totalCount == 0
                ? "共 0 条"
                : $"第 {_pageIndex + 1} / {Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize))} 页，共 {_totalCount} 条";
            QueryErrorText.Text = string.Empty;
            PreviousPageButton.IsEnabled = _pageIndex > 0;
            NextPageButton.IsEnabled = (_pageIndex + 1) * PageSize < _totalCount;
            AlarmGrid.SelectedItem = null;
            NoDataText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            QueryErrorText.Text = exception.Message;
            _rows.Clear();
            ListSummaryText.Text = "查询失败";
            QueryAlarmValue.Text = "-";
            StationCountValue.Text = "-";
            LatestAlarmStationValue.Text = "-";
            NoDataText.Visibility = Visibility.Visible;
        }
    }

    private PassageQuery BuildAlarmQuery()
    {
        ushort? headRfid = null;
        var headText = HeadRfidFilter.Text.Trim();
        if (headText.Length > 0)
        {
            if (headText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) headText = headText.Substring(2);
            if (!ushort.TryParse(headText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new FormatException("车头RFID必须是4位十六进制，例如 0003。");
            }
            headRfid = parsed;
        }

        var stationTag = (StationFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        return new PassageQuery
        {
            From = ParseFilterDateTime(FromDatePicker, "开始时间"),
            To = ParseFilterDateTime(ToDatePicker, "结束时间", endOfDay: true)?.AddSeconds(1),
            StationId = stationTag,
            StationIds = _displayScopeStationIds?.ToArray(),
            HeadRfid = headRfid,
            IncludeWarnings = true,
            PageIndex = _pageIndex,
            PageSize = PageSize
        };
    }

    private void OnAlarmDetailsClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AlarmRow row)
        {
            return;
        }

        var record = _recordStore.GetDetails(row.Record.PassageId) ?? row.Record;
        var dialog = new PassageDetailsDialog(record, FormatStation(record.StationId), alarmMode: true, _acknowledgeAlarmRequested)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            Refresh();
        }
    }

    private void OnAlarmAcknowledgeClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AlarmRow row || !row.CanAcknowledge)
        {
            return;
        }

        try
        {
            if (_acknowledgeAlarmRequested is null)
            {
                throw new InvalidOperationException("当前页面未配置报警确认处理器。");
            }

            _acknowledgeAlarmRequested(row.Record.PassageId, DateTimeOffset.Now);
            Refresh();
        }
        catch (Exception exception)
        {
            QueryErrorText.Text = exception.Message;
        }
    }

    private void RebuildStationFilter()
    {
        var selectedStationId = (StationFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        StationFilter.Items.Clear();
        StationFilter.Items.Add(new ComboBoxItem { Content = "全部", Tag = null });
        if (_displayScopeStationIds is null || _displayScopeStationIds.Contains(PassageRecord.LegacyStationId))
        {
            StationFilter.Items.Add(new ComboBoxItem
            {
                Content = "历史未识别基站",
                Tag = PassageRecord.LegacyStationId
            });
        }

        foreach (var station in _stations
                      .Where(item => !string.IsNullOrWhiteSpace(item.StationId))
                      .Where(item => _displayScopeStationIds is null || _displayScopeStationIds.Contains(item.StationId.Trim()))
                      .GroupBy(item => item.StationId.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(item => item.StationId, StringComparer.OrdinalIgnoreCase))
        {
            StationFilter.Items.Add(new ComboBoxItem
            {
                Content = FormatStation(station.StationId),
                Tag = station.StationId.Trim()
            });
        }

        StationFilter.SelectedItem = StationFilter.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag as string,
                selectedStationId,
                StringComparison.OrdinalIgnoreCase))
            ?? StationFilter.Items[0];
    }

    private string GetSelectedYardId() =>
        (YardFilter.SelectedItem as RfidStationYardOption)?.Id ?? RfidStationYardOption.AllId;

    private void SetYardFilterSelection(string selectedYardId)
    {
        var selectedItem = _yardFilterOptions.FirstOrDefault(option =>
            string.Equals(option.Id, selectedYardId, StringComparison.OrdinalIgnoreCase))
            ?? _yardFilterOptions.FirstOrDefault();
        if (selectedItem is null)
        {
            return;
        }

        _isYardFilterSync = true;
        try
        {
            YardFilter.ItemsSource = _yardFilterOptions;
            YardFilter.SelectedItem = selectedItem;
        }
        finally
        {
            _isYardFilterSync = false;
        }
    }

    private static IReadOnlyDictionary<string, string> BuildStationNames(IEnumerable<RfidStationConfig>? stations) =>
        (stations ?? Array.Empty<RfidStationConfig>())
            .Where(item => !string.IsNullOrWhiteSpace(item.StationId))
            .GroupBy(item => item.StationId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Name?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

    private string FormatStation(string? stationId)
    {
        var normalized = stationId?.Trim() ?? string.Empty;
        if (normalized.Length == 0 ||
            string.Equals(normalized, PassageRecord.LegacyStationId, StringComparison.OrdinalIgnoreCase))
        {
            return "历史未识别基站";
        }

        var station = _stations.FirstOrDefault(item =>
            string.Equals(item.StationId.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        var displayId = station is null
            ? normalized
            : RfidStationIdentity.GetDisplayId(station.StationId, station.YardId);
        return _stationNames.TryGetValue(normalized, out var name) && name.Length > 0
            ? $"{displayId} · {name}"
            : displayId;
    }

    private void SetDefaultDateTimeFilter()
    {
        _dateRangePolicy.Reset();
        SetDateTimeFilter(DateTime.Today);
    }

    private void RefreshDefaultDateRange()
    {
        var today = DateTime.Today;
        if (_dateRangePolicy.ShouldRefreshFor(FromDatePicker.SelectedDate, ToDatePicker.SelectedDate, today))
        {
            SetDateTimeFilter(today);
        }
    }

    private void SetDateTimeFilter(DateTime date)
    {
        _isUpdatingDateRange = true;
        try
        {
            FromDatePicker.SelectedDate = date.Date;
            ToDatePicker.SelectedDate = date.Date;
        }
        finally
        {
            _isUpdatingDateRange = false;
        }
    }

    private void OnFilterDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingDateRange)
        {
            return;
        }

        _dateRangePolicy.RecordUserSelection(FromDatePicker.SelectedDate, ToDatePicker.SelectedDate, DateTime.Today);
    }

    private void OnDatePickerPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DatePicker picker || IsInsideButton(e.OriginalSource))
        {
            return;
        }

        e.Handled = true;
        picker.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => picker.IsDropDownOpen = true));
    }

    private static bool IsInsideButton(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button)
            {
                return true;
            }

            current = current is Visual visual ? VisualTreeHelper.GetParent(visual) : null;
        }

        return false;
    }

    private static DateTimeOffset? ParseFilterDateTime(DatePicker picker, string fieldName, bool endOfDay = false)
    {
        if (!picker.SelectedDate.HasValue)
        {
            return null;
        }

        var value = StartOfDay(picker.SelectedDate.Value);
        if (endOfDay)
        {
            value = value.AddDays(1).AddSeconds(-1);
        }
        return new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value));
    }

    private static DateTime StartOfDay(DateTime value) => value.Date;

    private static string FormatTime(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private sealed class AlarmRow
    {
        public AlarmRow(PassageRecord record, string stationText)
        {
            Record = record;
            AlarmTimeText = FormatTime(record.CompletedAt);
            VehicleNoText = string.IsNullOrWhiteSpace(record.VehicleNo) ? "未配置车辆" : record.VehicleNo!;
            AlarmTypeText = record.Outcome == PassageOutcome.UncouplingAlarm ? "脱节报警" : "识别告警";
            ExpectedCountText = $"{record.ExpectedVehicleCount?.ToString(CultureInfo.InvariantCulture) ?? "未配置"}节";
            ActualCountText = $"{record.DetectedVehicleCount}节";
            MissingCountText = Math.Max(record.ExpectedVehicleCount.GetValueOrDefault() - record.DetectedVehicleCount, 0).ToString(CultureInfo.InvariantCulture);
            StationText = stationText;
            AlarmStatusText = AlarmStatusDisplayFormatter.Format(record.AlarmStatus);
            CanAcknowledge = record.AlarmStatus == AlarmStatus.Unconfirmed;
            AcknowledgeButtonVisibility = CanAcknowledge ? Visibility.Visible : Visibility.Collapsed;
        }

        public PassageRecord Record { get; }
        public string AlarmTimeText { get; }
        public string VehicleNoText { get; }
        public string AlarmTypeText { get; }
        public string ExpectedCountText { get; }
        public string ActualCountText { get; }
        public string StationText { get; }
        public string MissingCountText { get; }
        public string AlarmStatusText { get; }
        public bool CanAcknowledge { get; }
        public Visibility AcknowledgeButtonVisibility { get; }
    }

}
