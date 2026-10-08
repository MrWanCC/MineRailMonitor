using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Pages;

public partial class RfidStatisticsPage : UserControl
{
    private const int RecentExceptionDisplayLimit = 5;

    private static readonly Brush NeutralBrush = CreateBrush(38, 179, 214);
    private static readonly Brush SuccessBrush = CreateBrush(38, 190, 130);
    private static readonly Brush AlarmBrush = CreateBrush(239, 82, 103);
    private static readonly Brush WarningBrush = CreateBrush(232, 173, 82);
    private static readonly Brush PassageBrush = CreateBrush(48, 160, 230);
    private static readonly Brush MutedBrush = CreateBrush(121, 172, 203);

    private readonly StatisticsDashboardQueryService _dashboardQueryService;
    private readonly StationTagStatisticsQueryService _stationTagStatisticsQueryService;
    private readonly ObservableCollection<KpiMetricDisplayItem> _kpiItems = new();
    private readonly ObservableCollection<AlarmStatusDisplayItem> _alarmStatusItems = new();
    private readonly ObservableCollection<RankingDisplayItem> _alarmStationItems = new();
    private readonly ObservableCollection<RankingDisplayItem> _alarmVehicleItems = new();
    private readonly ObservableCollection<RankingDisplayItem> _passageVehicleItems = new();
    private readonly ObservableCollection<QualityKpiDisplayItem> _qualityKpiItems = new();
    private readonly ObservableCollection<StationQualityDisplayItem> _stationQualityItems = new();
    private readonly ObservableCollection<RankingDisplayItem> _stationTagRankItems = new();
    private readonly ObservableCollection<StationProfileMetricDisplayItem> _stationProfileItems = new();
    private readonly ObservableCollection<StationTagStatisticsDisplayItem> _stationTagItems = new();
    private readonly ObservableCollection<StatisticsRecordDisplayItem> _recentPassageItems = new();
    private readonly ObservableCollection<StatisticsRecordDisplayItem> _recentAlarmItems = new();
    private readonly ObservableCollection<StatisticsRecordDisplayItem> _recentExceptionItems = new();
    private readonly System.Collections.Generic.Dictionary<string, string> _stationNames =
        new(System.StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Generic.Dictionary<string, string> _stationMapPointNames =
        new(System.StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Generic.Dictionary<ushort, string> _vehicleTagLabels = new();
    private readonly System.Collections.Generic.HashSet<ushort> _ambiguousVehicleTagIds = new();
    private IReadOnlyList<string>? _displayScopeStationIds;
    private string? _displayScopeYardId;
    private StationTagStatisticsQueryResult? _currentStationTagStatistics;
    private string? _selectedStationId;
    private bool _isUpdatingStationSelection;
    private bool _refreshLoopRunning;
    private int _refreshVersion;
    private IReadOnlyList<StatisticsTrendBucket> _currentTrend = System.Array.Empty<StatisticsTrendBucket>();
    private StatisticsBucketSize _currentBucketSize;

    public RfidStatisticsPage(IStatisticsService statisticsService, IPassageRecordStore passageRecordStore)
    {
        var validatedPassageRecordStore = passageRecordStore ?? throw new ArgumentNullException(nameof(passageRecordStore));
        _dashboardQueryService = new StatisticsDashboardQueryService(
            statisticsService ?? throw new ArgumentNullException(nameof(statisticsService)),
            validatedPassageRecordStore);
        _stationTagStatisticsQueryService = new StationTagStatisticsQueryService(
            validatedPassageRecordStore,
            statisticsService as IStationTagStatisticsReader);

        InitializeComponent();
        StationQualityKpisItemsControl.ItemsSource = _qualityKpiItems;
        StationQualityListBox.ItemsSource = _stationQualityItems;
        StationTagRankingItemsControl.ItemsSource = _stationTagRankItems;
        StationProfileItemsControl.ItemsSource = _stationProfileItems;
        StationTagStatisticsGrid.ItemsSource = _stationTagItems;
        KpiMetricsItemsControl.ItemsSource = _kpiItems;
        AlarmStatusItemsControl.ItemsSource = _alarmStatusItems;
        AlarmStationRankingItemsControl.ItemsSource = _alarmStationItems;
        AlarmVehicleRankingItemsControl.ItemsSource = _alarmVehicleItems;
        PassageVehicleRankingItemsControl.ItemsSource = _passageVehicleItems;
        RecentPassagesGrid.ItemsSource = _recentPassageItems;
        RecentAlarmsItemsControl.ItemsSource = _recentAlarmItems;
        RecentExceptionsItemsControl.ItemsSource = _recentExceptionItems;
        TimeRangeSelector.SelectedIndex = 0;
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        _refreshVersion++;
        if (_refreshLoopRunning)
        {
            return;
        }

        _refreshLoopRunning = true;
        _ = RefreshLatestAsync();
    }

    private async Task RefreshLatestAsync()
    {
        var completedVersion = -1;
        try
        {
            while (true)
            {
                var version = _refreshVersion;
                var range = StatisticsComparisonRange.ForPreset(GetSelectedPeriod(), DateTimeOffset.Now);
                var stationIds = _displayScopeStationIds?.ToArray();
                StatisticsScopeText.Text = FormatDisplayScope();
                StatisticsErrorText.Visibility = Visibility.Collapsed;

                try
                {
                    var snapshot = await Task.Run(() =>
                    {
                        var dashboard = _dashboardQueryService.Query(range, stationIds);
                        var stationTags = _stationTagStatisticsQueryService.Query(
                            dashboard.Range.Current.Start,
                            dashboard.Range.Current.End,
                            stationIds);
                        return (Dashboard: dashboard, StationTags: stationTags);
                    });

                    if (version == _refreshVersion)
                    {
                        ApplyDashboard(snapshot.Dashboard, snapshot.StationTags);
                    }
                }
                catch (Exception exception)
                {
                    if (version == _refreshVersion)
                    {
                        ClearDashboard();
                        StatisticsErrorText.Text = $"统计查询失败：{exception.Message}";
                        StatisticsErrorText.Visibility = Visibility.Visible;
                    }
                }
                finally
                {
                    if (version == _refreshVersion)
                    {
                        UpdatedAtText.Text = $"数据更新：{DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)}";
                    }
                }

                completedVersion = version;
                if (version == _refreshVersion)
                {
                    break;
                }
            }
        }
        finally
        {
            _refreshLoopRunning = false;
            if (completedVersion != _refreshVersion)
            {
                Refresh();
            }
        }
    }

    private void ApplyDashboard(
        StatisticsDashboardQueryResult result,
        StationTagStatisticsQueryResult tagStatistics)
    {
        UpdateStationTagStatistics(tagStatistics, result.Range.Current.Duration > TimeSpan.FromDays(1));
        UpdateKpis(result);
        UpdateTrend(result.Trend, result.TrendBucketSize);
        UpdateAlarmStatuses(result.AlarmStatuses, result.CurrentOverview.AlarmCount);
        UpdateRankings(result);
        var includeDate = result.Range.Current.Duration > TimeSpan.FromDays(1);
        ReplaceRecords(_recentPassageItems, result.RecentPassages, includeDate);
        ReplaceRecords(_recentAlarmItems, result.RecentAlarms, includeDate);
        ReplaceRecords(_recentExceptionItems, OrderExceptionsForDisplay(result.RecentExceptions), includeDate);
        RecentPassagesGrid.Visibility = _recentPassageItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentPassagesEmptyText.Visibility = _recentPassageItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentAlarmsEmptyText.Visibility = _recentAlarmItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentExceptionsEmptyText.Visibility = _recentExceptionItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetDisplayScope(IEnumerable<string>? stationIds, string? yardId = null)
    {
        _displayScopeStationIds = stationIds is null
            ? null
            : stationIds
                .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
                .Select(stationId => stationId!.Trim())
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToArray();
        _displayScopeYardId = string.IsNullOrWhiteSpace(yardId) ? null : yardId!.Trim();
        Refresh();
    }

    public void SetRfidStations(IEnumerable<RfidStationConfig>? stations)
    {
        _stationNames.Clear();
        foreach (var station in stations ?? System.Array.Empty<RfidStationConfig>())
        {
            if (station is null || string.IsNullOrWhiteSpace(station.StationId) || string.IsNullOrWhiteSpace(station.Name))
            {
                continue;
            }

            _stationNames[station.StationId.Trim()] = station.Name.Trim();
        }
    }

    public void SetMapPointNames(IEnumerable<StationConfig>? yards)
    {
        _stationMapPointNames.Clear();
        var yardList = (yards ?? System.Array.Empty<StationConfig>()).ToArray();
        foreach (var stationId in _stationNames.Keys)
        {
            var binding = RfidStationBindingRules.FindBindings(yardList, stationId).FirstOrDefault();
            if (binding is null)
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(binding.DeviceName)
                ? binding.DeviceId
                : binding.DeviceName;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                _stationMapPointNames[stationId] = displayName.Trim();
            }
        }
    }

    public void SetVehicles(IEnumerable<VehicleConfig>? vehicles)
    {
        _vehicleTagLabels.Clear();
        _ambiguousVehicleTagIds.Clear();
        foreach (var vehicle in vehicles ?? System.Array.Empty<VehicleConfig>())
        {
            if (vehicle is null || !vehicle.Enabled)
            {
                continue;
            }

            var vehicleName = !string.IsNullOrWhiteSpace(vehicle.VehicleName)
                ? vehicle.VehicleName.Trim()
                : !string.IsNullOrWhiteSpace(vehicle.VehicleNo)
                    ? vehicle.VehicleNo.Trim()
                    : "未命名车辆";

            if (vehicle.HeadRfid != 0)
            {
                AddVehicleTagLabel(
                    vehicle.HeadRfid,
                    $"{vehicleName} · 车头标签 {vehicle.HeadRfid.ToString(CultureInfo.CurrentCulture)}");
            }

            foreach (var tag in vehicle.Tags ?? new System.Collections.Generic.List<VehicleTagConfig>())
            {
                if (tag is null || tag.Rfid == 0 || tag.Rfid == vehicle.HeadRfid)
                {
                    continue;
                }

                var tagDescription = tag.Type == VehicleTagType.Head
                    ? $"车头标签 {tag.Rfid.ToString(CultureInfo.CurrentCulture)}"
                    : $"第{tag.Position.ToString(CultureInfo.CurrentCulture)}节标签 {tag.Rfid.ToString(CultureInfo.CurrentCulture)}";
                AddVehicleTagLabel(tag.Rfid, $"{vehicleName} · {tagDescription}");
            }
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

    private void OnPeriodSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            Refresh();
        }
    }

    private void OnTrendChartSizeChanged(object sender, SizeChangedEventArgs e) => RenderTrendChart();

    private void OnStationQualitySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingStationSelection || StationQualityListBox.SelectedItem is not StationQualityDisplayItem selected)
        {
            return;
        }

        _selectedStationId = selected.StationId;
        UpdateSelectedStationDetails();
    }

