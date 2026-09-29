using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.ExternalAlarm;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class AlarmForwardingDispatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Dispatches_each_yard_alarm_as_external_status_without_legacy_raw_sender()
    {
        var station560 = CreateStation("RFID-560", "560", 0x01, 63101);
        var station620 = CreateStation("RFID-620", "620", 0x01, 63102);
        var legacySender560 = new RecordingExternalDataInterface();
        var legacySender620 = new RecordingExternalDataInterface();
        var statusSender560 = new RecordingExternalAlarmStatusSender();
        var statusSender620 = new RecordingExternalAlarmStatusSender();
        var coordinator560 = CreateStatusCoordinator(statusSender560);
        var coordinator620 = CreateStatusCoordinator(statusSender620);
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560"), CreateCommunication("620") },
            new[] { station560, station620 },
            CreateSettings(),
            new InMemoryPassageRecordStore(),
            alarmForwardSenders: new Dictionary<string, IExternalDataInterface>(StringComparer.OrdinalIgnoreCase)
            {
                ["560"] = legacySender560,
                ["620"] = legacySender620
            },
            externalAlarmStatusCoordinators: new Dictionary<string, ExternalAlarmStatusCoordinator>(StringComparer.OrdinalIgnoreCase)
            {
                ["560"] = coordinator560,
                ["620"] = coordinator620
            });

        TriggerAlarm(manager.GetContext("560")!, station560, new byte[] { 0x56, 0x01 });
        TriggerAlarm(manager.GetContext("620")!, station620, new byte[] { 0x62, 0x01 });
        await coordinator560.StartAsync();
        await coordinator620.StartAsync();

        var payload560 = await statusSender560.WaitForStatusAsync((byte)ExternalAlarmStatus.UncouplingAlarm);
        var payload620 = await statusSender620.WaitForStatusAsync((byte)ExternalAlarmStatus.UncouplingAlarm);

        Assert.Equal(12, payload560.Length);
        Assert.Equal(12, payload620.Length);
        Assert.Equal((byte)0x01, payload560[2]);
        Assert.Equal((byte)0x03, payload560[4]);
        Assert.Equal((byte)0x02, payload560[7]);
        Assert.Equal((byte)0x01, payload620[2]);
        Assert.Equal((byte)0x03, payload620[4]);
        Assert.Equal((byte)0x02, payload620[7]);
        Assert.Equal(0, legacySender560.SendCount);
        Assert.Equal(0, legacySender620.SendCount);
    }

    [Fact]
    public async Task Sender_failure_does_not_affect_rfid_communication_health()
    {
        var station = CreateStation("RFID-560", "560", 0x01, 63103);
        var legacySender = new RecordingExternalDataInterface();
        var statusSender = new FailingExternalAlarmStatusSender(new InvalidOperationException("loopback unavailable"));
        var coordinator = CreateStatusCoordinator(statusSender);
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560") },
            new[] { station },
            CreateSettings(),
            new InMemoryPassageRecordStore(),
            alarmForwardSenders: new Dictionary<string, IExternalDataInterface>
            {
                ["560"] = legacySender
            },
            externalAlarmStatusCoordinators: new Dictionary<string, ExternalAlarmStatusCoordinator>
            {
                ["560"] = coordinator
            });
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ExternalAlarmStatusSendFailed += (_, _, exception) => failure.TrySetResult(exception);

        var context = manager.GetContext("560")!;
        var beforeErrorCount = context.ErrorCount;
        TriggerAlarm(context, station, new byte[] { 0x70, 0x01 });
        await coordinator.StartAsync();

        var completed = await Task.WhenAny(failure.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(failure.Task, completed);
        Assert.Contains("loopback unavailable", (await failure.Task).Message, StringComparison.Ordinal);
        Assert.Equal(0, legacySender.SendCount);
        var passage = context.RuntimeStates.Single().LastPassageRecord;
        Assert.NotNull(passage);
        Assert.Equal(PassageOutcome.UncouplingAlarm, passage!.Outcome);
        Assert.Null(context.LastError);
        Assert.Equal(beforeErrorCount, context.ErrorCount);
        context.AcknowledgeAlarm(passage.PassageId, Start.AddSeconds(31));
        context.RuntimeCoordinator!.MarkCommandSent(station, RfidPollCommand.Clear, Start.AddSeconds(31));
        context.ProcessFrame(CreateFrame(station, Start.AddSeconds(32), Array.Empty<ushort>(), new byte[] { 0x80 }));
        context.ProcessFrame(CreateFrame(station, Start.AddSeconds(33), Array.Empty<ushort>(), new byte[] { 0x81 }));
        Assert.Equal(PassageLifecycleState.Idle, context.RuntimeStates.Single().LifecycleState);
    }

    [Fact]
    public async Task Missing_raw_does_not_affect_rfid_communication_health()
    {
        var station = CreateStation("RFID-560", "560", 0x01, 63104);
        var legacySender = new RecordingExternalDataInterface();
        var statusSender = new RecordingExternalAlarmStatusSender();
        var coordinator = CreateStatusCoordinator(statusSender);
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560") },
            new[] { station },
            CreateSettings(),
            new InMemoryPassageRecordStore(),
            alarmForwardSenders: new Dictionary<string, IExternalDataInterface>
            {
                ["560"] = legacySender
            },
            externalAlarmStatusCoordinators: new Dictionary<string, ExternalAlarmStatusCoordinator>
            {
                ["560"] = coordinator
            });

        var context = manager.GetContext("560")!;
        var beforeErrorCount = context.ErrorCount;
        TriggerAlarm(context, station, Array.Empty<byte>());
        await coordinator.StartAsync();

        var payload = await statusSender.WaitForStatusAsync((byte)ExternalAlarmStatus.UncouplingAlarm);
        Assert.Equal((byte)0x03, payload[4]);
        Assert.Equal(0, legacySender.SendCount);
        var passage = context.RuntimeStates.Single().LastPassageRecord;
        Assert.NotNull(passage);
        Assert.Equal(PassageOutcome.UncouplingAlarm, passage!.Outcome);
        Assert.Null(context.LastError);
        Assert.Equal(beforeErrorCount, context.ErrorCount);
    }

    private static void TriggerAlarm(
        YardCommunicationContext context,
        RfidStationConfig station,
        byte[] rawPayload)
    {
        context.ProcessFrame(CreateFrame(station, Start, new ushort[] { 0x0001, 0x0011 }, rawPayload));
        context.ProcessFrame(CreateFrame(station, Start.AddSeconds(29.9), new ushort[] { 0x0001, 0x0011 }, new byte[] { 0x7F, 0x7F }));
        context.Evaluate(Start.AddSeconds(30));
    }

    private static YardCommunicationConfig CreateCommunication(string yardId) => new()
    {
        YardId = yardId,
        ListenIp = IPAddress.Loopback.ToString(),
        ListenPort = yardId == "560" ? 63201 : 63202,
        Enabled = true
    };

    private static RfidStationConfig CreateStation(string stationId, string yardId, byte address, int port) => new()
    {
        StationId = stationId,
        Name = stationId,
        YardId = yardId,
        IpAddress = IPAddress.Loopback.ToString(),
        Port = port,
        ProtocolAddress = address,
        Enabled = true
    };

    private static RfidSettings CreateSettings() => new()
    {
        ExpectedVehicleCount = 11,
        InterVehicleTimeoutSeconds = 30,
        PollIntervalMs = 1000
    };

    private static RfidStationFrame CreateFrame(
        RfidStationConfig station,
        DateTimeOffset receivedAt,
        IReadOnlyList<ushort> rfids,
        byte[] rawPayload)
    {
        var slots = new ushort[14];
        rfids.Take(14).ToArray().CopyTo(slots, 0);
        return new RfidStationFrame
        {
            StationAddress = station.ProtocolAddress,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, station.Port),
            ReceivedAt = receivedAt,
            RawRfidSlots = slots,
            ValidRfids = rfids.ToArray(),
            ActualNonZeroSlotCount = rfids.Count,
            ReportedCardCount = (byte)rfids.Count,
            RawData = rawPayload
        };
    }

    private sealed class RecordingExternalDataInterface : IExternalDataInterface
    {
        private readonly Exception? _exception;
        private readonly TaskCompletionSource<byte[]> _payload =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingExternalDataInterface(Exception? exception = null) => _exception = exception;

        public int SendCount { get; private set; }

        public Task<byte[]> WaitForPayloadAsync() => _payload.Task;

        public Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            SendCount++;
            if (_exception is not null)
            {
                return Task.FromException(_exception);
            }

            _payload.TrySetResult((byte[])payload.Clone());
            return Task.CompletedTask;
        }
    }

    private static ExternalAlarmStatusCoordinator CreateStatusCoordinator(IExternalAlarmStatusSender sender) =>
        new(new ExternalAlarmFrameCodec(), sender);

    private sealed class RecordingExternalAlarmStatusSender : IExternalAlarmStatusSender
    {
        private readonly object _syncRoot = new();
        private readonly List<byte[]> _payloads = new();

        public int SendCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _payloads.Count;
                }
            }
        }

        public Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _payloads.Add((byte[])payload.Clone());
            }

            return Task.CompletedTask;
        }

        public async Task<byte[]> WaitForStatusAsync(byte status)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                lock (_syncRoot)
                {
                    var match = _payloads.LastOrDefault(payload => payload.Length > 4 && payload[4] == status);
                    if (match is not null)
                    {
                        return (byte[])match.Clone();
                    }
                }

                await Task.Delay(10).ConfigureAwait(false);
            }

            throw new TimeoutException($"Expected external alarm status 0x{status:X2}; got {SendCount} sends.");
        }
    }

    private sealed class FailingExternalAlarmStatusSender : IExternalAlarmStatusSender
    {
        private readonly Exception _exception;

        public FailingExternalAlarmStatusSender(Exception exception) => _exception = exception;

        public Task SendAsync(byte[] payload, CancellationToken cancellationToken) =>
            Task.FromException(_exception);
    }
}
