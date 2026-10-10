using System.Globalization;
using System;
using MineRailMonitor.Core.Communication;
using System.Windows;
using System.Windows.Input;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Pages;

public partial class PassageDetailsDialog : Window
{
    private readonly Action<Guid, DateTimeOffset>? _acknowledgeAlarmRequested;
    private Guid _passageId;

    public PassageDetailsDialog(
        PassageRecord record,
        string stationText,
        bool alarmMode,
        Action<Guid, DateTimeOffset>? acknowledgeAlarmRequested = null)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        _acknowledgeAlarmRequested = acknowledgeAlarmRequested;
        InitializeComponent();
        LoadRecord(record, stationText, alarmMode);
    }

    private void LoadRecord(PassageRecord record, string stationText, bool alarmMode)
    {
        _passageId = record.PassageId;
        DialogTitleText.Text = alarmMode ? "告警详情" : "记录详情";
        HeaderAccent.Background = alarmMode ? FindResource("AlarmBrush") as System.Windows.Media.Brush : FindResource("PrimaryBlueBrush") as System.Windows.Media.Brush;

        PassageIdValue.Text = record.PassageId.ToString("D");
        StationValue.Text = stationText;
        VehicleNoValue.Text = string.IsNullOrWhiteSpace(record.VehicleNo) ? "未配置车辆" : record.VehicleNo;
        VehicleNameValue.Text = string.IsNullOrWhiteSpace(record.VehicleName) ? "未配置车辆" : record.VehicleName;
        HeadRfidValue.Text = FormatRfid(record.VehicleHeadRfid ?? record.HeadRfid);
        AlarmTimeValue.Text = FormatTime(record.CompletedAt);
        StartedAtValue.Text = FormatTime(record.StartedAt);
        CompletedAtValue.Text = FormatTime(record.CompletedAt);
        ExpectedCountValue.Text = record.ExpectedVehicleCount?.ToString(CultureInfo.InvariantCulture) ?? "未配置";
        DetectedCountValue.Text = record.DetectedVehicleCount.ToString(CultureInfo.InvariantCulture);
        MissingCountValue.Text = record.ExpectedVehicleCount.HasValue
            ? Math.Max(record.ExpectedVehicleCount.Value - record.DetectedVehicleCount, 0).ToString(CultureInfo.InvariantCulture)
            : "—";
        OutcomeValue.Text = FormatOutcome(record.Outcome);
        ClearStateValue.Text = FormatClearState(record.ClearState);
        AlarmStatusValue.Text = AlarmStatusDisplayFormatter.Format(record.AlarmStatus);
        AlarmAcknowledgedAtValue.Text = FormatOptionalTime(record.AlarmAcknowledgedAt);
        AlarmRecoveredAtValue.Text = FormatOptionalTime(record.AlarmRecoveredAt);
        ConfirmAlarmButton.Visibility = alarmMode && record.AlarmStatus == AlarmStatus.Unconfirmed && _acknowledgeAlarmRequested is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        AlarmLabel.Text = record.Outcome == PassageOutcome.Unverified ? "判定说明" : "报警说明";
        AlarmValue.Text = record.Outcome == PassageOutcome.UncouplingAlarm
            ? PassageAlarmDisplayFormatter.FormatDetails(record)
            : record.Outcome == PassageOutcome.Unverified
                ? "车头未匹配车辆配置，完整性未判定"
                : record.AlarmMessage ?? "-";
        WarningValue.Text = record.WarningMessages.Count == 0 ? "-" : string.Join("；", record.WarningMessages);

        var rows = record.RfidObservations.Select(item => new RfidDetailRow(item)).ToArray();
        RfidDetailsGrid.ItemsSource = rows;
        RfidCountText.Text = $"共 {rows.Length} 条，按首次出现顺序";
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnConfirmAlarmClick(object sender, RoutedEventArgs e)
    {
        if (_acknowledgeAlarmRequested is null)
        {
            return;
        }

        try
        {
            _acknowledgeAlarmRequested(_passageId, DateTimeOffset.Now);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ConfirmErrorText.Text = exception.Message;
        }
    }

    private static string FormatTime(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private static string FormatRfid(ushort? value) => value.HasValue ? value.Value.ToString("X4") : "-";

    private static string FormatOutcome(PassageOutcome value) => value switch
    {
        PassageOutcome.Completed => "正常通过",
        PassageOutcome.UncouplingAlarm => "脱节报警",
        PassageOutcome.Unverified => "完整性未判定",
        _ => "未知"
    };

    private static string FormatClearState(PassageClearState value) => value == PassageClearState.Cleared ? "已清除" : "待清除";

    private static string FormatOptionalTime(DateTimeOffset? value) => value.HasValue ? FormatTime(value.Value) : "-";

    private sealed class RfidDetailRow
    {
        public RfidDetailRow(PassageRfidObservation observation)
        {
            SequenceNo = observation.SequenceNo;
            RfidText = observation.RfidValue.ToString("X4");
            FirstSeenAtText = FormatTime(observation.FirstSeenAt);
            BatchNo = observation.BatchNo;
        }

        public int SequenceNo { get; }
        public string RfidText { get; }
        public string FirstSeenAtText { get; }
        public int BatchNo { get; }
    }
}
