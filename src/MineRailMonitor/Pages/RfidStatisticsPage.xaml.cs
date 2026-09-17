using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Core.Statistics;

namespace MineRailMonitor.Pages;

public partial class RfidStatisticsPage : UserControl
{
    private const int RecentRecordCount = 10;
    private readonly IPassageRecordStore _recordStore;
    private IReadOnlyList<RfidStationConfig> _stations;
    private IReadOnlyDictionary<string, string> _stationNames;
    private IReadOnlyList<StationConfig> _yardConfigs = Array.Empty<StationConfig>();
    private IReadOnlyList<RfidStationYardOption> _yardFilterOptions;
    private readonly ObservableCollection<RankingRow> _rankingRows = new();
    private readonly ObservableCollection<RecentRow> _recentRows = new();
    private IReadOnlyList<StationRuntimeState> _runtimeStates = Array.Empty<StationRuntimeState>();
    private IReadOnlyList<PassageRecord> _currentRecords = Array.Empty<PassageRecord>();
    private HashSet<string>? _displayScopeStationIds;
    private bool _isYardFilterSync;
    private int _rangeDays = 1;

    public RfidStatisticsPage(IPassageRecordStore recordStore, IEnumerable<RfidStationConfig>? stations = null)
    {
        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _stations = (stations ?? Array.Empty<RfidStationConfig>()).Where(station => station is not null).ToArray();
        _stationNames = BuildStationNames(_stations);
        _yardFilterOptions = RfidYardFilter.BuildOptions(null, _stations);

        InitializeComponent();
        RankingItemsControl.ItemsSource = _rankingRows;
        RecentPassageGrid.ItemsSource = _recentRows;
        YardFilter.ItemsSource = _yardFilterOptions;
        YardFilter.SelectedIndex = 0;
        RebuildStationFilter();
        StationFilter.SelectedIndex = 0;
        OutcomeFilter.SelectedIndex = 0;
        Loaded += (_, _) => Refresh();
        UpdateRangeButtonStyles();
    }

    public void SetRuntimeStates(IEnumerable<StationRuntimeState> states)
    {
        if (states is null) throw new ArgumentNullException(nameof(states));

        _runtimeStates = states
            .Where(state => state is not null)
            .ToArray();
        UpdateRuntimeMetrics();
    }

    public void SetRfidStations(IEnumerable<RfidStationConfig>? stations)
    {
        var selectedStationId = (StationFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        _stations = (stations ?? Array.Empty<RfidStationConfig>())
            .Where(station => station is not null)
            .ToArray();
        _stationNames = BuildStationNames(_stations);
        _yardFilterOptions = RfidYardFilter.BuildOptions(_yardConfigs, _stations);
        SetYardFilterSelection(GetSelectedYardId());
        RebuildStationFilter(selectedStationId);
        Refresh();
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
        RebuildStationFilter((StationFilter.SelectedItem as ComboBoxItem)?.Tag as string);
        Refresh();
    }

    public void Refresh()
    {
        try
        {
            _currentRecords = BuildFilteredRecords();
            var hasNoFilters = GetStationFilter() is null && GetOutcomeFilter() is null && string.IsNullOrWhiteSpace(HeadRfidFilter.Text);
            var todayStatistics = _rangeDays == 1 && hasNoFilters
                ? YardPassageFilter.BuildStatistics(_recordStore.Records, DateTimeOffset.Now, _displayScopeStationIds)
                : null;
            UpdateMetrics(todayStatistics);
            UpdateRanking();
            UpdateRecentRows();
            UpdateInsights();
            DrawTrend();
            QueryErrorText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            QueryErrorText.Text = exception.Message;
            _currentRecords = Array.Empty<PassageRecord>();
            UpdateMetrics(null);
            UpdateRanking();
            UpdateRecentRows();
            UpdateInsights();
            DrawTrend();
        }
    }

    private void OnQueryClick(object sender, RoutedEventArgs e) => Refresh();

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
        _rangeDays = 1;
        StationFilter.SelectedIndex = 0;
        OutcomeFilter.SelectedIndex = 0;
        HeadRfidFilter.Clear();
        UpdateRangeButtonStyles();
        Refresh();
    }

    private void OnTodayRangeClick(object sender, RoutedEventArgs e) => SelectRange(1);

    private void OnLast7DaysClick(object sender, RoutedEventArgs e) => SelectRange(7);

    private void OnLast30DaysClick(object sender, RoutedEventArgs e) => SelectRange(30);

    private void OnTrendCanvasSizeChanged(object sender, SizeChangedEventArgs e) => DrawTrend();

