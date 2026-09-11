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
    private readonly IReadOnlyList<string> _stationIdOptions;
    private readonly ObservableCollection<RfidStationEditorRow> _stationRows = new();
    private SettingsDraft _savedDraft = null!;

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
        var configuredStations = (stations ?? Array.Empty<RfidStationConfig>()).ToArray();
        _stationIdOptions = BuildStationIdOptions(configuredStations);
        foreach (var station in configuredStations)
        {
            var row = RfidStationEditorRow.FromConfig(station, _stationIdOptions);
            row.DisplayIndex = _stationRows.Count + 1;
            _stationRows.Add(row);
        }

        StationsItemsControl.ItemsSource = _stationRows;
        _savedDraft = CaptureDraft();
        _adminModeService.PropertyChanged += OnAdminModePropertyChanged;
        UpdateStationEditorState();
    }

    public event Func<RfidSettings, Task<bool>>? SaveRequested;

    public event Func<IReadOnlyList<RfidStationConfig>, Task<bool>>? StationsSaveRequested;

    public static readonly DependencyProperty StationEditingEnabledProperty =
        DependencyProperty.Register(nameof(StationEditingEnabled), typeof(bool), typeof(SettingsPage), new PropertyMetadata(false));

    public bool StationEditingEnabled
    {
        get => (bool)GetValue(StationEditingEnabledProperty);
        private set => SetValue(StationEditingEnabledProperty, value);
    }

    public bool HasUnsavedChanges => !AreDraftsEqual(_savedDraft, CaptureDraft());

    public async Task<bool> TryLeaveAsync(string reason)
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var dialog = new UnsavedSettingsChangesDialog
        {
            Owner = Window.GetWindow(this)
        };
        dialog.SetReason(reason);
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        return dialog.Result switch
        {
            UnsavedSettingsChangesResult.SaveAndContinue => await SaveChangesAsync(),
            UnsavedSettingsChangesResult.Discard => RestoreSavedDraft(),
            _ => false
        };
    }

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
        StationEditorPanel.IsEnabled = true;
        StationEditingEnabled = _stationsEditable && _adminModeService.IsAdmin;
    }

    private void OnAddStationClick(object sender, RoutedEventArgs e)
    {
        if (!_stationsEditable || !_adminModeService.IsAdmin)
        {
            return;
        }

        _stationRows.Add(new RfidStationEditorRow
        {
            DisplayIndex = _stationRows.Count + 1,
            StationId = GetNextStationId(),
            ProtocolAddress = "03",
            StationIdOptions = _stationIdOptions
        });
    }

    private void OnRemoveStationClick(object sender, RoutedEventArgs e)
    {
        if (!_stationsEditable || !_adminModeService.IsAdmin || sender is not System.Windows.Controls.Button button || button.DataContext is not RfidStationEditorRow row)
        {
            return;
        }

        _stationRows.Remove(row);
        for (var index = 0; index < _stationRows.Count; index++)
        {
            _stationRows[index].DisplayIndex = index + 1;
        }
        StationsItemsControl.Items.Refresh();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveChangesAsync();

    private async Task<bool> SaveChangesAsync()
    {
        if (!int.TryParse(PollIntervalTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) ||
            !int.TryParse(ExpectedVehicleCountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vehicleCount) ||
            !int.TryParse(InterVehicleTimeoutTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout))
        {
            SetSaveResult("三个参数都必须是整数。", true);
            return false;
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
            return false;
        }

        try
        {
            if (SaveRequested is not null && !await SaveRequested(candidate))
            {
                return false;
            }
            if (_stationsEditable && _adminModeService.IsAdmin && StationsSaveRequested is not null &&
                !await StationsSaveRequested(stations))
            {
                return false;
            }
        }
        catch (Exception exception)
        {
            SetSaveResult($"保存失败：{exception.Message}", true);
            return false;
        }

        _savedDraft = CaptureDraft();
        return true;
    }

    private bool RestoreSavedDraft()
    {
        PollIntervalTextBox.Text = _savedDraft.PollIntervalText;
        ExpectedVehicleCountTextBox.Text = _savedDraft.ExpectedVehicleCountText;
        InterVehicleTimeoutTextBox.Text = _savedDraft.InterVehicleTimeoutText;
        _stationRows.Clear();
        foreach (var station in _savedDraft.Stations)
        {
            _stationRows.Add(station.ToRow(_stationIdOptions, _stationRows.Count + 1));
        }
        StationsItemsControl.Items.Refresh();
        ValidationText.Text = string.Empty;
        return true;
    }

    private SettingsDraft CaptureDraft() => new(
        PollIntervalTextBox?.Text ?? string.Empty,
        ExpectedVehicleCountTextBox?.Text ?? string.Empty,
        InterVehicleTimeoutTextBox?.Text ?? string.Empty,
        _stationRows.Select(StationDraft.FromRow).ToArray());

    private static bool AreDraftsEqual(SettingsDraft left, SettingsDraft right)
    {
        if (!string.Equals(left.PollIntervalText, right.PollIntervalText, StringComparison.Ordinal) ||
            !string.Equals(left.ExpectedVehicleCountText, right.ExpectedVehicleCountText, StringComparison.Ordinal) ||
            !string.Equals(left.InterVehicleTimeoutText, right.InterVehicleTimeoutText, StringComparison.Ordinal) ||
            left.Stations.Count != right.Stations.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Stations.Count; index++)
        {
            if (!left.Stations[index].IsSameAs(right.Stations[index]))
            {
                return false;
            }
        }

        return true;
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

    private string GetNextStationId()
    {
        foreach (var option in _stationIdOptions)
        {
            if (_stationRows.All(row => !string.Equals(row.StationId, option, StringComparison.OrdinalIgnoreCase)))
            {
                return option;
            }
        }

        return $"RFID-{_stationRows.Count + 1:00}";
    }

    private static IReadOnlyList<string> BuildStationIdOptions(IEnumerable<RfidStationConfig> stations)
    {
        var options = stations
            .Where(station => station is not null && !string.IsNullOrWhiteSpace(station.StationId))
            .Select(station => station.StationId.Trim())
            .ToList();

        for (var index = 1; index <= 999; index++)
        {
            options.Add($"RFID-{index:00}");
        }

        return options.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private sealed class SettingsDraft
    {
        public SettingsDraft(
            string pollIntervalText,
            string expectedVehicleCountText,
            string interVehicleTimeoutText,
            IReadOnlyList<StationDraft> stations)
        {
            PollIntervalText = pollIntervalText;
            ExpectedVehicleCountText = expectedVehicleCountText;
            InterVehicleTimeoutText = interVehicleTimeoutText;
            Stations = stations;
        }

        public string PollIntervalText { get; }

        public string ExpectedVehicleCountText { get; }

        public string InterVehicleTimeoutText { get; }

        public IReadOnlyList<StationDraft> Stations { get; }
    }

    private sealed class StationDraft
    {
        public StationDraft(
            string stationId,
            string name,
            string ipAddress,
            string port,
            string protocolAddress,
            bool enabled)
        {
            StationId = stationId;
            Name = name;
            IpAddress = ipAddress;
            Port = port;
            ProtocolAddress = protocolAddress;
            Enabled = enabled;
        }

        public string StationId { get; }

        public string Name { get; }

        public string IpAddress { get; }

        public string Port { get; }

        public string ProtocolAddress { get; }

        public bool Enabled { get; }

        public static StationDraft FromRow(RfidStationEditorRow row) => new(
            row.StationId,
            row.Name,
            row.IpAddress,
            row.Port,
            row.ProtocolAddress,
            row.Enabled);

        public RfidStationEditorRow ToRow(IReadOnlyList<string> stationIdOptions, int displayIndex) => new()
        {
            DisplayIndex = displayIndex,
            StationIdOptions = stationIdOptions,
            StationId = StationId,
            Name = Name,
            IpAddress = IpAddress,
            Port = Port,
            ProtocolAddress = ProtocolAddress,
            Enabled = Enabled
        };

        public bool IsSameAs(StationDraft other) =>
            string.Equals(StationId, other.StationId, StringComparison.Ordinal) &&
            string.Equals(Name, other.Name, StringComparison.Ordinal) &&
            string.Equals(IpAddress, other.IpAddress, StringComparison.Ordinal) &&
            string.Equals(Port, other.Port, StringComparison.Ordinal) &&
            string.Equals(ProtocolAddress, other.ProtocolAddress, StringComparison.Ordinal) &&
            Enabled == other.Enabled;
    }
}

public sealed class RfidStationEditorRow
{
    public int DisplayIndex { get; set; }

    public IReadOnlyList<string> StationIdOptions { get; set; } = Array.Empty<string>();

    public string StationId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string Port { get; set; } = string.Empty;

    public string ProtocolAddress { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public static RfidStationEditorRow FromConfig(RfidStationConfig station, IReadOnlyList<string> stationIdOptions) => new()
    {
        StationIdOptions = stationIdOptions,
        StationId = station.StationId,
        Name = station.Name,
        IpAddress = station.IpAddress,
        Port = station.Port > 0 ? station.Port.ToString(CultureInfo.InvariantCulture) : string.Empty,
        ProtocolAddress = station.ProtocolAddress.ToString("X2"),
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