    private StatisticsPeriod GetSelectedPeriod() => TimeRangeSelector.SelectedIndex switch
    {
        1 => StatisticsPeriod.Yesterday,
        2 => StatisticsPeriod.Last7Days,
        3 => StatisticsPeriod.Last30Days,
        _ => StatisticsPeriod.Today
    };

    private void UpdateKpis(StatisticsDashboardQueryResult result)
    {
        _kpiItems.Clear();
        var current = result.CurrentOverview;
        var previous = result.PreviousOverview;
        var compareText = result.Range.ComparisonLabel;
        _kpiItems.Add(CreateCountMetric("通行次数", current.TotalCount, previous.TotalCount, PassageBrush, compareText, NeutralBrush));
        _kpiItems.Add(CreateCountMetric("正常通过", current.NormalCount, previous.NormalCount, SuccessBrush, compareText, NeutralBrush));
        _kpiItems.Add(CreateCountMetric("脱节报警", current.AlarmCount, previous.AlarmCount, AlarmBrush, compareText,
            GetAlarmChangeBrush(current.AlarmCount, previous.AlarmCount)));
        _kpiItems.Add(CreateCountMetric("完整性未判定", current.UnverifiedCount, previous.UnverifiedCount, WarningBrush, compareText, WarningBrush));

        var currentRate = current.HasVerifiablePassages ? current.AlarmRatePercent : (double?)null;
        var previousRate = previous.HasVerifiablePassages ? previous.AlarmRatePercent : (double?)null;
        var rateDeltaText = currentRate.HasValue && previousRate.HasValue
            ? FormatDifference(currentRate.Value - previousRate.Value, "个百分点")
            : "无对比数据";
        var rateBrush = currentRate.HasValue && previousRate.HasValue
            ? GetAlarmChangeBrush(currentRate.Value, previousRate.Value)
            : MutedBrush;
        _kpiItems.Add(new KpiMetricDisplayItem(
            "报警率",
            currentRate.HasValue ? $"{currentRate.Value.ToString("0.0", CultureInfo.CurrentCulture)}%" : "—",
            rateDeltaText,
            compareText,
            currentRate.HasValue ? AlarmBrush : MutedBrush,
            rateBrush));

        var currentAverage = current.HasAverageDurationSamples ? current.AverageDurationSeconds : (double?)null;
        var previousAverage = previous.HasAverageDurationSamples ? previous.AverageDurationSeconds : (double?)null;
        var averageDeltaText = currentAverage.HasValue && previousAverage.HasValue
            ? FormatDifference(currentAverage.Value - previousAverage.Value, "秒")
            : "无对比数据";
        _kpiItems.Add(new KpiMetricDisplayItem(
            "平均通行时长",
            currentAverage.HasValue ? $"{currentAverage.Value.ToString("0.0", CultureInfo.CurrentCulture)} 秒" : "—",
            averageDeltaText,
            compareText,
            NeutralBrush,
            currentAverage.HasValue && previousAverage.HasValue ? NeutralBrush : MutedBrush));
    }

