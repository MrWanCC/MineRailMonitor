using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidStationConfigurationChangeRulesTests
{
    [Fact]
    public void Display_name_only_changes_do_not_require_runtime_restart()
    {
        var previous = CreateStation("RFID-01", 62001, 1);
        var candidate = CreateStation("RFID-01", 62001, 1);
        candidate.Name = "新名称";

        Assert.False(RfidStationConfigurationChangeRules.RequiresRuntimeRestart(
            new[] { previous },
            new[] { candidate }));
    }

    [Fact]
    public void Communication_yard_changes_require_runtime_restart()
    {
        var previous = CreateStation("RFID-01", 62001, 1);
        var candidate = CreateStation("RFID-01", 62001, 1);
        candidate.YardId = "620";

        Assert.True(RfidStationConfigurationChangeRules.RequiresRuntimeRestart(
            new[] { previous },
            new[] { candidate }));
    }

    [Fact]
    public void Endpoint_changes_require_runtime_restart()
    {
        var previous = CreateStation("RFID-01", 62001, 1);
        var candidate = CreateStation("RFID-01", 62002, 1);

        Assert.True(RfidStationConfigurationChangeRules.RequiresRuntimeRestart(
            new[] { previous },
            new[] { candidate }));
    }

    [Fact]
    public void Protocol_and_enabled_changes_require_runtime_restart()
    {
        var previous = CreateStation("RFID-01", 62001, 1);
        var protocolCandidate = CreateStation("RFID-01", 62001, 2);
        var disabledCandidate = CreateStation("RFID-01", 62001, 1);
        disabledCandidate.Enabled = false;

        Assert.True(RfidStationConfigurationChangeRules.RequiresRuntimeRestart(
            new[] { previous },
            new[] { protocolCandidate }));
        Assert.True(RfidStationConfigurationChangeRules.RequiresRuntimeRestart(
            new[] { previous },
            new[] { disabledCandidate }));
    }

    [Fact]
    public void Adding_or_removing_a_station_requires_runtime_restart()
    {
        var first = CreateStation("RFID-01", 62001, 1);
        var second = CreateStation("RFID-02", 62002, 2);

        Assert.True(RfidStationConfigurationChangeRules.RequiresRuntimeRestart(
            new[] { first },
            new[] { first, second }));
        Assert.True(RfidStationConfigurationChangeRules.RequiresRuntimeRestart(
            new[] { first, second },
            new[] { first }));
    }

    private static RfidStationConfig CreateStation(string stationId, int port, byte protocolAddress) => new()
    {
        StationId = stationId,
        Name = stationId,
        IpAddress = "127.0.0.1",
        Port = port,
        ProtocolAddress = protocolAddress,
        Enabled = true,
        CommandBytes = new byte[4],
        RequestPayload = new byte[28]
    };
}
