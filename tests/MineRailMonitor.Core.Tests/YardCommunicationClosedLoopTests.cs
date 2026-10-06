using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Simulator.Models;
using Xunit.Abstractions;

namespace MineRailMonitor.Core.Tests;

public sealed class YardCommunicationClosedLoopTests
{
    private static readonly object PortAllocationLock = new();
    private static readonly HashSet<int> AllocatedPorts = new();

    private readonly ITestOutputHelper _output;
    private readonly object _outputSync = new();

    public YardCommunicationClosedLoopTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Two_yards_complete_independent_udp_poll_runtime_clear_and_restart_loop()
    {
        var testStartedAt = DateTimeOffset.UtcNow;
        var processId = Process.GetCurrentProcess().Id;
        void Trace(string message) => WriteDiagnostic(processId, message);

        Trace($"TEST_START at={testStartedAt:O} processId={processId} managedThreadId={Thread.CurrentThread.ManagedThreadId}");

        var simulator560Port = GetUnusedUdpPort();
        var simulator620Port = GetUnusedUdpPort();
        var simulator560 = CreateSimulator("SIM-560", 0x01, simulator560Port);
        var simulator620 = CreateSimulator("SIM-620", 0x01, simulator620Port);
        simulator560.SetSlots(CreateCompleteTrainSlots());
        simulator620.SetSlots(CreateStableSlot(0x0201));

        var station560 = CreateStation("RFID-560-01", "560", 0x01, simulator560Port);
        var station620 = CreateStation("RFID-620-01", "620", 0x01, simulator620Port);
        var communication560 = CreateCommunication("560");
        var communication620 = CreateCommunication("620");
        var recordStore = new InMemoryPassageRecordStore();
        using var manager = new YardCommunicationManager(
            new[] { communication560, communication620 },
            new[] { station560, station620 },
            CreateSettings(),
            recordStore);

        var context560 = Assert.IsType<YardCommunicationContext>(manager.GetContext("560"));
        var context620 = Assert.IsType<YardCommunicationContext>(manager.GetContext("620"));
        var wire560 = new ContextWireDiagnostics("560", Trace);
        var wire620 = new ContextWireDiagnostics("620", Trace);
        var simulatorDiagnostics560 = new SimulatorDiagnostics("560", simulator560, Trace);
        var simulatorDiagnostics620 = new SimulatorDiagnostics("620", simulator620, Trace);
        var wireByYard = new Dictionary<string, ContextWireDiagnostics>(StringComparer.OrdinalIgnoreCase)
        {
            ["560"] = wire560,
            ["620"] = wire620
        };
        var parser = new RfidFrameParser(new RfidFrameParserOptions { EmptyRfidValue = 0 });

        manager.DatagramSent += (context, args) => wireByYard[context.YardId].RecordSent(args);
        manager.DatagramReceived += (context, args) => ProcessDatagram(context, args, parser);
        manager.DatagramReceived += (context, args) => wireByYard[context.YardId].RecordReceived(context, args);

        try
        {
            Trace($"PORTS simulator560={simulator560Port} simulator620={simulator620Port} configuredListener560={communication560.ListenIp}:{communication560.ListenPort} configuredListener620={communication620.ListenIp}:{communication620.ListenPort}");

            simulatorDiagnostics560.BeginPhase("initial-start", new IPEndPoint(IPAddress.Loopback, communication560.ListenPort));
            simulatorDiagnostics620.BeginPhase("initial-start", new IPEndPoint(IPAddress.Loopback, communication620.ListenPort));
            wire560.BeginPhase("initial-start");
            wire620.BeginPhase("initial-start");

            Trace("simulator560.StartAsync BEGIN");
            await simulator560.StartAsync();
            Trace($"simulator560.StartAsync END running={simulator560.IsRunning} endpoint={simulator560.Endpoint} error={simulator560.ErrorMessage}");
            Trace("simulator620.StartAsync BEGIN");
            await simulator620.StartAsync();
            Trace($"simulator620.StartAsync END running={simulator620.IsRunning} endpoint={simulator620.Endpoint} error={simulator620.ErrorMessage}");

            Trace("manager.StartAllAsync BEGIN");
            await manager.StartAllAsync();
            Trace("manager.StartAllAsync END");
            Trace(BuildContextSnapshot("560", context560, wire560));
            Trace(BuildContextSnapshot("620", context620, wire620));

            Assert.NotEqual(context560.ListenerEndPoint!.Port, context620.ListenerEndPoint!.Port);
            Assert.Equal(new[] { "RFID-560-01" }, context560.Stations.Select(item => item.StationId));
            Assert.Equal(new[] { "RFID-620-01" }, context620.Stations.Select(item => item.StationId));

            await WaitUntilAsync(() => simulator560.ResponseCount > 0 && simulator620.ResponseCount > 0);
            await WaitUntilAsync(() => context560.ResponseCount > 0 && context620.ResponseCount > 0);
            Assert.True(context560.ResponseCount > 0);
            Assert.True(context620.ResponseCount > 0);
            Assert.Equal(0x01, context560.PollingStatuses.Single().StationAddress);
            Assert.Equal(0x01, context620.PollingStatuses.Single().StationAddress);
            Assert.True(context560.PollingStatuses.Single().SentCount > 0);
            Assert.True(context620.PollingStatuses.Single().SentCount > 0);
            Assert.True(context560.PollingStatuses.Single().ReceivedCount > 0);
            Assert.True(context620.PollingStatuses.Single().ReceivedCount > 0);
            Assert.True(context560.PollingStatuses.Single().IsOnline);
            Assert.True(context620.PollingStatuses.Single().IsOnline);
            Assert.Equal(0, context560.PollingStatuses.Single().TimeoutCount);
            Assert.Equal(0, context620.PollingStatuses.Single().TimeoutCount);

            await WaitUntilAsync(() => simulator560.ClearCount > 0);
            var runtime620 = context620.RuntimeStates.Single();
            var runtime620Snapshot = (runtime620.LifecycleState, runtime620.DetectedVehicleCount, runtime620.PendingClear);
            Assert.Equal(0, simulator620.ClearCount);
            Assert.Equal((ushort)0x0201, simulator620.SnapshotSlots()[0]);
            Assert.Equal(runtime620Snapshot.LifecycleState, runtime620.LifecycleState);
            Assert.Equal(runtime620Snapshot.DetectedVehicleCount, runtime620.DetectedVehicleCount);
            Assert.Equal(runtime620Snapshot.PendingClear, runtime620.PendingClear);

            var responsesBeforeStop = simulator620.ResponseCount;
            Trace("manager.StopYardAsync(560) BEGIN");
            Trace(BuildContextSnapshot("560 before stop", context560, wire560));
            await manager.StopYardAsync("560");
            Trace("manager.StopYardAsync(560) END");
            Trace(BuildContextSnapshot("560 after stop", context560, wire560));
            Trace(BuildContextSnapshot("620 after stop 560", context620, wire620));
            Assert.False(context560.IsRunning);
            Assert.True(context620.IsRunning);
            await WaitUntilAsync(() => simulator620.ResponseCount >= responsesBeforeStop + 2);
            Assert.Equal(0, simulator620.ClearCount);

            var responsesBeforeRestart = simulator560.ResponseCount;
            simulatorDiagnostics560.BeginPhase(
                "restart-560",
                new IPEndPoint(IPAddress.Loopback, communication560.ListenPort));
            wire560.BeginPhase("restart-560");
            Trace("manager.RestartYardAsync(560) BEGIN");
            await manager.RestartYardAsync("560");
            Trace("manager.RestartYardAsync(560) END");
            Trace(BuildContextSnapshot("560 after restart", context560, wire560));
            Trace(BuildContextSnapshot("620 after restart 560", context620, wire620));
            Assert.True(context560.IsRunning);
            Assert.True(context620.IsRunning);
            await WaitUntilAsync(() => simulator560.ResponseCount > responsesBeforeRestart);
            Assert.Equal(0, simulator620.ClearCount);
            Assert.True(simulator620.ResponseCount >= responsesBeforeStop + 2);
        }
        catch (Exception exception)
        {
            Trace($"TEST_FAILURE at={DateTimeOffset.UtcNow:O} elapsed={(DateTimeOffset.UtcNow - testStartedAt).TotalMilliseconds:F0}ms exception={exception}");
            Trace("=== 560 STATE ===");
            Trace(BuildContextSnapshot("560", context560, wire560));
            Trace("=== 620 STATE ===");
            Trace(BuildContextSnapshot("620", context620, wire620));
            Trace("=== SIMULATOR 560 ===");
            Trace(simulatorDiagnostics560.Snapshot());
            Trace("=== SIMULATOR 620 ===");
            Trace(simulatorDiagnostics620.Snapshot());
            throw;
        }
        finally
        {
            Trace("manager.StopAllAsync BEGIN");
            try
            {
                await manager.StopAllAsync();
                Trace("manager.StopAllAsync END");
            }
            finally
            {
                simulatorDiagnostics560.Dispose();
                simulatorDiagnostics620.Dispose();
                Trace("simulator560.Dispose BEGIN");
                simulator560.Dispose();
                Trace("simulator560.Dispose END");
                Trace("simulator620.Dispose BEGIN");
                simulator620.Dispose();
                Trace("simulator620.Dispose END");
            }
        }
    }