    private void UpdateStationTagStatistics(StationTagStatisticsQueryResult statistics, bool includeDate)
    {
        _currentStationTagStatistics = statistics;
        _qualityKpiItems.Clear();
        _qualityKpiItems.Add(new QualityKpiDisplayItem("涉及基站", statistics.StationCount.ToString("N0", CultureInfo.CurrentCulture), "有通行记录", NeutralBrush));
        _qualityKpiItems.Add(new QualityKpiDisplayItem("通行批次", statistics.PassageCount.ToString("N0", CultureInfo.CurrentCulture), "已结束通行", PassageBrush));
        _qualityKpiItems.Add(new QualityKpiDisplayItem("理论标签累计", FormatQualityCount(statistics.ExpectedCount), "完整率统计基数", NeutralBrush));
        _qualityKpiItems.Add(new QualityKpiDisplayItem("实际识别累计", FormatQualityCount(statistics.DetectedCount), "按趟去重", PassageBrush));
        _qualityKpiItems.Add(new QualityKpiDisplayItem("缺失标签累计", FormatQualityCount(statistics.MissingCount), "理论与识别差值", AlarmBrush));
        _qualityKpiItems.Add(new QualityKpiDisplayItem("整体完整率", FormatPercent(statistics.CompletenessPercent), "实际识别 / 理论标签", GetCompletenessBrush(statistics.CompletenessPercent)));

        _stationQualityItems.Clear();
        var qualityStations = statistics.Stations
            .OrderByDescending(item => item.CompletenessPercent.HasValue)
            .ThenByDescending(item => item.CompletenessPercent)
            .ThenByDescending(item => item.MissingCount)
            .ThenBy(item => item.StationId, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.StationId, System.StringComparer.Ordinal)
            .ToArray();
        var attentionStations = statistics.Stations
            .Where(item => item.RequiresAttention)
            .ToArray();
        var mostMissing = statistics.Stations
            .Where(item => item.MissingCount is > 0)
            .OrderByDescending(item => item.MissingCount)
            .ThenByDescending(item => item.PassageCount)
            .ThenBy(item => item.StationId, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.StationId, System.StringComparer.Ordinal)
            .FirstOrDefault();
        foreach (var item in qualityStations)
        {
            _stationQualityItems.Add(new StationQualityDisplayItem(
                item,
                FormatStationLabel(item.StationId),
                item.RequiresAttention,
                mostMissing is not null && string.Equals(
                    item.StationId,
                    mostMissing.StationId,
                    System.StringComparison.OrdinalIgnoreCase)));
        }

        if (attentionStations.Length == 0)
        {
            StationQualitySummaryText.Text = qualityStations.Any(item => item.CompletenessPercent.HasValue)
                ? "本期识别正常，无待关注基站"
                : "暂无可判定基站";
        }
        else
        {
            var summaryParts = new System.Collections.Generic.List<string>();
            var lowestCompleteness = attentionStations
                .Where(item => item.CompletenessPercent is < 100d)
                .OrderBy(item => item.CompletenessPercent)
                .ThenByDescending(item => item.MissingCount)
                .FirstOrDefault();
            if (lowestCompleteness is not null)
            {
                summaryParts.Add($"完整率偏低：{FormatStationLabel(lowestCompleteness.StationId)} {FormatPercent(lowestCompleteness.CompletenessPercent)}");
            }
            if (mostMissing is not null)
            {
                summaryParts.Add($"缺失最多：{FormatStationLabel(mostMissing.StationId)} {FormatQualityCount(mostMissing.MissingCount)}");
            }
            var alarmCount = attentionStations.Sum(item => item.UncouplingAlarmCount);
            if (alarmCount > 0)
            {
                summaryParts.Add($"脱节报警：{alarmCount.ToString("N0", CultureInfo.CurrentCulture)} 起");
            }
            StationQualitySummaryText.Text = string.Join("　", summaryParts);
        }
        StationQualityEmptyText.Visibility = _stationQualityItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StationQualityListBox.Visibility = _stationQualityItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var attention = statistics.Stations.FirstOrDefault(item =>
            string.Equals(item.StationId, statistics.AttentionStationId, System.StringComparison.OrdinalIgnoreCase));
        var hasVerifiableQuality = statistics.CompletenessPercent.HasValue;
        var focusBrush = attention is not null
            ? WarningBrush
            : hasVerifiableQuality ? SuccessBrush : MutedBrush;
        FocusStationPanel.BorderBrush = focusBrush;
        FocusStationAccent.Background = focusBrush;
        FocusStationTitleText.Foreground = focusBrush;
        FocusStationTitleText.Text = attention is null ? "识别质量状态" : "重点关注基站";
        FocusStationNameText.Text = attention is not null
            ? FormatStationLabel(attention.StationId)
            : hasVerifiableQuality ? "本期识别正常" : "暂无可判定质量数据";
        FocusStationCompletenessText.Text = attention is not null
            ? $"完整率 {FormatPercent(attention.CompletenessPercent)}"
            : hasVerifiableQuality ? $"整体完整率 {FormatPercent(statistics.CompletenessPercent)}" : "完整率 —";
        FocusStationMissingText.Text = attention is not null
            ? $"缺失 {FormatQualityCount(attention.MissingCount)}"
            : hasVerifiableQuality ? $"缺失累计 {FormatQualityCount(statistics.MissingCount)}" : "缺失 —";
        FocusStationPassagesText.Text = attention is not null
            ? $"通行 {attention.PassageCount.ToString("N0", CultureInfo.CurrentCulture)} 趟"
            : $"通行 {statistics.PassageCount.ToString("N0", CultureInfo.CurrentCulture)} 趟";
        FocusStationCompletenessText.Foreground = attention is null
            ? hasVerifiableQuality ? SuccessBrush : MutedBrush
            : GetCompletenessBrush(attention.CompletenessPercent);

        var selectedStationId = statistics.Stations.Any(item =>
            string.Equals(item.StationId, _selectedStationId, System.StringComparison.OrdinalIgnoreCase))
            ? _selectedStationId
            : qualityStations.FirstOrDefault()?.StationId;
        _selectedStationId = selectedStationId;
        _isUpdatingStationSelection = true;
        try
        {
            StationQualityListBox.SelectedItem = _stationQualityItems.FirstOrDefault(item =>
                string.Equals(item.StationId, selectedStationId, System.StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isUpdatingStationSelection = false;
        }

        _includeStationTagDate = includeDate;
        UpdateSelectedStationDetails();
    }

    private bool _includeStationTagDate;

    private void UpdateSelectedStationDetails()
    {
        _stationProfileItems.Clear();
        _stationTagRankItems.Clear();
        _stationTagItems.Clear();
        var selected = _currentStationTagStatistics?.Stations.FirstOrDefault(item =>
            string.Equals(item.StationId, _selectedStationId, System.StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            SelectedStationTitleText.Text = "基站识别质量";
            StationTagRankingEmptyText.Text = _currentStationTagStatistics?.Stations.Count == 0
                ? "当前范围暂无基站记录"
                : "请选择基站查看标签排行";
            StationTagDetailEmptyText.Text = StationTagRankingEmptyText.Text;
            StationTagRankingEmptyText.Visibility = Visibility.Visible;
            StationTagDetailEmptyText.Visibility = Visibility.Visible;
            StationTagStatisticsGrid.Visibility = Visibility.Collapsed;
            StationTagRankingPanel.Height = 90;
            return;
        }

        SelectedStationTitleText.Text = $"{FormatStationLabel(selected.StationId)} · 识别质量";
        _stationProfileItems.Add(new StationProfileMetricDisplayItem("通行批次", selected.PassageCount.ToString("N0", CultureInfo.CurrentCulture), PassageBrush));
        _stationProfileItems.Add(new StationProfileMetricDisplayItem("理论累计", FormatQualityCount(selected.ExpectedCount), NeutralBrush));
        _stationProfileItems.Add(new StationProfileMetricDisplayItem("实际识别", FormatQualityCount(selected.DetectedCount), PassageBrush));
        _stationProfileItems.Add(new StationProfileMetricDisplayItem("缺失累计", FormatQualityCount(selected.MissingCount), AlarmBrush));
        _stationProfileItems.Add(new StationProfileMetricDisplayItem("完整率", FormatPercent(selected.CompletenessPercent), GetCompletenessBrush(selected.CompletenessPercent)));
        _stationProfileItems.Add(new StationProfileMetricDisplayItem("脱节报警", selected.UncouplingAlarmCount.ToString("N0", CultureInfo.CurrentCulture), selected.UncouplingAlarmCount > 0 ? AlarmBrush : MutedBrush));
        _stationProfileItems.Add(new StationProfileMetricDisplayItem("标签种类", selected.TagKinds.ToString("N0", CultureInfo.CurrentCulture), NeutralBrush));
        _stationProfileItems.Add(new StationProfileMetricDisplayItem("标签出现趟数", selected.TagOccurrences.ToString("N0", CultureInfo.CurrentCulture), PassageBrush));

        var selectedTags = _currentStationTagStatistics!.Tags
            .Where(item => string.Equals(item.StationId, selected.StationId, System.StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.PassageCount)
            .ThenBy(item => item.RfidValue)
            .ToArray();
        var max = selectedTags.Length == 0 ? 0 : selectedTags.Max(item => item.PassageCount);
        var tagCounts = selectedTags
            .GroupBy(item => item.PassageCount)
            .ToDictionary(group => group.Key, group => group.Count());
        var rank = 0;
        var previousPassageCount = -1;
        for (var index = 0; index < Math.Min(selectedTags.Length, 5); index++)
        {
            var item = selectedTags[index];
            if (index == 0 || item.PassageCount < previousPassageCount)
            {
                rank = index + 1;
                previousPassageCount = item.PassageCount;
            }
            var isTied = tagCounts[item.PassageCount] > 1;
            var isUniqueLeader = rank == 1 && !isTied;
            _stationTagRankItems.Add(new RankingDisplayItem(
                rank.ToString(CultureInfo.CurrentCulture),
                FormatVehicleTagLabel(item.RfidValue),
                $"{item.PassageCount.ToString("N0", CultureInfo.CurrentCulture)} 趟",
                isTied ? "并列" : string.Empty,
                max == 0 ? 0d : 100d * item.PassageCount / max,
                isUniqueLeader ? WarningBrush : NeutralBrush,
                isUniqueLeader ? WarningBrush : PassageBrush));
        }

        foreach (var item in selectedTags)
        {
            _stationTagItems.Add(new StationTagStatisticsDisplayItem(
                item,
                _includeStationTagDate,
                FormatVehicleTagLabel(item.RfidValue)));
        }

        StationTagRankingEmptyText.Visibility = _stationTagRankItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StationTagDetailEmptyText.Visibility = _stationTagItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StationTagStatisticsGrid.Visibility = _stationTagItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        StationTagRankingPanel.Height = 90 + Math.Max(0, _stationTagRankItems.Count - 1) * 31;
    }

    private static string FormatQualityCount(long? count) =>
        count.HasValue ? count.Value.ToString("N0", CultureInfo.CurrentCulture) : "—";

    private static string FormatPercent(double? percent) =>
        percent.HasValue ? $"{percent.Value.ToString("0.0", CultureInfo.CurrentCulture)}%" : "—";

    private static Brush GetCompletenessBrush(double? percent) => !percent.HasValue
        ? MutedBrush
        : percent.Value >= 99d
            ? SuccessBrush
            : percent.Value >= 95d
                ? NeutralBrush
                : AlarmBrush;

    private string FormatStationLabel(string? stationId)
    {
        if (stationId is null)
        {
            return "历史数据";
        }

        var normalizedStationId = stationId.Trim();
        if (normalizedStationId.Length == 0 || string.Equals(normalizedStationId, PassageRecord.LegacyStationId, System.StringComparison.OrdinalIgnoreCase))
        {
            return "历史数据";
        }

        if (_stationMapPointNames.TryGetValue(normalizedStationId, out var mapPointName))
        {
            return mapPointName;
        }

        return _stationNames.TryGetValue(normalizedStationId, out var stationName)
            ? stationName
            : normalizedStationId;
    }

    private void AddVehicleTagLabel(ushort rfid, string label)
    {
        if (_ambiguousVehicleTagIds.Contains(rfid))
        {
            return;
        }

        if (_vehicleTagLabels.TryGetValue(rfid, out var existingLabel))
        {
            if (!string.Equals(existingLabel, label, System.StringComparison.Ordinal))
            {
                _vehicleTagLabels.Remove(rfid);
                _ambiguousVehicleTagIds.Add(rfid);
            }

            return;
        }

        _vehicleTagLabels.Add(rfid, label);
    }

    private string FormatVehicleTagLabel(ushort rfid)
    {
        if (_ambiguousVehicleTagIds.Contains(rfid))
        {
            return $"标签配置冲突 · RFID {rfid.ToString("X4", CultureInfo.InvariantCulture)}";
        }

        return _vehicleTagLabels.TryGetValue(rfid, out var label)
            ? label
            : $"未匹配车辆 · RFID {rfid.ToString("X4", CultureInfo.InvariantCulture)}";
    }

    private static KpiMetricDisplayItem CreateCountMetric(
        string title,
        int current,
        int previous,
        Brush accent,
        string comparisonLabel,
        Brush deltaBrush)
    {
        var delta = current - previous;
        var hasComparison = previous != 0 || current != 0;
        var deltaText = !hasComparison
            ? "无对比数据"
            : $"{FormatSignedCount(delta)} / {(previous == 0 ? "—" : FormatPercentChange(current, previous))}";
        var comparisonText = previous == 0 && current > 0
            ? $"{comparisonLabel} · 上一周期无记录"
            : comparisonLabel;
        return new KpiMetricDisplayItem(
            title,
            current.ToString("N0", CultureInfo.CurrentCulture),
            deltaText,
            comparisonText,
            accent,
            hasComparison ? deltaBrush : MutedBrush);
    }

    private void UpdateTrend(IReadOnlyList<StatisticsTrendBucket> trend, StatisticsBucketSize bucketSize)
    {
        _currentTrend = trend;
        _currentBucketSize = bucketSize;
        UpdateTrendSummary();
        TrendEmptyText.Visibility = trend.All(bucket => bucket.TotalCount == 0)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RenderTrendChart();
    }

    private void UpdateTrendSummary()
    {
        var activeBuckets = _currentTrend.Where(bucket => bucket.TotalCount > 0).ToArray();
        var peakPassage = activeBuckets
            .OrderByDescending(bucket => bucket.TotalCount)
            .ThenByDescending(bucket => bucket.Start)
            .FirstOrDefault();
        if (peakPassage is null)
        {
            TrendPeakPeriodText.Text = "—";
            TrendPeakPassageText.Text = "—";
        }
        else
        {
            TrendPeakPeriodText.Text = FormatTrendSummaryPeriod(peakPassage.Start);
            TrendPeakPassageText.Text = $"{peakPassage.TotalCount.ToString("N0", CultureInfo.CurrentCulture)} 次";
        }

        var peakAlarm = activeBuckets
            .Where(bucket => bucket.AlarmCount > 0)
            .OrderByDescending(bucket => bucket.AlarmCount)
            .ThenByDescending(bucket => bucket.Start)
            .FirstOrDefault();
        TrendAlarmPeakText.Text = peakAlarm is null
            ? "—"
            : $"{FormatTrendSummaryPeriod(peakAlarm.Start)} / {peakAlarm.AlarmCount.ToString("N0", CultureInfo.CurrentCulture)} 次";
        TrendAlarmPeakText.Foreground = peakAlarm is null ? MutedBrush : AlarmBrush;
    }

    private string FormatTrendSummaryPeriod(DateTimeOffset start) => _currentBucketSize == StatisticsBucketSize.Hour
        ? $"{start.ToString("HH:00", CultureInfo.CurrentCulture)}–{start.AddHours(1).ToString("HH:00", CultureInfo.CurrentCulture)}"
        : start.ToString("MM-dd", CultureInfo.CurrentCulture);

    private void RenderTrendChart()
    {
        if (TrendChartCanvas is null)
        {
            return;
        }

        TrendChartCanvas.Children.Clear();
        if (_currentTrend.Count == 0 || _currentTrend.All(bucket => bucket.TotalCount == 0) ||
            TrendChartCanvas.ActualWidth < 40 || TrendChartCanvas.ActualHeight < 50)
        {
            return;
        }

        var left = 34d;
        var right = 12d;
        var top = 12d;
        var bottom = 28d;
        var plotWidth = Math.Max(1d, TrendChartCanvas.ActualWidth - left - right);
        var plotHeight = Math.Max(1d, TrendChartCanvas.ActualHeight - top - bottom);
        var baseline = top + plotHeight;
        var maxCount = Math.Max(1, _currentTrend.Max(bucket => bucket.TotalCount));
        var slotWidth = plotWidth / _currentTrend.Count;
        var passageWidth = Math.Max(3d, Math.Min(_currentTrend.Count == 1 ? 40d : 24d, slotWidth * 0.48d));
        var alarmWidth = Math.Max(2d, Math.Min(8d, slotWidth * 0.2d));
        var ratePoints = new List<Point?>();

        AddChartText("0", 2, baseline - 8, NeutralBrush);
        AddChartText(maxCount.ToString(CultureInfo.CurrentCulture), 2, top - 5, NeutralBrush);
        AddChartLine(left, baseline, left + plotWidth, baseline, CreateBrush(30, 70, 94), 1);

        for (var index = 0; index < _currentTrend.Count; index++)
        {
            var bucket = _currentTrend[index];
            var center = left + slotWidth * (index + 0.5d);
            var totalHeight = plotHeight * bucket.TotalCount / maxCount;
            var alarmHeight = plotHeight * bucket.AlarmCount / maxCount;
            if (bucket.TotalCount > 0)
            {
                var passage = new Rectangle
                {
                    Width = passageWidth,
                    Height = Math.Max(1d, totalHeight),
                    Fill = PassageBrush,
                    RadiusX = 2,
                    RadiusY = 2,
                    ToolTip = $"{FormatBucketLabel(bucket.Start)} 通行 {bucket.TotalCount} 次"
                };
                Canvas.SetLeft(passage, center - passageWidth / 2d - alarmWidth / 3d);
                Canvas.SetTop(passage, baseline - passage.Height);
                TrendChartCanvas.Children.Add(passage);
            }

            if (bucket.AlarmCount > 0)
            {
                var alarm = new Rectangle
                {
                    Width = alarmWidth,
                    Height = Math.Max(2d, alarmHeight),
                    Fill = AlarmBrush,
                    RadiusX = 1,
                    RadiusY = 1,
                    ToolTip = $"{FormatBucketLabel(bucket.Start)} 脱节报警 {bucket.AlarmCount} 次"
                };
                Canvas.SetLeft(alarm, center + passageWidth / 2d - alarmWidth / 3d);
                Canvas.SetTop(alarm, baseline - alarm.Height);
                TrendChartCanvas.Children.Add(alarm);
            }

            var ratePoint = bucket.AlarmRatePercent.HasValue
                ? new Point?(new Point(center, baseline - plotHeight * bucket.AlarmRatePercent.Value / 100d))
                : null;
            ratePoints.Add(ratePoint);
            if (ShouldShowBucketLabel(index, _currentTrend.Count))
            {
                var label = _currentBucketSize == StatisticsBucketSize.Hour
                    ? bucket.Start.ToString("HH", CultureInfo.CurrentCulture)
                    : bucket.Start.ToString("MM-dd", CultureInfo.CurrentCulture);
                AddChartText(label, center - 15, baseline + 6, NeutralBrush, 30, TextAlignment.Center);
            }
        }

        for (var index = 1; index < ratePoints.Count; index++)
        {
            if (ratePoints[index - 1].HasValue && ratePoints[index].HasValue)
            {
                AddChartLine(ratePoints[index - 1]!.Value.X, ratePoints[index - 1]!.Value.Y,
                    ratePoints[index]!.Value.X, ratePoints[index]!.Value.Y, WarningBrush, 2);
            }
        }

        foreach (var point in ratePoints)
        {
            if (!point.HasValue)
            {
                continue;
            }

            var marker = new Ellipse { Width = 4, Height = 4, Fill = WarningBrush };
            Canvas.SetLeft(marker, point.Value.X - 2);
            Canvas.SetTop(marker, point.Value.Y - 2);
            TrendChartCanvas.Children.Add(marker);
        }
    }

    private void UpdateAlarmStatuses(IReadOnlyList<AlarmStatisticsItem> statuses, int alarmTotal)
    {
        _alarmStatusItems.Clear();
        AlarmTotalText.Text = alarmTotal.ToString("N0", CultureInfo.CurrentCulture);
        foreach (var item in statuses)
        {
            var brush = item.Status switch
            {
                AlarmStatus.Unconfirmed => AlarmBrush,
                AlarmStatus.Acknowledged => WarningBrush,
                AlarmStatus.Recovered => SuccessBrush,
                _ => NeutralBrush
            };
            var percent = alarmTotal == 0 ? 0d : item.Count * 100d / alarmTotal;
            _alarmStatusItems.Add(new AlarmStatusDisplayItem(
                FormatAlarmStatus(item.Status),
                item.Count,
                percent,
                percent,
                brush));
        }
    }

    private void UpdateRankings(StatisticsDashboardQueryResult result)
    {
        ReplaceRanking(_alarmStationItems, result.AlarmStations, isVehicle: false);
        ReplaceRanking(_alarmVehicleItems, result.AlarmVehicles, isVehicle: true);
        ReplaceRanking(_passageVehicleItems, result.PassageVehicles, isVehicle: true);
        UpdateRankingSummary(AlarmStationRankingSummaryText, _alarmStationItems.Count, "报警基站");
        UpdateRankingSummary(AlarmVehicleRankingSummaryText, _alarmVehicleItems.Count, "报警车辆");
        UpdateRankingSummary(PassageVehicleRankingSummaryText, _passageVehicleItems.Count, "通行车辆");
        AlarmStationRankingEmptyText.Visibility = _alarmStationItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AlarmVehicleRankingEmptyText.Visibility = _alarmVehicleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PassageVehicleRankingEmptyText.Visibility = _passageVehicleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReplaceRanking(
        ObservableCollection<RankingDisplayItem> target,
        IReadOnlyList<StatisticsRankingItem> source,
        bool isVehicle)
    {
        target.Clear();
        var max = source.Count == 0 ? 0 : source.Max(item => item.Count);
        for (var index = 0; index < source.Count; index++)
        {
            var item = source[index];
            var label = isVehicle
                ? FormatVehicleLabel(item.Key, item.Name)
                : string.IsNullOrWhiteSpace(item.Key)
                    ? "未匹配基站"
                    : string.Equals(item.Key, PassageRecord.LegacyStationId, System.StringComparison.OrdinalIgnoreCase)
                        ? "历史数据"
                    : FormatStationLabel(item.Key);
            target.Add(new RankingDisplayItem(
                (index + 1).ToString("00", CultureInfo.CurrentCulture),
                label,
                $"{item.Count.ToString("N0", CultureInfo.CurrentCulture)} 次",
                $"{item.SharePercent.ToString("0.0", CultureInfo.CurrentCulture)}%",
                max == 0 ? 0d : 100d * item.Count / max,
                index == 0 ? WarningBrush : NeutralBrush,
                index == 0 ? WarningBrush : PassageBrush));
        }
    }

    private static void UpdateRankingSummary(TextBlock summary, int count, string category)
    {
        if (count is > 0 and < 5)
        {
            summary.Text = $"当前统计周期共涉及 {count.ToString("N0", CultureInfo.CurrentCulture)} 个{category}";
            summary.Visibility = Visibility.Visible;
            return;
        }

        summary.Text = string.Empty;
        summary.Visibility = Visibility.Collapsed;
    }

    private static IReadOnlyList<PassageRecord> OrderExceptionsForDisplay(IReadOnlyList<PassageRecord> records) =>
        records
            .OrderBy(GetExceptionDisplayPriority)
            .ThenByDescending(record => record.CompletedAt)
            .Take(RecentExceptionDisplayLimit)
            .ToArray();

    private static int GetExceptionDisplayPriority(PassageRecord record) => record.Outcome switch
    {
        PassageOutcome.UncouplingAlarm when record.AlarmStatus == AlarmStatus.Unconfirmed => 0,
        PassageOutcome.UncouplingAlarm when record.AlarmStatus == AlarmStatus.Acknowledged => 1,
        PassageOutcome.Unverified => 2,
        _ => 3
    };

    private void ReplaceRecords(
        ObservableCollection<StatisticsRecordDisplayItem> target,
        IReadOnlyList<PassageRecord> records,
        bool includeDate)
    {
        target.Clear();
        foreach (var record in records)
        {
            target.Add(new StatisticsRecordDisplayItem(record, includeDate, FormatStationLabel(record.StationId)));
        }
    }

    private void ClearDashboard()
    {
        _currentStationTagStatistics = null;
        _selectedStationId = null;
        _qualityKpiItems.Clear();
        _stationQualityItems.Clear();
        _stationProfileItems.Clear();
        _stationTagRankItems.Clear();
        _stationTagItems.Clear();
        StationTagStatisticsGrid.Visibility = Visibility.Collapsed;
        StationQualityListBox.Visibility = Visibility.Collapsed;
        StationQualityEmptyText.Visibility = Visibility.Collapsed;
        StationQualitySummaryText.Text = string.Empty;
        FocusStationNameText.Text = string.Empty;
        FocusStationCompletenessText.Text = string.Empty;
        FocusStationMissingText.Text = string.Empty;
        FocusStationPassagesText.Text = string.Empty;
        SelectedStationTitleText.Text = string.Empty;
        StationTagRankingEmptyText.Visibility = Visibility.Collapsed;
        StationTagDetailEmptyText.Visibility = Visibility.Collapsed;
        _kpiItems.Clear();
        _alarmStatusItems.Clear();
        _alarmStationItems.Clear();
        _alarmVehicleItems.Clear();
        _passageVehicleItems.Clear();
        AlarmStationRankingEmptyText.Visibility = Visibility.Collapsed;
        AlarmVehicleRankingEmptyText.Visibility = Visibility.Collapsed;
        PassageVehicleRankingEmptyText.Visibility = Visibility.Collapsed;
        _recentPassageItems.Clear();
        _recentAlarmItems.Clear();
        _recentExceptionItems.Clear();
        _currentTrend = System.Array.Empty<StatisticsTrendBucket>();
        TrendChartCanvas.Children.Clear();
        TrendEmptyText.Visibility = Visibility.Collapsed;
        AlarmTotalText.Text = "—";
        TrendPeakPeriodText.Text = "—";
        TrendPeakPassageText.Text = "—";
        TrendAlarmPeakText.Text = "—";
        TrendAlarmPeakText.Foreground = MutedBrush;
        AlarmStationRankingSummaryText.Text = string.Empty;
        AlarmVehicleRankingSummaryText.Text = string.Empty;
        PassageVehicleRankingSummaryText.Text = string.Empty;
        AlarmStationRankingSummaryText.Visibility = Visibility.Collapsed;
        AlarmVehicleRankingSummaryText.Visibility = Visibility.Collapsed;
        PassageVehicleRankingSummaryText.Visibility = Visibility.Collapsed;
        RecentPassagesEmptyText.Visibility = Visibility.Collapsed;
        RecentPassagesGrid.Visibility = Visibility.Collapsed;
        RecentAlarmsEmptyText.Visibility = Visibility.Collapsed;
        RecentExceptionsEmptyText.Visibility = Visibility.Collapsed;
    }

    private void AddChartText(
        string text,
        double left,
        double top,
        Brush foreground,
        double width = 32,
        TextAlignment alignment = TextAlignment.Left)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = 10,
            Width = width,
            TextAlignment = alignment
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        TrendChartCanvas.Children.Add(label);
    }

    private void AddChartLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness };
        TrendChartCanvas.Children.Add(line);
    }

    private static bool ShouldShowBucketLabel(int index, int count) =>
        count <= 7 || index == 0 || index == count - 1 || index % (count <= 24 ? 4 : 5) == 0;

    private static string FormatBucketLabel(DateTimeOffset start) => start.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    private static string FormatVehicleLabel(string? number, string? name)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return "未匹配车辆";
        }

        return string.IsNullOrWhiteSpace(name) ? number! : $"{number} {name}";
    }

