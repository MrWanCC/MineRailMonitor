using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MineRailMonitor.Simulator.Models;

public sealed class SimulatorStationConfig : INotifyPropertyChanged
{
    private string _yardId = "560";
    private string _stationName = string.Empty;
    private string _listenIp = "127.0.0.1";
    private int _listenPort;
    private byte _protocolAddress;
    private bool _enabled = true;
    private ushort _emptySlotValue;
    private byte _crcHigh;
    private byte _crcLow;

    public string StationName
    {
        get => _stationName;
        set => SetField(ref _stationName, value ?? string.Empty);
    }

    /// <summary>
    /// The yard channel that owns this simulated lower-device endpoint.
    /// Legacy simulator files without this field remain in the 560 group.
    /// </summary>
    public string YardId
    {
        get => _yardId;
        set => SetField(ref _yardId, string.IsNullOrWhiteSpace(value) ? "560" : value.Trim());
    }

    public string ListenIp
    {
        get => _listenIp;
        set => SetField(ref _listenIp, value ?? string.Empty);
    }

    public int ListenPort
    {
        get => _listenPort;
        set => SetField(ref _listenPort, value);
    }

    public byte ProtocolAddress
    {
        get => _protocolAddress;
        set => SetField(ref _protocolAddress, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public ushort EmptySlotValue
    {
        get => _emptySlotValue;
        set => SetField(ref _emptySlotValue, value);
    }

    public byte CrcHigh
    {
        get => _crcHigh;
        set => SetField(ref _crcHigh, value);
    }

    public byte CrcLow
    {
        get => _crcLow;
        set => SetField(ref _crcLow, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SimulatorStationConfig Clone() => new()
    {
        YardId = YardId,
        StationName = StationName,
        ListenIp = ListenIp,
        ListenPort = ListenPort,
        ProtocolAddress = ProtocolAddress,
        Enabled = Enabled,
        EmptySlotValue = EmptySlotValue,
        CrcHigh = CrcHigh,
        CrcLow = CrcLow
    };

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