    private void WriteDiagnostic(int processId, string message)
    {
        lock (_outputSync)
        {
            _output.WriteLine($"[{DateTimeOffset.UtcNow:O}] pid={processId} managedThreadId={Thread.CurrentThread.ManagedThreadId} {message}");
        }
    }

    private static string BuildContextSnapshot(
        string yardId,
        YardCommunicationContext context,
        ContextWireDiagnostics wire)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Yard={yardId} IsRunning={context.IsRunning} Listener={FormatEndpoint(context.ListenerEndPoint)} LastError={context.LastError ?? "<none>"}");
        builder.AppendLine($"ContextCounts requests={context.RequestCount} responses={context.ResponseCount} clears={context.ClearCount} errors={context.ErrorCount} lastCommand={context.LastCommand ?? "<none>"} lastRequestAt={FormatTime(context.LastRequestAt)}");

        foreach (var station in context.Stations)
        {
            station.TryResolveEndpoint(out var remoteEndpoint);
            builder.AppendLine($"Station StationId={station.StationId} RemoteEndpoint={FormatEndpoint(remoteEndpoint)}");
        }

        foreach (var status in context.PollingStatuses.OrderBy(item => item.StationId, StringComparer.OrdinalIgnoreCase))
        {
            var endpoint = status.EndpointKey?.Endpoint;
            builder.AppendLine($"Polling StationId={status.StationId} RemoteEndpoint={FormatEndpoint(endpoint)} IsOnline={status.IsOnline} requests={status.RequestCount} responses={status.ResponseCount} timeouts={status.TimeoutCount} consecutiveTimeouts={status.ConsecutiveTimeoutCount} lastSentAt={FormatTime(status.LastSentAt)} lastReceivedAt={FormatTime(status.LastReceivedAt)} lastErrorAt={FormatTime(status.LastErrorAt)} lastError={status.LastError ?? "<none>"}");
        }