    private static string FormatAlarmStatus(AlarmStatus status) => status switch
    {
        AlarmStatus.Unconfirmed => "未确认",
        AlarmStatus.Acknowledged => "已确认",
        AlarmStatus.Recovered => "已恢复",
        _ => "—"
    };

    private static string FormatPercentChange(double current, double previous)
    {
        var percent = (current - previous) * 100d / previous;
        var sign = percent > 0d ? "+" : string.Empty;
        return $"{sign}{percent.ToString("0.0", CultureInfo.CurrentCulture)}%";
    }

    private static string FormatDifference(double difference, string unit)
    {
        return $"{FormatSignedValue(difference)} {unit}";
    }

    private static string FormatSignedCount(int value) =>
        $"{(value > 0 ? "+" : string.Empty)}{value.ToString("N0", CultureInfo.CurrentCulture)}";

    private static string FormatSignedValue(double value) =>
        $"{(value > 0d ? "+" : string.Empty)}{value.ToString("0.0", CultureInfo.CurrentCulture)}";

    private static Brush GetAlarmChangeBrush(double current, double previous) =>
        current < previous ? SuccessBrush : current > previous ? AlarmBrush : NeutralBrush;

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private string FormatDisplayScope()
    {
        if (_displayScopeStationIds is { Count: 0 })
        {
            return string.IsNullOrWhiteSpace(_displayScopeYardId)
                ? "当前范围未配置 RFID 基站"
                : "当前站场未配置 RFID 基站";
        }

        return string.IsNullOrWhiteSpace(_displayScopeYardId)
            ? "统计范围：全部站场"
            : $"统计范围：{_displayScopeYardId}站场";
    }
}

