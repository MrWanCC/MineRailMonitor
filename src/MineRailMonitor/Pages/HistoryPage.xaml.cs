using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Pages;

public partial class HistoryPage : UserControl
{
    private const int PageSize = 20;
    private readonly IPassageRecordStore _recordStore;
    private readonly IReadOnlyDictionary<string, string> _stationNames;
    private readonly ObservableCollection<HistoryRow> _rows = new();
    private int _pageIndex;
    private int _totalCount;

    public HistoryPage(IPassageRecordStore recordStore, IEnumerable<RfidStationConfig>? stations = null)
    {
        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _stationNames = BuildStationNames(stations);
        InitializeComponent();
        HistoryGrid.ItemsSource = _rows;
        PopulateStationFilter(stations);
        StationFilter.SelectedIndex = 0;
        OutcomeFilter.SelectedIndex = 0;
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
        OutcomeFilter.SelectedIndex = 0;
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
            var result = _recordStore.Query(BuildQuery());
            _totalCount = result.TotalCount;
            _rows.Clear();
            foreach (var record in result.Items)
            {
                _rows.Add(new HistoryRow(record, FormatStation(record.StationId, _stationNames)));
            }

            var statistics = _recordStore.GetStatistics(DateTimeOffset.Now);
            TodayPassageValue.Text = statistics.TodayPassageCount.ToString(CultureInfo.InvariantCulture);
            TodayNormalValue.Text = statistics.TodayNormalCount.ToString(CultureInfo.InvariantCulture);
            TodayAlarmValue.Text = statistics.TodayAlarmCount.ToString(CultureInfo.InvariantCulture);
            HistoryTotalValue.Text = _recordStore.Records.Count.ToString(CultureInfo.InvariantCulture);

            ListSummaryText.Text = _totalCount == 0
                ? "共 0 条"
                : $"第 {_pageIndex + 1} / {Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize))} 页，共 {_totalCount} 条";
            QueryErrorText.Text = string.Empty;
            PreviousPageButton.IsEnabled = _pageIndex > 0;
            NextPageButton.IsEnabled = (_pageIndex + 1) * PageSize < _totalCount;
            HistoryGrid.SelectedItem = null;
            NoDataText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            QueryErrorText.Text = exception.Message;
            _rows.Clear();
            ListSummaryText.Text = "查询失败";
            TodayPassageValue.Text = "-";
            TodayNormalValue.Text = "-";
            TodayAlarmValue.Text = "-";
            HistoryTotalValue.Text = "-";
            NoDataText.Visibility = Visibility.Visible;
        }
    }

    private PassageQuery BuildQuery()
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
            From = ParseFilterDateTime(FromDateTextBox, "开始时间"),
            To = ParseFilterDateTime(ToDateTextBox, "结束时间")?.AddSeconds(1),
            StationId = stationTag,
            HeadRfid = headRfid,
            Outcome = GetSelectedOutcome(),
            PageIndex = _pageIndex,
            PageSize = PageSize
        };
    }

    private PassageOutcome? GetSelectedOutcome()
    {
        var tag = (OutcomeFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        return tag switch
        {
            nameof(PassageOutcome.Completed) => PassageOutcome.Completed,
            nameof(PassageOutcome.UncouplingAlarm) => PassageOutcome.UncouplingAlarm,
            _ => null
        };
    }

    private void OnHistoryDetailsClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not HistoryRow row)
        {
            return;
        }

        var record = _recordStore.GetDetails(row.Record.PassageId) ?? row.Record;
        var dialog = new PassageDetailsDialog(record, FormatStation(record.StationId, _stationNames), alarmMode: false)
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
            Content = "Legacy / 历史未识别基站",
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
            return "Legacy / 历史未识别基站";
        }

        return stationNames.TryGetValue(normalized, out var name) && name.Length > 0
            ? $"{normalized} · {name}"
            : normalized;
    }

    private void SetDefaultDateTimeFilter()
    {
        FromDateTextBox.Text = FormatFilterDateTime(DateTime.Today);
        ToDateTextBox.Text = FormatFilterDateTime(DateTime.Today.AddDays(1).AddSeconds(-1));
    }

    private static DateTimeOffset? ParseFilterDateTime(TextBox textBox, string fieldName)
    {
        var text = textBox.Text.Trim();
        if (text.Length == 0) return null;

        if (!DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var value) &&
            !DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out value))
        {
            throw new FormatException($"{fieldName}格式为 yyyy-MM-dd HH:mm:ss。");
        }

        return new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value));
    }

    private static string FormatFilterDateTime(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatTime(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private static string FormatRfid(ushort? value) => value.HasValue ? value.Value.ToString("X4") : "-";

    private static string FormatOutcome(PassageOutcome value) => value == PassageOutcome.Completed ? "正常" : "脱节报警";

    private static string FormatClearState(PassageClearState value) => value == PassageClearState.Cleared ? "已清除" : "待清除";

    private sealed class HistoryRow
    {
        public HistoryRow(PassageRecord record, string stationText)
        {
            Record = record;
            CompletedAtText = FormatTime(record.CompletedAt);
            StationText = stationText;
            HeadRfidText = FormatRfid(record.HeadRfid);
            DetectedCountText = record.DetectedVehicleCount.ToString(CultureInfo.InvariantCulture);
            ExpectedCountText = record.ExpectedVehicleCount.ToString(CultureInfo.InvariantCulture);
            OutcomeText = FormatOutcome(record.Outcome);
            ClearStateText = FormatClearState(record.ClearState);
        }

        public PassageRecord Record { get; }
        public string CompletedAtText { get; }
        public string StationText { get; }
        public string HeadRfidText { get; }
        public string DetectedCountText { get; }
        public string ExpectedCountText { get; }
        public string OutcomeText { get; }
        public string ClearStateText { get; }
    }

}
