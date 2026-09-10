using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Windows;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Pages;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private readonly RfidSettings _settings;
    private readonly AdminModeService _adminModeService;
    private readonly bool _stationsEditable;
    private readonly ObservableCollection<RfidStationEditorRow> _stationRows = new();

    public SettingsPage(
        RfidSettings settings,
        AdminModeService adminModeService,
        IEnumerable<RfidStationConfig>? stations = null,
        bool stationsEditable = true)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _adminModeService = adminModeService ?? throw new ArgumentNullException(nameof(adminModeService));
        _stationsEditable = stationsEditable;
        InitializeComponent();
        PollIntervalTextBox.Text = settings.PollIntervalMs.ToString(CultureInfo.InvariantCulture);
        ExpectedVehicleCountTextBox.Text = settings.ExpectedVehicleCount.ToString(CultureInfo.InvariantCulture);
        InterVehicleTimeoutTextBox.Text = settings.InterVehicleTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        foreach (var station in stations ?? Array.Empty<RfidStationConfig>())
        {
            _stationRows.Add(RfidStationEditorRow.FromConfig(station));
        }

        StationsItemsControl.ItemsSource = _stationRows;
        _adminModeService.PropertyChanged += OnAdminModePropertyChanged;
        UpdateStationEditorState();
    }

    public event Func<RfidSettings, Task>? SaveRequested;

    public event Func<IReadOnlyList<RfidStationConfig>, Task>? StationsSaveRequested;

    private void OnEnterAdminModeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AdminPasswordDialog(_adminModeService)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
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
        StationEditorPanel.IsEnabled = _stationsEditable && _adminModeService.IsAdmin;
    }

    private void OnAddStationClick(object sender, RoutedEventArgs e)
    {
        if (!_stationsEditable || !_adminModeService.IsAdmin)
        {
            return;
        }

        _stationRows.Add(new RfidStationEditorRow());
    }

    private void OnRemoveStationClick(object sender, RoutedEventArgs e)
    {
        if (!_stationsEditable || !_adminModeService.IsAdmin || sender is not System.Windows.Controls.Button button || button.DataContext is not RfidStationEditorRow row)
        {
            return;
        }

        _stationRows.Remove(row);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PollIntervalTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) ||
            !int.TryParse(ExpectedVehicleCountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vehicleCount) ||
            !int.TryParse(InterVehicleTimeoutTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout))
        {
            SetSaveResult("三个参数都必须是整数。", true);
            return;
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
        if (errors.Count > 0 || stations is null)
        {
            SetSaveResult(string.Join(Environment.NewLine, errors), true);
            return;
        }

        if (SaveRequested is not null)
        {
            await SaveRequested(candidate);
        }
        if (_stationsEditable && _adminModeService.IsAdmin && StationsSaveRequested is not null)
        {
            await StationsSaveRequested(stations);
        }
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
}

public sealed class RfidStationEditorRow
{
    public string StationId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string Port { get; set; } = string.Empty;

    public string ProtocolAddress { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public static RfidStationEditorRow FromConfig(RfidStationConfig station) => new()
    {
        StationId = station.StationId,
        Name = station.Name,
        IpAddress = station.IpAddress,
        Port = station.Port > 0 ? station.Port.ToString(CultureInfo.InvariantCulture) : string.Empty,
        ProtocolAddress = station.ProtocolAddress == 0 ? string.Empty : station.ProtocolAddress.ToString("X2"),
        Enabled = station.Enabled
    };

    public RfidStationConfig ToConfigWithoutValidation() => new()
    {
        StationId = StationId.Trim(),
        Name = Name.Trim(),
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