public sealed class QualityKpiDisplayItem
{
    public QualityKpiDisplayItem(string title, string valueText, string subtitleText, Brush accentBrush)
    {
        Title = title;
        ValueText = valueText;
        SubtitleText = subtitleText;
        AccentBrush = accentBrush;
    }

    public string Title { get; }
    public string ValueText { get; }
    public string SubtitleText { get; }
    public Brush AccentBrush { get; }
}

public sealed class StationQualityDisplayItem
{
    private static readonly Brush SuccessBrush = CreateBrush(38, 190, 130);
    private static readonly Brush NeutralBrush = CreateBrush(38, 179, 214);
    private static readonly Brush AlarmBrush = CreateBrush(239, 112, 75);
    private static readonly Brush MutedBrush = CreateBrush(121, 172, 203);

    public StationQualityDisplayItem(StationQualityStatisticsItem item, string stationLabel, bool isAttention, bool isMostMissing)
    {
        StationId = item.StationId;
        StationLabel = stationLabel;
        PassageText = $"{item.PassageCount.ToString("N0", CultureInfo.CurrentCulture)} 趟";
        CompletenessText = item.CompletenessPercent.HasValue
            ? $"{item.CompletenessPercent.Value.ToString("0.0", CultureInfo.CurrentCulture)}%"
            : "未判定";
        MissingText = item.MissingCount.HasValue
            ? $"缺失 {item.MissingCount.Value.ToString("N0", CultureInfo.CurrentCulture)}"
            : "缺失 —";
        CompletenessBrush = !item.CompletenessPercent.HasValue
            ? MutedBrush
            : item.CompletenessPercent.Value >= 99d
                ? SuccessBrush
                : item.CompletenessPercent.Value >= 95d
                    ? NeutralBrush
                    : AlarmBrush;
        AttentionText = isAttention ? "重点关注" : string.Empty;
        MostMissingText = isMostMissing ? "缺失最多" : string.Empty;
        IsAttention = isAttention;
        IsMostMissing = isMostMissing;
    }

