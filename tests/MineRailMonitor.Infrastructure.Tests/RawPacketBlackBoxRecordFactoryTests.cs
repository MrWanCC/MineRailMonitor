using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Infrastructure.BlackBox;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class RawPacketBlackBoxRecordFactoryTests
{
    [Fact]
    public void BlackBox_preserves_invalid_rx_frame()
    {
        using var directory = new TemporaryDirectory();
        var station = CreateStation("RFID-560-01", "560", 0x01, 65101);
        using var context = CreateContext("560", station);
        var receivedAt = new DateTimeOffset(2026, 9, 18, 10, 20, 30, TimeSpan.Zero);
        var datagram = new RfidUdpDatagramEventArgs(
            new byte[] { 0xB0, 0xB0 },
            new IPEndPoint(IPAddress.Loopback, station.Port),
            receivedAt);
        var record = RawPacketBlackBoxRecordFactory.FromReceived(context, datagram);

        Assert.Null(record.StationId);
        Assert.Null(record.ProtocolAddress);
        Assert.False(record.Valid);
        Assert.Equal("B0 B0", record.Hex);

        using var writer = new RawPacketBlackBoxWriter(directory.Path);
        Assert.True(writer.TryEnqueue(record));
        writer.Dispose();

        var line = File.ReadAllLines(Path.Combine(directory.Path, "2026-09-18", "560.jsonl")).Single();
        var json = JsonConvert.DeserializeObject<JObject>(line);
        Assert.NotNull(json);
        Assert.False(json!["valid"]!.Value<bool>());
        Assert.Equal("Length must be 40 bytes.", json["validationError"]!.Value<string>());
        Assert.Equal("B0 B0", json["hex"]!.Value<string>());
        Assert.True(json["stationId"] is JValue { Type: JTokenType.Null });
        Assert.True(json["protocolAddress"] is JValue { Type: JTokenType.Null });
    }

    [Fact]
    public void BlackBox_does_not_confuse_same_protocol_address_between_yards()
    {
        using var directory = new TemporaryDirectory();
        var station560 = CreateStation("RFID-560-01", "560", 0x01, 65111);
        var station620 = CreateStation("RFID-620-01", "620", 0x01, 65112);
        using var context560 = CreateContext("560", station560);
        using var context620 = CreateContext("620", station620);
        using var writer = new RawPacketBlackBoxWriter(directory.Path);
        var frame = CreateValidFrame(0x01);
        var receivedAt = new DateTimeOffset(2026, 9, 18, 10, 20, 30, TimeSpan.Zero);

        Assert.True(writer.TryEnqueue(RawPacketBlackBoxRecordFactory.FromReceived(
            context560,
            new RfidUdpDatagramEventArgs(
                frame,
                new IPEndPoint(IPAddress.Loopback, station560.Port),
                receivedAt))));
        Assert.True(writer.TryEnqueue(RawPacketBlackBoxRecordFactory.FromReceived(
            context620,
            new RfidUdpDatagramEventArgs(
                frame,
                new IPEndPoint(IPAddress.Loopback, station620.Port),
                receivedAt))));
        writer.Dispose();

        var json560 = ReadSingleJson(directory.Path, "560");
        var json620 = ReadSingleJson(directory.Path, "620");
        Assert.Equal("560", json560["yard"]!.Value<string>());
        Assert.Equal("RFID-560-01", json560["stationId"]!.Value<string>());
        Assert.Equal("620", json620["yard"]!.Value<string>());
        Assert.Equal("RFID-620-01", json620["stationId"]!.Value<string>());
    }

    private static JObject ReadSingleJson(string rootDirectory, string yardId)
    {
        var dateDirectory = Directory.GetDirectories(rootDirectory).Single();
        var line = File.ReadAllLines(Path.Combine(dateDirectory, yardId + ".jsonl")).Single();
        return JsonConvert.DeserializeObject<JObject>(line)!;
    }

    private static YardCommunicationContext CreateContext(string yardId, RfidStationConfig station) =>
        new(
            new YardCommunicationConfig
            {
                YardId = yardId,
                ListenIp = IPAddress.Loopback.ToString(),
                ListenPort = string.Equals(yardId, "560", StringComparison.OrdinalIgnoreCase) ? 65200 : 65201,
                Enabled = false
            },
            new[] { station },
            new RfidSettings
            {
                PollIntervalMs = 1000,
                ExpectedVehicleCount = 11,
                InterVehicleTimeoutSeconds = 30
            },
            new InMemoryPassageRecordStore());

    private static RfidStationConfig CreateStation(
        string stationId,
        string yardId,
        byte protocolAddress,
        int port) => new()
        {
            StationId = stationId,
            Name = stationId,
            YardId = yardId,
            IpAddress = IPAddress.Loopback.ToString(),
            Port = port,
            ProtocolAddress = protocolAddress,
            Enabled = true
        };

    private static byte[] CreateValidFrame(byte protocolAddress)
    {
        var frame = new byte[40];
        frame[0] = 0xB0;
        frame[1] = 0xB0;
        frame[2] = protocolAddress;
        frame[38] = 0xAA;
        frame[39] = 0xAA;
        return frame;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MineRailMonitor-BlackBox-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
