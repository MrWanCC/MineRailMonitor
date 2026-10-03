using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Infrastructure.Configuration;
using MineRailMonitor.Infrastructure.Logging;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class PassageEventJsonlLoggerTests
{
    [Fact]
    public void Writes_one_json_event_per_line_under_the_passage_log_directory()
    {
        var logsDirectory = Path.Combine(Path.GetTempPath(), "MineRailMonitorTests", Guid.NewGuid().ToString("N"));
        var passageId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 10, 2, 13, 14, 15, TimeSpan.FromHours(8));

        try
        {
            var logger = new PassageEventJsonlLogger(logsDirectory);
            logger.Write(new PassageEvent
            {
                PassageId = passageId,
                EventType = PassageEventTypes.Start,
                OccurredAt = occurredAt,
                StationId = "RFID-01",
                StationAddress = 1,
                VehicleNo = "K001",
                VehicleName = "1号矿车",
                VehicleHeadRfid = 1,
                ExpectedCount = 7
            });

            var path = Path.Combine(logsDirectory, "Passage", "passage-event-20261002.jsonl");
            var line = Assert.Single(File.ReadAllLines(path));
            using var document = JsonDocument.Parse(line);

            Assert.Equal(passageId, document.RootElement.GetProperty("PassageId").GetGuid());
            Assert.Equal("PASSAGE_START", document.RootElement.GetProperty("EventType").GetString());
            Assert.Equal("K001", document.RootElement.GetProperty("VehicleNo").GetString());
            Assert.Equal("1号矿车", document.RootElement.GetProperty("VehicleName").GetString());
            Assert.Equal(7, document.RootElement.GetProperty("ExpectedCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(logsDirectory))
            {
                Directory.Delete(logsDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Writes_unverified_terminal_event_with_null_expected_count_and_reason()
    {
        var logsDirectory = Path.Combine(Path.GetTempPath(), "MineRailMonitorTests", Guid.NewGuid().ToString("N"));
        var occurredAt = new DateTimeOffset(2026, 10, 3, 10, 0, 30, TimeSpan.FromHours(8));
        var passageId = Guid.NewGuid();

        try
        {
            new PassageEventJsonlLogger(logsDirectory).Write(new PassageEvent
            {
                PassageId = passageId,
                EventType = PassageEventTypes.Unverified,
                OccurredAt = occurredAt,
                StationId = "RFID-560-01",
                DetectedCount = 3,
                Reason = "车辆未匹配，且未配置完整性判断回退节数"
            });

            var path = Path.Combine(logsDirectory, "Passage", "passage-event-20261003.jsonl");
            using var document = JsonDocument.Parse(Assert.Single(File.ReadAllLines(path)));
            Assert.Equal(passageId, document.RootElement.GetProperty("PassageId").GetGuid());
            Assert.Equal("PASSAGE_UNVERIFIED", document.RootElement.GetProperty("EventType").GetString());
            Assert.Equal("RFID-560-01", document.RootElement.GetProperty("StationId").GetString());
            Assert.True(document.RootElement.GetProperty("ExpectedCount").ValueKind == JsonValueKind.Null);
            Assert.Equal(3, document.RootElement.GetProperty("DetectedCount").GetInt32());
            Assert.Equal("车辆未匹配，且未配置完整性判断回退节数", document.RootElement.GetProperty("Reason").GetString());
        }
        finally
        {
            if (Directory.Exists(logsDirectory))
            {
                Directory.Delete(logsDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Runtime_writes_passage_lifecycle_events_through_yard_manager()
    {
        var logsDirectory = Path.Combine(Path.GetTempPath(), "MineRailMonitorTests", Guid.NewGuid().ToString("N"));
        var start = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(8));
        var station = new RfidStationConfig
        {
            StationId = "RFID-01",
            Name = "测试基站",
            YardId = "560",
            IpAddress = IPAddress.Loopback.ToString(),
            Port = 62001,
            ProtocolAddress = 1,
            Enabled = true
        };
        var vehicle = new VehicleConfig
        {
            VehicleNo = "K001",
            HeadRfid = 1,
            VehicleName = "1号矿车",
            Tags = new List<VehicleTagConfig>
            {
                new() { Position = 0, Rfid = 1, Type = VehicleTagType.Head },
                new() { Position = 1, Rfid = 17, Type = VehicleTagType.Carriage },
                new() { Position = 2, Rfid = 18, Type = VehicleTagType.Carriage }
            }
        };

        try
        {
            var logger = new PassageEventJsonlLogger(logsDirectory);
            using var manager = new YardCommunicationManager(
                new[]
                {
                    new YardCommunicationConfig
                    {
                        YardId = "560",
                        ListenIp = IPAddress.Loopback.ToString(),
                        ListenPort = GetUnusedPort(),
                        Enabled = true
                    }
                },
                new[] { station },
                new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
                new InMemoryPassageRecordStore(),
                vehicleConfigurations: new[] { vehicle },
                passageEventLogger: logger);
            var context = manager.GetContext("560")!;

            context.ProcessFrame(CreateFrame(station, start, 1, 17));
            context.ProcessFrame(CreateFrame(station, start.AddSeconds(2), 1, 17, 18));
            context.RuntimeCoordinator!.MarkCommandSent(station, RfidPollCommand.Clear, start.AddSeconds(3));
            context.ProcessFrame(CreateFrame(station, start.AddSeconds(4)));
            context.ProcessFrame(CreateFrame(station, start.AddSeconds(5)));

            var path = Path.Combine(logsDirectory, "Passage", "passage-event-20261002.jsonl");
            var lines = File.ReadAllLines(path);
            var parsedEvents = lines.Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Equal(
                    new[] { "PASSAGE_START", "PASSAGE_PROGRESS", "PASSAGE_PROGRESS", "PASSAGE_COMPLETE", "PASSAGE_CLEAR" },
                    parsedEvents.Select(document => document.RootElement.GetProperty("EventType").GetString()));
                var passageId = parsedEvents[0].RootElement.GetProperty("PassageId").GetGuid();
                Assert.All(parsedEvents, document => Assert.Equal(
                    passageId,
                    document.RootElement.GetProperty("PassageId").GetGuid()));
                Assert.Equal("K001", parsedEvents[0].RootElement.GetProperty("VehicleNo").GetString());
                Assert.Equal("1号矿车", parsedEvents[0].RootElement.GetProperty("VehicleName").GetString());
                Assert.All(parsedEvents, document =>
                    Assert.Equal("K001", document.RootElement.GetProperty("VehicleNo").GetString()));
                Assert.Equal(3, parsedEvents[3].RootElement.GetProperty("ExpectedCount").GetInt32());
                Assert.Equal(3, parsedEvents[3].RootElement.GetProperty("DetectedCount").GetInt32());
            }
            finally
            {
                foreach (var document in parsedEvents)
                {
                    document.Dispose();
                }
            }
        }
        finally
        {
            if (Directory.Exists(logsDirectory))
            {
                Directory.Delete(logsDirectory, recursive: true);
            }
        }
    }

    private static RfidStationFrame CreateFrame(RfidStationConfig station, DateTimeOffset at, params ushort[] rfids)
    {
        var slots = new ushort[14];
        rfids.CopyTo(slots, 0);
        return new RfidStationFrame
        {
            StationAddress = station.ProtocolAddress,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, station.Port),
            ReceivedAt = at,
            RawRfidSlots = slots,
            HeadRfid = slots[0],
            ValidRfids = slots.Where(value => value != 0).ToArray(),
            ActualNonZeroSlotCount = slots.Count(value => value != 0),
            ReportedCardCount = (byte)slots.Count(value => value != 0)
        };
    }

    private static int GetUnusedPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