    public string StationId { get; }
    public string StationLabel { get; }
    public string PassageText { get; }
    public string CompletenessText { get; }
    public string MissingText { get; }
    public Brush CompletenessBrush { get; }
    public string AttentionText { get; }
    public string MostMissingText { get; }
    public bool IsAttention { get; }
    public bool IsMostMissing { get; }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

public sealed class StationProfileMetricDisplayItem
{
    public StationProfileMetricDisplayItem(string title, string valueText, Brush accentBrush)
    {
        Title = title;
        ValueText = valueText;
        AccentBrush = accentBrush;
    }

    public string Title { get; }
    public string ValueText { get; }
    public Brush AccentBrush { get; }
}

public sealed class StationTagStatisticsDisplayItem
{
    public StationTagStatisticsDisplayItem(StationTagStatisticsItem item, bool includeDate, string displayText)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        StationId = string.Equals(item.StationId, PassageRecord.LegacyStationId, System.StringComparison.OrdinalIgnoreCase)
            ? "历史数据"
            : item.StationId;
        RfidText = displayText;
        PassageCountText = $"{item.PassageCount.ToString("N0", CultureInfo.CurrentCulture)} 趟";
        var timeFormat = includeDate ? "MM-dd HH:mm:ss" : "HH:mm:ss";
        FirstSeenAtText = item.FirstSeenAt.ToLocalTime().ToString(timeFormat, CultureInfo.CurrentCulture);
        LastSeenAtText = item.LastSeenAt.ToLocalTime().ToString(timeFormat, CultureInfo.CurrentCulture);
    }

