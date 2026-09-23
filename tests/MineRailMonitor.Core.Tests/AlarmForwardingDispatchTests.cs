using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class AlarmForwardingDispatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Dispatches_each_yard_alarm_to_its_independent_sender()
    {
        var station560 = CreateStation("RFID-560", "560", 0x01, 63101);
        var station620 = CreateStation("RFID-620", "620", 0x01, 63102);
        var sender560 = new RecordingExternalDataInterface();
        var sender620 = new RecordingExternalDataInterface();
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560"), CreateCommunication("620") },
            new[] { station560, station620 },
            CreateSettings(),
            new InMemoryPassageRecordStore(),
            alarmForwardSenders: new Dictionary<string, IExternalDataInterface>(StringComparer.OrdinalIgnoreCase)
            {
                ["560"] = sender560,
                ["620"] = sender620
            });

        TriggerAlarm(manager.GetContext("560")!, station560, new byte[] { 0x56, 0x01 });
        TriggerAlarm(manager.GetContext("620")!, station620, new byte[] { 0x62, 0x01 });

        var payload560 = await sender560.WaitForPayloadAsync();
        var payload620 = await sender620.WaitForPayloadAsync();

        Assert.Equal(new byte[] { 0x56, 0x01 }, payload560);
        Assert.Equal(new byte[] { 0x62, 0x01 }, payload620);
        Assert.Equal(1, sender560.SendCount);
        Assert.Equal(1, sender620.SendCount);
    }

    [Fact]
    public async Task Sender_failure_does_not_affect_rfid_communication_health()
    {
        var station = CreateStation("RFID-560", "560", 0x01, 63103);
        var sender = new RecordingExternalDataInterface(new InvalidOperationException("loopback unavailable"));
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560") },
            new[] { station },
            CreateSettings(),
            new InMemoryPassageRecordStore(),
            alarmForwardSenders: new Dictionary<string, IExternalDataInterface>
            {
                ["560"] = sender
            });
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.AlarmForwardFailed += (_, _, exception) => failure.TrySetResult(exception);

        var context = manager.GetContext("560")!;
        var beforeErrorCount = context.ErrorCount;
        TriggerAlarm(context, station, new byte[] { 0x70, 0x01 });

        var completed = await Task.WhenAny(failure.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(failure.Task, completed);
        Assert.Contains("loopback unavailable", (await failure.Task).Message, StringComparison.Ordinal);
        Assert.Equal(1, sender.SendCount);
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
        var sender = new RecordingExternalDataInterface();
        using var manager = new YardCommunicationManager(
            new[] { CreateCommunication("560") },
            new[] { station },
            CreateSettings(),
            new InMemoryPassageRecordStore(),
            alarmForwardSenders: new Dictionary<string, IExternalDataInterface>
            {
                ["560"] = sender
            });
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.AlarmForwardFailed += (_, _, exception) => failure.TrySetResult(exception);

        var context = manager.GetContext("560")!;
        var beforeErrorCount = context.ErrorCount;
        TriggerAlarm(context, station, Array.Empty<byte>());

        var completed = await Task.WhenAny(failure.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(failure.Task, completed);
        Assert.Contains("缺少原始 UDP 报文", (await failure.Task).Message, StringComparison.Ordinal);
        Assert.Equal(0, sender.SendCount);
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
}
