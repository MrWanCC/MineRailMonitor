using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Pages;

public partial class AlarmHistoryPage : UserControl
{
    private const int PageSize = 20;
    private readonly IPassageRecordStore _recordStore;
    private readonly IReadOnlyDictionary<string, string> _stationNames;
    private readonly ObservableCollection<AlarmRow> _rows = new();
    private int _pageIndex;
    private int _totalCount;

    public AlarmHistoryPage(IPassageRecordStore recordStore, IEnumerable<RfidStationConfig>? stations = null)
    {
        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _stationNames = BuildStationNames(stations);
        InitializeComponent();
        AlarmGrid.ItemsSource = _rows;
        PopulateStationFilter(stations);
        StationFilter.SelectedIndex = 0;
        SetDefaultDateTimeFilter();
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        _pageIndex = 0;
        ExecuteQuery();
    }

    private void OnQueryClick(object sender, RoutedEventArgs e)
    {
        _pageIndex = 0;
        ExecuteQuery();
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
                _rows.Add(new AlarmRow(record, FormatStation(record.StationId, _stationNames)));
            }

            var statistics = _recordStore.GetStatistics(DateTimeOffset.Now);
            TodayAlarmValue.Text = statistics.TodayAlarmCount.ToString(CultureInfo.InvariantCulture);
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
            TodayAlarmValue.Text = "-";
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
            HeadRfid = headRfid,
            Outcome = PassageOutcome.UncouplingAlarm,
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
        var dialog = new PassageDetailsDialog(record, FormatStation(record.StationId, _stationNames), alarmMode: true)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

    private void PopulateStationFilter(IEnumerable<RfidStationConfig>? stations)
    {
        StationFilter.Items.Add(new ComboBoxItem { Content = "全部", Tag = null });
        StationFilter.Items.Add(new ComboBoxItem
        {
            Content = "历史未识别基站",
            Tag = PassageRecord.LegacyStationId
        });
        foreach (var station in (stations ?? Array.Empty<RfidStationConfig>())
                     .Where(item => !string.IsNullOrWhiteSpace(item.StationId))
                     .GroupBy(item => item.StationId.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(item => item.StationId, StringComparer.OrdinalIgnoreCase))
        {
            StationFilter.Items.Add(new ComboBoxItem
            {
                Content = FormatStation(station.StationId, _stationNames),
                Tag = station.StationId.Trim()
            });
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

    private static string FormatStation(string? stationId, IReadOnlyDictionary<string, string> stationNames)
    {
        var normalized = stationId?.Trim() ?? string.Empty;
        if (normalized.Length == 0 ||
            string.Equals(normalized, PassageRecord.LegacyStationId, StringComparison.OrdinalIgnoreCase))
        {
            return "历史未识别基站";
        }

        return stationNames.TryGetValue(normalized, out var name) && name.Length > 0
            ? $"{normalized} · {name}"
            : normalized;
    }

    private void SetDefaultDateTimeFilter()
    {
        FromDatePicker.SelectedDate = DateTime.Today;
        ToDatePicker.SelectedDate = DateTime.Today;
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

    private static string FormatRfid(ushort? value) => value.HasValue ? value.Value.ToString("X4") : "-";

    private static string FormatClearState(PassageClearState value) => value == PassageClearState.Cleared ? "已清除" : "待清除";

    private sealed class AlarmRow
    {
        public AlarmRow(PassageRecord record, string stationText)
        {
            Record = record;
            AlarmTimeText = FormatTime(record.CompletedAt);
            StationText = stationText;
            HeadRfidText = FormatRfid(record.HeadRfid);
            ProgressText = $"{record.DetectedVehicleCount} / {record.ExpectedVehicleCount}";
            MissingCountText = Math.Max(record.ExpectedVehicleCount - record.DetectedVehicleCount, 0).ToString(CultureInfo.InvariantCulture);
            AlarmReasonText = record.AlarmMessage ?? "脱节报警";
            ClearStateText = FormatClearState(record.ClearState);
        }

        public PassageRecord Record { get; }
        public string AlarmTimeText { get; }
        public string StationText { get; }
        public string HeadRfidText { get; }
        public string ProgressText { get; }
        public string MissingCountText { get; }
        public string AlarmReasonText { get; }
        public string ClearStateText { get; }
    }

}