    public string StationId { get; }

    public string RfidText { get; }

    public string PassageCountText { get; }

    public string FirstSeenAtText { get; }

    public string LastSeenAtText { get; }
}

public sealed class KpiMetricDisplayItem
{
    public KpiMetricDisplayItem(string title, string valueText, string deltaText, string comparisonText, Brush accentBrush, Brush deltaBrush)
    {
        Title = title;
        ValueText = valueText;
        DeltaText = deltaText;
        ComparisonText = comparisonText;
        AccentBrush = accentBrush;
        DeltaBrush = deltaBrush;
    }

    public string Title { get; }
    public string ValueText { get; }
    public string DeltaText { get; }
    public string ComparisonText { get; }
    public Brush AccentBrush { get; }
    public Brush DeltaBrush { get; }
}

public sealed class AlarmStatusDisplayItem
{
    public AlarmStatusDisplayItem(string statusText, int count, double percent, double barWidth, Brush accentBrush)
    {
        StatusText = statusText;
        CountText = count.ToString("N0", CultureInfo.CurrentCulture);
        PercentText = $"{percent.ToString("0.0", CultureInfo.CurrentCulture)}%";
        BarWidth = barWidth;
        AccentBrush = accentBrush;
    }

    public string StatusText { get; }
    public string CountText { get; }
    public string PercentText { get; }
    public double BarWidth { get; }
    public Brush AccentBrush { get; }
}

