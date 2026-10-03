using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Pages;

public partial class RfidStatisticsPage : UserControl
{
    private readonly IStatisticsService _statisticsService;
    private readonly ObservableCollection<VehicleStatisticsDisplayItem> _vehicleItems = new();
    private IReadOnlyList<string>? _displayScopeStationIds;
    private string? _displayScopeYardId;

    public RfidStatisticsPage(IStatisticsService statisticsService)
    {
        _statisticsService = statisticsService ?? throw new ArgumentNullException(nameof(statisticsService));

        InitializeComponent();
        VehicleStatisticsGrid.ItemsSource = _vehicleItems;
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var todayStart = new DateTimeOffset(DateTime.Today);
        var tomorrowStart = new DateTimeOffset(DateTime.Today.AddDays(1));
        StatisticsSubtitleText.Text = FormatDisplayScope();
        StatisticsDateText.Text = $"统计日期：{todayStart:yyyy-MM-dd}";
        VehicleStatisticsDateText.Text = $"按车辆编号汇总今日通行和报警情况 · {todayStart:yyyy-MM-dd}";
        StatisticsErrorText.Visibility = Visibility.Collapsed;
        StatisticsSubtitleText.Visibility = Visibility.Visible;

        try
        {
            var overview = _statisticsService.GetOverview(todayStart, tomorrowStart, stationIds: _displayScopeStationIds);
            var vehicles = _statisticsService.GetVehicleStatistics(todayStart, tomorrowStart, stationIds: _displayScopeStationIds);

            PassageMetricValue.Text = overview.TotalCount.ToString(CultureInfo.CurrentCulture);
            NormalMetricValue.Text = overview.NormalCount.ToString(CultureInfo.CurrentCulture);
            AlarmMetricValue.Text = overview.AlarmCount.ToString(CultureInfo.CurrentCulture);
            UnverifiedMetricValue.Text = overview.UnverifiedCount.ToString(CultureInfo.CurrentCulture);
            AlarmRateMetricValue.Text = overview.HasVerifiablePassages
                ? FormatPercentage(overview.AlarmRatePercent)
                : "—";
            AverageMetricValue.Text = $"{overview.AverageDurationSeconds:0.0} 秒";
            OverviewEmptyText.Visibility = overview.TotalCount == 0 ? Visibility.Visible : Visibility.Collapsed;
            OverviewSummaryText.Text = overview.TotalCount == 0
                ? "统计时间范围内尚无完成的车辆通行记录。"
                : "总记录数包含全部已结束Passage；报警率按脱节报警/(正常通过+脱节报警)计算，不含完整性未判定。";

            _vehicleItems.Clear();
            foreach (var item in vehicles)
            {
                _vehicleItems.Add(new VehicleStatisticsDisplayItem(item));
            }

            VehicleGridEmptyText.Visibility = _vehicleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            SetEmptyValues();
            _vehicleItems.Clear();
            OverviewEmptyText.Visibility = Visibility.Collapsed;
            VehicleGridEmptyText.Visibility = Visibility.Collapsed;
            StatisticsSubtitleText.Visibility = Visibility.Collapsed;
            StatisticsErrorText.Text = $"统计查询失败：{exception.Message}";
            StatisticsErrorText.Visibility = Visibility.Visible;
        }
    }

    public void SetDisplayScope(IEnumerable<string>? stationIds, string? yardId = null)
    {
        _displayScopeStationIds = stationIds is null
            ? null
            : stationIds
                .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
                .Select(stationId => stationId!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        _displayScopeYardId = string.IsNullOrWhiteSpace(yardId) ? null : yardId!.Trim();
        Refresh();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

    private void SetEmptyValues()
    {
        PassageMetricValue.Text = "—";
        NormalMetricValue.Text = "—";
        AlarmMetricValue.Text = "—";
        UnverifiedMetricValue.Text = "—";
        AlarmRateMetricValue.Text = "—";
        AverageMetricValue.Text = "—";
    }

    private static string FormatPercentage(double value) =>
        $"{value.ToString("0.0", CultureInfo.CurrentCulture)}%";

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

public sealed class VehicleStatisticsDisplayItem
{
    public VehicleStatisticsDisplayItem(VehicleStatisticsItem item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        VehicleNoText = string.IsNullOrWhiteSpace(item.VehicleNo) ? "未配置" : item.VehicleNo!;
        VehicleNameText = string.IsNullOrWhiteSpace(item.VehicleName) ? "未配置车辆" : item.VehicleName!;
        TotalCount = item.TotalCount;
        AlarmCount = item.AlarmCount;
        AlarmRateText = $"{item.AlarmRatePercent.ToString("0.0", CultureInfo.CurrentCulture)}%";
    }

    public string VehicleNoText { get; }

    public string VehicleNameText { get; }

    public int TotalCount { get; }

    public int AlarmCount { get; }

    public string AlarmRateText { get; }
}