        foreach (var state in context.RuntimeStates.OrderBy(item => item.StationId, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"Runtime StationId={state.StationId} lifecycle={state.LifecycleState} communication={state.CommunicationState} visual={state.VisualState} detected={state.DetectedVehicleCount} pendingClear={state.PendingClear} lastResponseAt={FormatTime(state.LastResponseAt)}");
        }

        builder.AppendLine(wire.Snapshot());
        return builder.ToString().TrimEnd();
    }

    private static string FormatEndpoint(IPEndPoint? endpoint) => endpoint?.ToString() ?? "<none>";

    private static string FormatTime(DateTimeOffset? value) => value?.ToString("O") ?? "<none>";

    private static void ProcessDatagram(
        YardCommunicationContext context,
        RfidUdpDatagramEventArgs args,
        RfidFrameParser parser)
    {
        if (!args.IsValid || args.Data.Length < 4 || args.Data[3] != 0x04 || args.Data.Length < 3 ||
            !context.RecordResponse(args.RemoteEndPoint, args.Data[2], args.ReceivedAt) ||
            !parser.TryParse(args.Data, args.RemoteEndPoint, args.ReceivedAt, out var frame) || frame is null)
        {
            return;
        }

        context.ProcessFrame(frame);
    }

    private static SimulatorStationContext CreateSimulator(string name, byte address, int port) =>
        new(new SimulatorStationConfig
        {
            StationName = name,
            ListenIp = IPAddress.Loopback.ToString(),
            ListenPort = port,
            ProtocolAddress = address,
            Enabled = true,
            EmptySlotValue = 0,
            CrcHigh = 0,
            CrcLow = 0
        });

    private static RfidStationConfig CreateStation(string id, string yardId, byte address, int port) => new()
    {
        StationId = id,
        Name = id,
        YardId = yardId,
        IpAddress = IPAddress.Loopback.ToString(),
        Port = port,
        ProtocolAddress = address,
        Enabled = true,
        Mode = 0x04,
        CommandBytes = new byte[4],
        RequestPayload = new byte[28]
    };

    private static YardCommunicationConfig CreateCommunication(string yardId) => new()
    {
        YardId = yardId,
        ListenIp = IPAddress.Loopback.ToString(),
        ListenPort = GetUnusedUdpPort(),
        Enabled = true
    };

    private static RfidSettings CreateSettings() => new()
    {
        PollIntervalMs = 20,
        ExpectedVehicleCount = 11,
        InterVehicleTimeoutSeconds = 30,
        EmptyRfidValue = 0
    };

    private static ushort[] CreateCompleteTrainSlots()
    {
        var slots = new ushort[14];
        for (var index = 0; index < 11; index++)
        {
            slots[index] = (ushort)(index + 1);
        }

        return slots;
    }

    private static ushort[] CreateStableSlot(ushort value)
    {
        var slots = new ushort[14];
        slots[0] = value;
        return slots;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
        while (!condition())
        {
            if (Stopwatch.GetTimestamp() >= deadline)
            {
                Assert.True(condition(), "等待 560/620 闭环状态超时。");
            }

            await Task.Delay(10);
        }
    }

    private static int GetUnusedUdpPort()
    {
        while (true)
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var port = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
            lock (PortAllocationLock)
            {
                if (AllocatedPorts.Add(port))
                {
                    return port;
                }
            }
        }
    }

    private sealed class ContextWireDiagnostics
    {
        private readonly object _syncRoot = new();
        private readonly string _yardId;
        private readonly Action<string> _trace;
        private int _sentCount;
        private int _receivedCount;
        private int _phaseSentBaseline;
        private int _phaseReceivedBaseline;
        private string _phase = "before-start";
        private bool _phaseFirstSendLogged;
        private bool _phaseFirstReceiveLogged;
        private string? _lastStateSignature;
        private DateTimeOffset? _firstSentAt;
        private DateTimeOffset? _lastSentAt;
        private IPEndPoint? _lastSendLocalEndpoint;
        private IPEndPoint? _lastSendRemoteEndpoint;
        private DateTimeOffset? _firstReceivedAt;
        private DateTimeOffset? _lastReceivedAt;
        private IPEndPoint? _lastReceivedRemoteEndpoint;

        public ContextWireDiagnostics(string yardId, Action<string> trace)
        {
            _yardId = yardId;
            _trace = trace;
        }

        public void BeginPhase(string phase)
        {
            lock (_syncRoot)
            {
                _phase = phase;
                _phaseSentBaseline = _sentCount;
                _phaseReceivedBaseline = _receivedCount;
                _phaseFirstSendLogged = false;
                _phaseFirstReceiveLogged = false;
            }

            _trace($"WIRE_PHASE yard={_yardId} phase={phase} at={DateTimeOffset.UtcNow:O}");
        }

        public void RecordSent(RfidUdpDatagramSentEventArgs args)
        {
            var phaseFirst = false;
            lock (_syncRoot)
            {
                _sentCount++;
                _firstSentAt ??= args.SentAt;
                _lastSentAt = args.SentAt;
                _lastSendLocalEndpoint = args.LocalEndPoint;
                _lastSendRemoteEndpoint = args.DestinationEndPoint;
                if (_sentCount > _phaseSentBaseline && !_phaseFirstSendLogged)
                {
                    _phaseFirstSendLogged = true;
                    phaseFirst = true;
                }
            }

            if (phaseFirst)
            {
                var command = args.Data.Length > 4 && args.Data[4] == 0x01 ? "Clear" : "Read";
                _trace($"FIRST_POLL_SEND yard={_yardId} phase={_phase} at={args.SentAt:O} command={command} local={FormatEndpoint(args.LocalEndPoint)} remote={args.DestinationEndPoint}");
            }
        }

        public void RecordReceived(YardCommunicationContext context, RfidUdpDatagramEventArgs args)
        {
            var phaseFirst = false;
            var stateSignature = BuildStateSignature(context);
            var stateChanged = false;
            lock (_syncRoot)
            {
                _receivedCount++;
                _firstReceivedAt ??= args.ReceivedAt;
                _lastReceivedAt = args.ReceivedAt;
                _lastReceivedRemoteEndpoint = args.RemoteEndPoint;
                if (_receivedCount > _phaseReceivedBaseline && !_phaseFirstReceiveLogged)
                {
                    _phaseFirstReceiveLogged = true;
                    phaseFirst = true;
                }

                if (!string.Equals(_lastStateSignature, stateSignature, StringComparison.Ordinal))
                {
                    _lastStateSignature = stateSignature;
                    stateChanged = true;
                }
            }

            if (phaseFirst)
            {
                _trace($"FIRST_CONTEXT_RECEIVE yard={_yardId} phase={_phase} at={args.ReceivedAt:O} remote={args.RemoteEndPoint} valid={args.IsValid} validationError={args.ValidationError ?? "<none>"}");
            }

            if (stateChanged)
            {
                _trace($"CONTEXT_STATE_TRANSITION yard={_yardId} at={args.ReceivedAt:O} {stateSignature}");
            }
        }

        public string Snapshot()
        {
            lock (_syncRoot)
            {
                return $"WireDiagnostics yard={_yardId} phase={_phase} sent={_sentCount} received={_receivedCount} firstSentAt={FormatTime(_firstSentAt)} lastSentAt={FormatTime(_lastSentAt)} lastSendLocal={FormatEndpoint(_lastSendLocalEndpoint)} lastSendRemote={FormatEndpoint(_lastSendRemoteEndpoint)} firstReceivedAt={FormatTime(_firstReceivedAt)} lastReceivedAt={FormatTime(_lastReceivedAt)} lastReceivedRemote={FormatEndpoint(_lastReceivedRemoteEndpoint)} phaseSent={_sentCount - _phaseSentBaseline} phaseReceived={_receivedCount - _phaseReceivedBaseline}";
            }
        }

        private static string BuildStateSignature(YardCommunicationContext context) => string.Join(
            "; ",
            context.RuntimeStates
                .OrderBy(state => state.StationId, StringComparer.OrdinalIgnoreCase)
                .Select(state => $"StationId={state.StationId} lifecycle={state.LifecycleState} communication={state.CommunicationState} visual={state.VisualState} detected={state.DetectedVehicleCount} pendingClear={state.PendingClear} emptyReads={state.ConsecutiveEmptyReads}"));
    }

    private sealed class SimulatorDiagnostics : IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly string _yardId;
        private readonly SimulatorStationContext _simulator;
        private readonly Action<string> _trace;
        private string _phase = "before-start";
        private string? _expectedRemoteEndpoint;
        private int _phaseRequestBaseline;
        private int _phaseResponseBaseline;
        private bool _phaseFirstRequestLogged;
        private bool _phaseFirstResponseLogged;
        private DateTimeOffset? _lastRequestAt;
        private string _lastRequestSource = "<none>";
        private DateTimeOffset? _lastResponseObservedAt;
        private string? _lastLifecycleSignature;

        public SimulatorDiagnostics(string yardId, SimulatorStationContext simulator, Action<string> trace)
        {
            _yardId = yardId;
            _simulator = simulator;
            _trace = trace;
            _simulator.PropertyChanged += OnSimulatorPropertyChanged;
        }

        public void BeginPhase(string phase, IPEndPoint expectedRemoteEndpoint)
        {
            lock (_syncRoot)
            {
                _phase = phase;
                _expectedRemoteEndpoint = expectedRemoteEndpoint.ToString();
                _phaseRequestBaseline = _simulator.RequestCount;
                _phaseResponseBaseline = _simulator.ResponseCount;
                _phaseFirstRequestLogged = false;
                _phaseFirstResponseLogged = false;
            }

            _trace($"SIMULATOR_PHASE yard={_yardId} phase={phase} at={DateTimeOffset.UtcNow:O} requestBaseline={_phaseRequestBaseline} responseBaseline={_phaseResponseBaseline} expectedRemote={_expectedRemoteEndpoint}");
        }

        public string Snapshot()
        {
            string phase;
            string expectedRemoteEndpoint;
            DateTimeOffset? lastResponseObservedAt;
            DateTimeOffset? lastRequestAt;
            string lastRequestSource;
            lock (_syncRoot)
            {
                phase = _phase;
                expectedRemoteEndpoint = _expectedRemoteEndpoint ?? "<none>";
                lastResponseObservedAt = _lastResponseObservedAt;
                lastRequestAt = _lastRequestAt;
                lastRequestSource = _lastRequestSource;
            }

            var logs = _simulator.Logs;
            var recentLogs = string.Join(" || ", logs.Skip(Math.Max(0, logs.Count - 6)));
            return $"Simulator yard={_yardId} running={_simulator.IsRunning} status={_simulator.StatusText} configuredEndpoint={_simulator.Endpoint} error={_simulator.ErrorMessage ?? "<none>"} requests={_simulator.RequestCount} lastRequestAt={FormatTime(lastRequestAt)} lastRequestSource={lastRequestSource} responses={_simulator.ResponseCount} lastResponseObservedAt={FormatTime(lastResponseObservedAt)} clears={_simulator.ClearCount} errors={_simulator.ErrorCount} phase={phase} expectedRemote={expectedRemoteEndpoint} recentLogs=[{recentLogs}]";
        }

        public void Dispose()
        {
            _simulator.PropertyChanged -= OnSimulatorPropertyChanged;
        }

        private void OnSimulatorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            var logFirstRequest = false;
            var logFirstResponse = false;
            var logLifecycleTransition = false;
            string phase;
            string expectedRemoteEndpoint;
            string source;
            DateTimeOffset? requestAt;
            int requests;
            int responses;
            lock (_syncRoot)
            {
                if (string.Equals(args.PropertyName, nameof(SimulatorStationContext.RequestSource), StringComparison.Ordinal))
                {
                    _lastRequestSource = _simulator.RequestSource;
                    _lastRequestAt = _simulator.LastRequestAt;
                    if (_simulator.RequestCount > _phaseRequestBaseline && IsExpectedRemoteNoLock(_lastRequestSource) && !_phaseFirstRequestLogged)
                    {
                        _phaseFirstRequestLogged = true;
                        logFirstRequest = true;
                    }
                }

                if (string.Equals(args.PropertyName, nameof(SimulatorStationContext.ResponseCount), StringComparison.Ordinal))
                {
                    _lastResponseObservedAt = DateTimeOffset.UtcNow;
                    if (_simulator.RequestCount > _phaseRequestBaseline && IsExpectedRemoteNoLock(_simulator.RequestSource) && !_phaseFirstRequestLogged)
                    {
                        _phaseFirstRequestLogged = true;
                        logFirstRequest = true;
                    }
                    if (_simulator.ResponseCount > _phaseResponseBaseline && IsExpectedRemoteNoLock(_simulator.RequestSource) && !_phaseFirstResponseLogged)
                    {
                        _phaseFirstResponseLogged = true;
                        logFirstResponse = true;
                    }
                }

                if (string.Equals(args.PropertyName, nameof(SimulatorStationContext.ErrorCount), StringComparison.Ordinal) &&
                    _simulator.RequestCount > _phaseRequestBaseline && IsExpectedRemoteNoLock(_simulator.RequestSource) && !_phaseFirstRequestLogged)
                {
                    _phaseFirstRequestLogged = true;
                    logFirstRequest = true;
                }

                if (string.Equals(args.PropertyName, nameof(SimulatorStationContext.ErrorCount), StringComparison.Ordinal) ||
                    string.Equals(args.PropertyName, nameof(SimulatorStationContext.IsRunning), StringComparison.Ordinal) ||
                    string.Equals(args.PropertyName, nameof(SimulatorStationContext.StatusText), StringComparison.Ordinal) ||
                    string.Equals(args.PropertyName, nameof(SimulatorStationContext.ErrorMessage), StringComparison.Ordinal))
                {
                    var signature = $"running={_simulator.IsRunning} status={_simulator.StatusText} error={_simulator.ErrorMessage} errors={_simulator.ErrorCount}";
                    if (!string.Equals(signature, _lastLifecycleSignature, StringComparison.Ordinal))
                    {
                        _lastLifecycleSignature = signature;
                        logLifecycleTransition = true;
                    }
                }

                phase = _phase;
                expectedRemoteEndpoint = _expectedRemoteEndpoint ?? "<none>";
                source = _simulator.RequestSource;
                requestAt = _simulator.LastRequestAt;
                requests = _simulator.RequestCount;
                responses = _simulator.ResponseCount;
            }

            if (logFirstRequest)
            {
                _trace($"SIMULATOR_FIRST_REQUEST yard={_yardId} phase={phase} at={FormatTime(requestAt)} source={source} requests={requests} expectedRemote={expectedRemoteEndpoint}");
            }

            if (logFirstResponse)
            {
                _trace($"SIMULATOR_FIRST_RESPONSE_SENT yard={_yardId} phase={phase} observedAt={DateTimeOffset.UtcNow:O} source={source} responses={responses} expectedRemote={expectedRemoteEndpoint}");
            }

            if (logLifecycleTransition)
            {
                _trace($"SIMULATOR_STATE_TRANSITION yard={_yardId} at={DateTimeOffset.UtcNow:O} running={_simulator.IsRunning} status={_simulator.StatusText} error={_simulator.ErrorMessage} errors={_simulator.ErrorCount}");
            }
        }

        private bool IsExpectedRemoteNoLock(string endpoint) =>
            _expectedRemoteEndpoint is null || string.Equals(endpoint, _expectedRemoteEndpoint, StringComparison.OrdinalIgnoreCase);
    }
}