public sealed class RankingDisplayItem
{
    public RankingDisplayItem(
        string rankText,
        string labelText,
        string countText,
        string shareText,
        double barWidth,
        Brush rankBrush,
        Brush barBrush)
    {
        RankText = rankText;
        LabelText = labelText;
        CountText = countText;
        ShareText = shareText;
        BarWidth = barWidth;
        RankBrush = rankBrush;
        BarBrush = barBrush;
    }

    public string RankText { get; }
    public string LabelText { get; }
    public string CountText { get; }
    public string ShareText { get; }
    public double BarWidth { get; }
    public Brush RankBrush { get; }
    public Brush BarBrush { get; }
}

public sealed class StatisticsRecordDisplayItem
{
    private static readonly Brush NeutralPriorityBrush = CreatePriorityBrush(38, 179, 214);
    private static readonly Brush SuccessPriorityBrush = CreatePriorityBrush(38, 190, 130);
    private static readonly Brush AlarmPriorityBrush = CreatePriorityBrush(239, 82, 103);
    private static readonly Brush WarningPriorityBrush = CreatePriorityBrush(232, 173, 82);

    public StatisticsRecordDisplayItem(PassageRecord record, bool includeDate = false, string? stationName = null)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        TimeText = record.CompletedAt.ToLocalTime().ToString(includeDate ? "MM-dd HH:mm:ss" : "HH:mm:ss", CultureInfo.CurrentCulture);
        var normalizedStationName = stationName?.Trim();
        var normalizedStationId = record.StationId?.Trim();
        StationText = string.Equals(normalizedStationId, PassageRecord.LegacyStationId, System.StringComparison.OrdinalIgnoreCase)
            ? "历史数据"
            : !string.IsNullOrWhiteSpace(normalizedStationName)
                ? normalizedStationName ?? string.Empty
                : !string.IsNullOrWhiteSpace(normalizedStationId)
                    ? normalizedStationId ?? string.Empty
                    : "未匹配基站";
        VehicleNoText = string.IsNullOrWhiteSpace(record.VehicleNo) ? "—" : record.VehicleNo!;
        VehicleNameText = string.IsNullOrWhiteSpace(record.VehicleName) ? "未匹配车辆" : record.VehicleName!;
        OutcomeText = record.Outcome switch
        {
            PassageOutcome.Completed => "正常通过",
            PassageOutcome.UncouplingAlarm => "脱节报警",
            PassageOutcome.Unverified => "完整性未判定",
            _ => "—"
        };
        CountText = $"{record.DetectedVehicleCount}/{record.ExpectedVehicleCount?.ToString(CultureInfo.CurrentCulture) ?? "—"}";
        AlarmStatusText = record.AlarmStatus switch
        {
            AlarmStatus.Unconfirmed => "未确认",
            AlarmStatus.Acknowledged => "已确认",
            AlarmStatus.Recovered => "已恢复",
            _ => "—"
        };
        AlarmSummaryText = $"{OutcomeText} · {CountText}";
        LocationText = string.IsNullOrWhiteSpace(VehicleNoText) || VehicleNoText == "—"
            ? StationText
            : $"{StationText} / {VehicleNoText}";
        PriorityBrush = record.Outcome switch
        {
            PassageOutcome.Unverified => WarningPriorityBrush,
            PassageOutcome.UncouplingAlarm when record.AlarmStatus == AlarmStatus.Unconfirmed => AlarmPriorityBrush,
            PassageOutcome.UncouplingAlarm when record.AlarmStatus == AlarmStatus.Acknowledged => WarningPriorityBrush,
            PassageOutcome.UncouplingAlarm when record.AlarmStatus == AlarmStatus.Recovered => SuccessPriorityBrush,
            _ => NeutralPriorityBrush
        };
        DurationText = record.CompletedAt < record.StartedAt
            ? "—"
            : $"{(record.CompletedAt - record.StartedAt).TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)} 秒";
    }

    public string TimeText { get; }
    public string StationText { get; }
    public string VehicleNoText { get; }
    public string VehicleNameText { get; }
    public string OutcomeText { get; }
    public string CountText { get; }
    public string AlarmStatusText { get; }
    public string AlarmSummaryText { get; }
    public string LocationText { get; }
    public Brush PriorityBrush { get; }
    public string DurationText { get; }

    private static Brush CreatePriorityBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
