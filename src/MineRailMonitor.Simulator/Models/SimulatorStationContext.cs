using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Simulator.Communication;
using MineRailMonitor.Simulator.Protocol;

namespace MineRailMonitor.Simulator.Models;

public sealed class SimulatorStationContext : INotifyPropertyChanged, IDisposable
{
    private const int MaxLogEntries = 300;

    private readonly object _syncRoot = new();
    private readonly SimulatorStation _station;
    private readonly List<string> _logs = new();
    private readonly List<string> _rawHexLogs = new();
    private SimulatorUdpResponder? _udpResponder;
    private RfidSimulatorResponder? _simulatorResponder;
    private CancellationTokenSource? _responderCts;
    private Task? _receiveTask;
    private DateTimeOffset? _nextScenarioStepAt;
    private DateTimeOffset? _lastRequestAt;
    private string _lastCommand = "-";
    private string _requestSource = "-";
    private string _frameParseText = string.Empty;
    private string _statusText = "已停止";
    private string _errorMessage = string.Empty;
    private string _scenarioName = "未选择";
    private TimeSpan _scanInterval = TimeSpan.FromSeconds(3);
    private int _requestCount;
    private int _responseCount;
    private int _clearCount;
    private int _errorCount;
    private bool _isRunning;
    private bool _disposed;

    public SimulatorStationContext(SimulatorStationConfig config)
    {
        Config = config?.Clone() ?? throw new ArgumentNullException(nameof(config));
        _station = new SimulatorStation
        {
            Address = Config.ProtocolAddress,
            Slots = new ushort[ScenarioPlaybackState.SlotCapacity],
            CommandBytes = new byte[4],
            CrcHigh = Config.CrcHigh,
            CrcLow = Config.CrcLow
        };
        Playback = new ScenarioPlaybackState();
    }

    public SimulatorStationConfig Config { get; }

    public ScenarioPlaybackState Playback { get; }