    private void SelectRange(int days)
    {
        _rangeDays = days;
        UpdateRangeButtonStyles();
        Refresh();
    }

    private IReadOnlyList<PassageRecord> BuildFilteredRecords()
    {
        var today = DateTime.Today;
        var start = today.AddDays(-(_rangeDays - 1));
        var endExclusive = today.AddDays(1);
        var stationId = GetStationFilter();
        var outcome = GetOutcomeFilter();
        var headRfid = ParseHeadRfid();

        return YardPassageFilter.Filter(_recordStore.Records, _displayScopeStationIds)
            .Where(record =>
            {
                var localCompletedAt = record.CompletedAt.ToLocalTime().DateTime;
                return localCompletedAt >= start && localCompletedAt < endExclusive;
            })
            .Where(record => stationId is null || string.Equals(record.StationId, stationId, StringComparison.OrdinalIgnoreCase))
            .Where(record => !outcome.HasValue || record.Outcome == outcome.Value)
            .Where(record => !headRfid.HasValue || record.HeadRfid == headRfid.Value)
            .OrderByDescending(record => record.CompletedAt)
            .ToArray();
    }

    private ushort? ParseHeadRfid()
    {
        var text = HeadRfidFilter.Text.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(2);
        }

        if (!ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException("车头RFID必须是4位十六进制，例如 0003。");
        }

        return value;
    }

    private string? GetStationFilter() => (StationFilter.SelectedItem as ComboBoxItem)?.Tag as string;

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

    private PassageOutcome? GetOutcomeFilter()
    {
        var tag = (OutcomeFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        return tag switch
        {
            nameof(PassageOutcome.Completed) => PassageOutcome.Completed,
            nameof(PassageOutcome.UncouplingAlarm) => PassageOutcome.UncouplingAlarm,
            _ => null
        };
    }

    private void UpdateMetrics(PassageStatistics? todayStatistics)
    {
        var total = todayStatistics?.TodayPassageCount ?? _currentRecords.Count;
        var normal = todayStatistics?.TodayNormalCount ?? _currentRecords.Count(record => record.Outcome == PassageOutcome.Completed);
        var alarm = todayStatistics?.TodayAlarmCount ?? _currentRecords.Count(record => record.Outcome == PassageOutcome.UncouplingAlarm);
        PassageMetricValue.Text = total.ToString(CultureInfo.InvariantCulture);
        NormalMetricValue.Text = normal.ToString(CultureInfo.InvariantCulture);
        AlarmMetricValue.Text = alarm.ToString(CultureInfo.InvariantCulture);
        PassageMetricLabel.Text = _rangeDays == 1 ? "今日通过" : $"近{_rangeDays}天通过";
        AverageMetricValue.Text = FormatDuration(_currentRecords.Count == 0
            ? 0
            : _currentRecords.Average(record => Math.Max(0, (record.CompletedAt - record.StartedAt).TotalSeconds)));
        UpdateRuntimeMetrics();
    }

    private void UpdateRuntimeMetrics()
    {
        var visibleStations = _displayScopeStationIds is null
            ? _stations
            : _stations.Where(station => _displayScopeStationIds.Contains(station.StationId)).ToArray();
        var visibleStationIds = visibleStations
            .Select(station => station.StationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleStates = _runtimeStates.Where(state => visibleStationIds.Contains(state.StationId)).ToArray();
        var enabledCount = visibleStations.Count(station => station.Enabled);
        var onlineCount = visibleStates.Count(state => state.CommunicationState == StationCommunicationState.Online);
        var recognizingCount = visibleStates.Count(state => state.LifecycleState == PassageLifecycleState.Recognizing);
        OnlineMetricValue.Text = $"{onlineCount} / {enabledCount}";
        RecognizingMetricValue.Text = recognizingCount.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateRanking()
    {
        var recordGroups = _currentRecords
            .GroupBy(record => record.StationId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var configuredStations = _stations
            .Where(station => station.Enabled &&
                              !string.IsNullOrWhiteSpace(station.StationId) &&
                              (_displayScopeStationIds is null || _displayScopeStationIds.Contains(station.StationId.Trim())))
            .Select(station => station.StationId.Trim())
            .ToArray();
        var stationIds = configuredStations
            .Concat(recordGroups.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var groups = stationIds
            .Select(stationId => new
            {
                StationId = stationId,
                Count = recordGroups.TryGetValue(stationId, out var count) ? count : 0
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var max = groups.Length == 0 ? 1 : groups.Max(item => item.Count);
        RankingScopeText.Text = _displayScopeStationIds is null
            ? $"全部基站（{stationIds.Length.ToString(CultureInfo.InvariantCulture)}）"
            : $"当前站场基站（{stationIds.Length.ToString(CultureInfo.InvariantCulture)}）";

        _rankingRows.Clear();
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            _rankingRows.Add(new RankingRow(
                index + 1,
                FormatStation(group.StationId),
                group.Count,
                Math.Max(0, 160d * group.Count / max)));
        }
    }

    private void UpdateRecentRows()
    {
        _recentRows.Clear();
        foreach (var record in _currentRecords.Take(RecentRecordCount))
        {
            _recentRows.Add(new RecentRow(record, FormatStation(record.StationId)));
        }

        RecentNoDataText.Visibility = _recentRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateInsights()
    {
        var byHour = _currentRecords
            .GroupBy(record => record.CompletedAt.ToLocalTime().Hour)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .FirstOrDefault();
        PeakHourText.Text = byHour is null ? "-" : $"{byHour.Key:00}:00 - {(byHour.Key + 1) % 24:00}:00";

        var alarmStation = _currentRecords
            .Where(record => record.Outcome == PassageOutcome.UncouplingAlarm)
            .GroupBy(record => record.StationId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        AlarmStationText.Text = alarmStation is null ? "-" : FormatStation(alarmStation.Key);

        var longest = _currentRecords
            .Select(record => Math.Max(0, (record.CompletedAt - record.StartedAt).TotalSeconds))
            .DefaultIfEmpty(0)
            .Max();
        LongestDurationText.Text = longest <= 0 ? "-" : FormatDuration(longest);
    }

    private void DrawTrend()
    {
        if (TrendCanvas is null)
        {
            return;
        }

        TrendCanvas.Children.Clear();
        var width = TrendCanvas.ActualWidth > 0 ? TrendCanvas.ActualWidth : TrendCanvas.RenderSize.Width;
        var height = TrendCanvas.ActualHeight > 0 ? TrendCanvas.ActualHeight : 228;
        if (width < 120)
        {
            return;
        }

        const double left = 34;
        const double right = 12;
        const double top = 12;
        const double bottom = 28;
        var plotWidth = Math.Max(30, width - left - right);
        var plotHeight = Math.Max(30, height - top - bottom);
        var trendPoints = RfidTrendAggregator.Build(_currentRecords, DateTimeOffset.Now, _rangeDays);
        var totals = trendPoints.Select(point => point.TotalCount).ToArray();
        var normals = trendPoints.Select(point => point.NormalCount).ToArray();
        var maxValue = Math.Max(5d, totals.DefaultIfEmpty(0).Max());
        maxValue = Math.Ceiling(maxValue / 5d) * 5d;

        for (var index = 0; index <= 4; index++)
        {
            var y = top + plotHeight * index / 4d;
            TrendCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(10, 51, 80)),
                StrokeThickness = 1
            });
            AddCanvasText(maxValue - maxValue * index / 4d, left - 28, y - 8, 24, TextAlignment.Right);
        }

        var pointDenominator = Math.Max(1, trendPoints.Count - 1);
        var labelStep = _rangeDays == 1 ? 2 : _rangeDays == 7 ? 1 : 5;
        for (var index = 0; index < trendPoints.Count; index++)
        {
            if (index % labelStep != 0 && index != trendPoints.Count - 1)
            {
                continue;
            }

            var x = left + plotWidth * index / (double)pointDenominator;
            TrendCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = top,
                Y2 = top + plotHeight,
                Stroke = new SolidColorBrush(Color.FromRgb(10, 51, 80)),
                StrokeThickness = 1
            });
            AddCanvasText(trendPoints[index].Label, x - 22, top + plotHeight + 6, 44, TextAlignment.Center);
        }

        TrendCanvas.Children.Add(CreateTrendPolyline(totals, left, top, plotWidth, plotHeight, maxValue, Color.FromRgb(22, 142, 220)));
        TrendCanvas.Children.Add(CreateTrendPolyline(normals, left, top, plotWidth, plotHeight, maxValue, Color.FromRgb(0, 229, 138)));
    }

    private Polyline CreateTrendPolyline(
        IReadOnlyList<int> values,
        double left,
        double top,
        double width,
        double height,
        double maxValue,
        Color color)
    {
        var line = new Polyline
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2,
            Fill = Brushes.Transparent
        };
        var denominator = Math.Max(1, values.Count - 1);
        for (var index = 0; index < values.Count; index++)
        {
            line.Points.Add(new Point(
                left + width * index / denominator,
                top + height - height * values[index] / maxValue));
        }

        return line;
    }

    private void AddCanvasText(object value, double left, double top, double width, TextAlignment alignment)
    {
        var text = new TextBlock
        {
            Text = value is double number ? number.ToString("0", CultureInfo.InvariantCulture) : value.ToString(),
            Width = width,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(150, 190, 215)),
            TextAlignment = alignment
        };
        Canvas.SetLeft(text, left);
        Canvas.SetTop(text, top);
        TrendCanvas.Children.Add(text);
    }

    private void UpdateRangeButtonStyles()
    {
        var activeBrush = (Brush)FindResource("AccentButtonBrush");
        var inactiveBrush = new SolidColorBrush(Color.FromRgb(11, 42, 67));
        TodayRangeButton.Background = _rangeDays == 1 ? activeBrush : inactiveBrush;
        Last7DaysButton.Background = _rangeDays == 7 ? activeBrush : inactiveBrush;
        Last30DaysButton.Background = _rangeDays == 30 ? activeBrush : inactiveBrush;
    }

    private void RebuildStationFilter(string? selectedStationId = null)
    {
        selectedStationId ??= (StationFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        StationFilter.Items.Clear();
        StationFilter.Items.Add(new ComboBoxItem { Content = "全部基站", Tag = null });
        if (_displayScopeStationIds is null || _displayScopeStationIds.Contains(PassageRecord.LegacyStationId))
        {
            StationFilter.Items.Add(new ComboBoxItem { Content = "历史未识别基站", Tag = PassageRecord.LegacyStationId });
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

    private static IReadOnlyDictionary<string, string> BuildStationNames(IEnumerable<RfidStationConfig> stations) =>
        stations
            .Where(item => !string.IsNullOrWhiteSpace(item.StationId))
            .GroupBy(item => item.StationId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private string FormatStation(string? stationId)
    {
        var normalized = stationId?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || string.Equals(normalized, PassageRecord.LegacyStationId, StringComparison.OrdinalIgnoreCase))
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

    private static Geometry? CreateArcGeometry(double startAngle, double sweepAngle)
    {
        if (sweepAngle <= 0.01)
        {
            return null;
        }

        const double center = 56;
        const double radius = 49;
        if (sweepAngle >= 359.9)
        {
            return new EllipseGeometry(new Point(center, center), radius, radius);
        }

        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweepAngle);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(double center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
    }

    private static string FormatDuration(double seconds) => $"{seconds:0.0} s";

    private sealed class RankingRow
    {
        public RankingRow(int rank, string stationText, int count, double barWidth)
        {
            Rank = rank;
            RankText = rank == 1 ? string.Empty : rank.ToString(CultureInfo.InvariantCulture);
            IsTopRank = rank == 1;
            StationText = stationText;
            Count = count;
            BarWidth = barWidth;
        }

        public int Rank { get; }
        public string RankText { get; }
        public bool IsTopRank { get; }
        public string StationText { get; }
        public int Count { get; }
        public double BarWidth { get; }
    }

    private sealed class RecentRow
    {
        public RecentRow(PassageRecord record, string stationText)
        {
            CompletedAtText = record.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            StationText = stationText;
            HeadRfidText = record.HeadRfid.HasValue ? record.HeadRfid.Value.ToString("X4", CultureInfo.InvariantCulture) : "-";
            ProgressText = $"{record.DetectedVehicleCount} / {record.ExpectedVehicleCount}";
            OutcomeText = record.Outcome == PassageOutcome.Completed ? "正常" : "脱节报警";
            OutcomeBrush = record.Outcome == PassageOutcome.Completed
                ? new SolidColorBrush(Color.FromRgb(0, 229, 138))
                : new SolidColorBrush(Color.FromRgb(255, 78, 87));
            DurationText = FormatDuration(Math.Max(0, (record.CompletedAt - record.StartedAt).TotalSeconds));
            ClearStateText = record.ClearState == PassageClearState.Cleared ? "已清除" : "待清除";
            ClearStateBrush = record.ClearState == PassageClearState.Cleared
                ? new SolidColorBrush(Color.FromRgb(0, 229, 138))
                : new SolidColorBrush(Color.FromRgb(224, 164, 63));
        }

        public string CompletedAtText { get; }
        public string StationText { get; }
        public string HeadRfidText { get; }
        public string ProgressText { get; }
        public string OutcomeText { get; }
        public Brush OutcomeBrush { get; }
        public string DurationText { get; }
        public string ClearStateText { get; }
        public Brush ClearStateBrush { get; }
    }
}