    public SimulatorFaultConfiguration FaultConfiguration
    {
        get
        {
            lock (_syncRoot)
            {
                return _station.FaultConfiguration;
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetField(ref _isRunning, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public string ScenarioName
    {
        get => _scenarioName;
        private set => SetField(ref _scenarioName, value);
    }

    public TimeSpan ScanInterval
    {
        get => _scanInterval;
        private set => SetField(ref _scanInterval, value);
    }

    public DateTimeOffset? NextScenarioStepAt
    {
        get => _nextScenarioStepAt;
        private set => SetField(ref _nextScenarioStepAt, value);
    }

    public DateTimeOffset? LastRequestAt
    {
        get => _lastRequestAt;
        private set => SetField(ref _lastRequestAt, value);
    }

    public string LastCommand
    {
        get => _lastCommand;
        private set => SetField(ref _lastCommand, value);
    }

    public string RequestSource
    {
        get => _requestSource;
        private set => SetField(ref _requestSource, value);
    }

    public string FrameParseText
    {
        get => _frameParseText;
        private set => SetField(ref _frameParseText, value);
    }

    public int RequestCount
    {
        get => _requestCount;
        private set => SetField(ref _requestCount, value);
    }

    public int ResponseCount
    {
        get => _responseCount;
        private set => SetField(ref _responseCount, value);
    }

    public int ClearCount
    {
        get => _clearCount;
        private set => SetField(ref _clearCount, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        private set => SetField(ref _errorCount, value);
    }

    public int ValidRfidCount => SnapshotSlots().Count(value => value != Config.EmptySlotValue && value != 0);

    public string Endpoint => $"{Config.ListenIp}:{Config.ListenPort.ToString(CultureInfo.InvariantCulture)}";

    public string AddressText => Config.ProtocolAddress.ToString("X2", CultureInfo.InvariantCulture);

    public string PlaybackProgress => $"{Playback.CurrentIndex} / {Playback.Sequence.Count}";

    public IReadOnlyList<string> Logs
    {
        get
        {
            lock (_syncRoot)
            {
                return _logs.ToArray();
            }
        }
    }

    public IReadOnlyList<string> RawHexLogs
    {
        get
        {
            lock (_syncRoot)
            {
                return _rawHexLogs.ToArray();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_syncRoot)
        {
            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            if (!Config.Enabled)
            {
                StatusText = "已禁用";
                ErrorMessage = string.Empty;
                return Task.CompletedTask;
            }

            if (!TryValidateConfig(Config, out var validationError))
            {
                SetStoppedError(validationError);
                return Task.CompletedTask;
            }

            try
            {
                var address = IPAddress.Parse(Config.ListenIp.Trim());
                _station.Address = Config.ProtocolAddress;
                _station.CrcHigh = Config.CrcHigh;
                _station.CrcLow = Config.CrcLow;
                _simulatorResponder = new RfidSimulatorResponder(new[] { _station }, Config.EmptySlotValue);
                _udpResponder = new SimulatorUdpResponder(address, Config.ListenPort);
                _udpResponder.PacketHandled += OnPacketHandled;
                _responderCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _receiveTask = ReceiveLoopAsync(_udpResponder, _simulatorResponder, _responderCts.Token);
                IsRunning = true;
                StatusText = "运行中";
                ErrorMessage = string.Empty;
                OnPropertyChanged(nameof(Endpoint));
                AppendLog($"启动应答 · {Endpoint}");
            }
            catch (SocketException exception)
            {
                DisposeResponderNoLock();
                SetStoppedError(exception.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? "端口已被占用"
                    : $"UDP监听失败：{exception.Message}");
            }
            catch (Exception exception)
            {
                DisposeResponderNoLock();
                SetStoppedError($"UDP监听失败：{exception.Message}");
            }
        }

        return Task.CompletedTask;
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            if (_disposed && _udpResponder is null && _responderCts is null)
            {
                return;
            }

            DisposeResponderNoLock();
            Playback.Pause();
            NextScenarioStepAt = null;
            IsRunning = false;
            StatusText = "已停止";
            OnPropertyChanged(nameof(PlaybackProgress));
        }
    }

    public void ClearSlots()
    {
        lock (_syncRoot)
        {
            Array.Clear(_station.Slots, 0, _station.Slots.Length);
            _simulatorResponder?.UpdateSlots(_station.Address, _station.Slots);
            OnPropertyChanged(nameof(ValidRfidCount));
        }
    }

    public void ResetScenario()
    {
        lock (_syncRoot)
        {
            Playback.Reset();
            NextScenarioStepAt = null;
            SyncStationSlotsNoLock(Playback.Slots);
            OnPropertyChanged(nameof(PlaybackProgress));
            OnPropertyChanged(nameof(ValidRfidCount));
        }
    }

    public void LoadScenario(string name, IEnumerable<ushort> sequence)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Scenario name must not be empty.", nameof(name));
        }

        lock (_syncRoot)
        {
            Playback.Load(sequence ?? throw new ArgumentNullException(nameof(sequence)));
            ScenarioName = name;
            NextScenarioStepAt = null;
            SyncStationSlotsNoLock(Playback.Slots);
            OnPropertyChanged(nameof(PlaybackProgress));
            OnPropertyChanged(nameof(ValidRfidCount));
        }
    }

    public bool StepScenario()
    {
        lock (_syncRoot)
        {
            var rfid = Playback.Step();
            if (!rfid.HasValue)
            {
                NextScenarioStepAt = null;
                return false;
            }

            SyncStationSlotsNoLock(Playback.Slots);
            OnPropertyChanged(nameof(PlaybackProgress));
            OnPropertyChanged(nameof(ValidRfidCount));
            if (!Playback.IsPlaying)
            {
                NextScenarioStepAt = null;
            }

            return true;
        }
    }

    public bool StartScenario(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        lock (_syncRoot)
        {
            if (Playback.Sequence.Count == 0 || Playback.CurrentIndex >= Playback.Sequence.Count)
            {
                return false;
            }

            ScanInterval = interval;
            if (Playback.CurrentIndex == 0 && !StepScenario())
            {
                return false;
            }

            if (!Playback.Start())
            {
                return false;
            }

            NextScenarioStepAt = DateTimeOffset.UtcNow.Add(interval);
            return true;
        }
    }

    public void PauseScenario()
    {
        lock (_syncRoot)
        {
            Playback.Pause();
            NextScenarioStepAt = null;
        }
    }

    public bool TickScenario(DateTimeOffset now)
    {
        lock (_syncRoot)
        {
            if (!Playback.IsPlaying || !NextScenarioStepAt.HasValue || now < NextScenarioStepAt.Value)
            {
                return false;
            }

            var stepped = StepScenario();
            if (stepped && Playback.IsPlaying)
            {
                NextScenarioStepAt = now.Add(ScanInterval);
            }

            return stepped;
        }
    }

    public bool TryApplyConfiguration(SimulatorStationConfig updated, out string error)
    {
        if (updated is null)
        {
            throw new ArgumentNullException(nameof(updated));
        }

        ThrowIfDisposed();
        var wasRunning = IsRunning;
        Stop();
        ApplyConfig(updated);

        if (!TryValidateConfig(Config, out error))
        {
            SetStoppedError(error);
            return false;
        }

        if (!wasRunning)
        {
            error = string.Empty;
            return true;
        }

        StartAsync().GetAwaiter().GetResult();
        error = ErrorMessage;
        return IsRunning;
    }

    public void SetSlots(IReadOnlyList<ushort> slots)
    {
        if (slots is null)
        {
            throw new ArgumentNullException(nameof(slots));
        }
        if (slots.Count != ScenarioPlaybackState.SlotCapacity)
        {
            throw new ArgumentException($"A simulator station must contain {ScenarioPlaybackState.SlotCapacity} slots.", nameof(slots));
        }

        lock (_syncRoot)
        {
            SyncStationSlotsNoLock(slots);
            OnPropertyChanged(nameof(ValidRfidCount));
        }
    }

    public void SetFaultConfiguration(SimulatorFaultConfiguration configuration)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        ThrowIfDisposed();
        lock (_syncRoot)
        {
            if (ReferenceEquals(_station.FaultConfiguration, configuration))
            {
                return;
            }

            _station.FaultConfiguration = configuration;
            OnPropertyChanged(nameof(FaultConfiguration));
        }
    }

    public void ClearLogs()
    {
        lock (_syncRoot)
        {
            _logs.Clear();
            _rawHexLogs.Clear();
            FrameParseText = string.Empty;
            OnPropertyChanged(nameof(Logs));
            OnPropertyChanged(nameof(RawHexLogs));
        }
    }

    public IReadOnlyList<ushort> SnapshotSlots()
    {
        lock (_syncRoot)
        {
            return _station.Slots.ToArray();
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            DisposeResponderNoLock();
            Playback.Pause();
            NextScenarioStepAt = null;
            IsRunning = false;
            StatusText = "已停止";
            _disposed = true;
        }
    }

    private async Task ReceiveLoopAsync(
        SimulatorUdpResponder responder,
        RfidSimulatorResponder logicResponder,
        CancellationToken cancellationToken)
    {
        try
        {
            await responder.RunAsync(
                (request, _, token) => CreateResponseAsync(logicResponder, request, token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            lock (_syncRoot)
            {
                DisposeResponderNoLock();
                ErrorCount++;
                IsRunning = false;
                StatusText = "异常";
                ErrorMessage = $"UDP接收异常：{exception.Message}";
                AppendLog(ErrorMessage);
            }
        }
    }

    private async Task<byte[]?> CreateResponseAsync(
        RfidSimulatorResponder logicResponder,
        byte[] request,
        CancellationToken cancellationToken)
    {
        var response = logicResponder.CreateResponse(request);
        if (response is null)
        {
            return null;
        }

        SimulatorFaultConfiguration configuration;
        lock (_syncRoot)
        {
            configuration = _station.FaultConfiguration;
        }

        switch (configuration.Mode)
        {
            case SimulatorFaultMode.Drop:
                return null;
            case SimulatorFaultMode.Delay:
                await Task.Delay(configuration.DelayMilliseconds, cancellationToken).ConfigureAwait(false);
                return response;
            case SimulatorFaultMode.InvalidFrame:
                var invalidFrame = (byte[])response.Clone();
                invalidFrame[0] = 0x00;
                return invalidFrame;
            case SimulatorFaultMode.Normal:
            default:
                return response;
        }
    }

    private void OnPacketHandled(IPEndPoint remoteEndPoint, byte[] request, byte[]? response)
    {
        lock (_syncRoot)
        {
            RequestCount++;
            LastRequestAt = DateTimeOffset.Now;
            LastCommand = request.Length > 4 && request[4] == 0x01 ? "Clear" : "Read";
            RequestSource = remoteEndPoint.ToString();
            if (LastCommand == "Clear")
            {
                ClearCount++;
            }

            if (response is null)
            {
                ErrorCount++;
            }
            else
            {
                ResponseCount++;
            }

            AppendLog($"{LastRequestAt:HH:mm:ss.fff}  RX  {remoteEndPoint}  {LastCommand}");
            AppendRawHex($"{LastRequestAt:HH:mm:ss.fff}  RX  {ToHex(request)}");
            if (response is not null)
            {
                AppendLog($"{DateTimeOffset.Now:HH:mm:ss.fff}  TX  {remoteEndPoint}  RFID  {response[7]} cards");
                AppendRawHex($"{DateTimeOffset.Now:HH:mm:ss.fff}  TX  {ToHex(response)}");
            }
            else
            {
                AppendLog($"{DateTimeOffset.Now:HH:mm:ss.fff}  TX  --  无响应");
            }

            FrameParseText = BuildFrameParse(LastCommand, request, response);
            OnPropertyChanged(nameof(ValidRfidCount));
        }
    }

    private void SyncStationSlotsNoLock(IReadOnlyList<ushort> slots)
    {
        Array.Copy(slots.ToArray(), _station.Slots, ScenarioPlaybackState.SlotCapacity);
        _simulatorResponder?.UpdateSlots(_station.Address, _station.Slots);
    }

    private void ApplyConfig(SimulatorStationConfig updated)
    {
        Config.StationName = updated.StationName;
        Config.ListenIp = updated.ListenIp;
        Config.ListenPort = updated.ListenPort;
        Config.ProtocolAddress = updated.ProtocolAddress;
        Config.Enabled = updated.Enabled;
        Config.EmptySlotValue = updated.EmptySlotValue;
        Config.CrcHigh = updated.CrcHigh;
        Config.CrcLow = updated.CrcLow;
        _station.Address = Config.ProtocolAddress;
        _station.CrcHigh = Config.CrcHigh;
        _station.CrcLow = Config.CrcLow;
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(AddressText));
        OnPropertyChanged(nameof(ValidRfidCount));
    }

    private void DisposeResponderNoLock()
    {
        if (_udpResponder is not null)
        {
            _udpResponder.PacketHandled -= OnPacketHandled;
        }

        _responderCts?.Cancel();
        _responderCts?.Dispose();
        _responderCts = null;
        _udpResponder?.Dispose();
        _udpResponder = null;
        _simulatorResponder = null;
        _receiveTask = null;
    }

    private void SetStoppedError(string error)
    {
        IsRunning = false;
        StatusText = "已停止";
        ErrorMessage = error;
        if (!string.IsNullOrWhiteSpace(error))
        {
            ErrorCount++;
            AppendLog(error);
        }
    }

    private void AppendLog(string message)
    {
        _logs.Add(message);
        while (_logs.Count > MaxLogEntries)
        {
            _logs.RemoveAt(0);
        }
        OnPropertyChanged(nameof(Logs));
    }

    private void AppendRawHex(string message)
    {
        _rawHexLogs.Add(message);
        while (_rawHexLogs.Count > MaxLogEntries)
        {
            _rawHexLogs.RemoveAt(0);
        }
        OnPropertyChanged(nameof(RawHexLogs));
    }

    private static string BuildFrameParse(string command, byte[] request, byte[]? response)
    {
        var builder = new StringBuilder();
        var address = request.Length > 2 ? request[2] : (byte)0;
        builder.AppendLine($"地址          {address:X2}");
        builder.AppendLine($"模式          {(request.Length > 3 ? request[3].ToString("X2") : "-")}");
        builder.AppendLine($"命令          {command}");
        builder.AppendLine($"RFID有效数量  {(response is { Length: > 7 } ? response[7].ToString(CultureInfo.InvariantCulture) : "-")}");
        builder.AppendLine();

        for (var index = 0; index < ScenarioPlaybackState.SlotCapacity; index++)
        {
            var offset = 8 + index * 2;
            var value = response is not null && response.Length >= offset + 2
                ? (ushort)(response[offset] | response[offset + 1] << 8)
                : (ushort)0;
            builder.AppendLine($"RFID{index + 1:00}        {value:X4}");
        }

        var crc = response is not null && response.Length >= 38
            ? $"{response[36]:X2} {response[37]:X2}"
            : "-";
        builder.AppendLine();
        builder.AppendLine($"CRC           {crc}");
        return builder.ToString();
    }

    private static string ToHex(byte[] frame) => BitConverter.ToString(frame).Replace('-', ' ');

    private static bool TryValidateConfig(SimulatorStationConfig config, out string error)
    {
        if (!IPAddress.TryParse(config.ListenIp?.Trim(), out _))
        {
            error = "监听地址格式无效。";
            return false;
        }
        if (config.ListenPort is < 1 or > 65535)
        {
            error = "监听端口必须是 1-65535。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SimulatorStationContext));
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
