using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Simulator.Acceptance;
using MineRailMonitor.Simulator.Models;
using MineRailMonitor.Simulator.Protocol;

namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorScenarioRunnerTests
{
    [Fact]
    public void Simulator_acceptance_options_force_loopback_and_reject_production_ports()
    {
        var options = SimulatorCommandLineOptions.Parse(new[]
        {
            "simulator.exe", "--test-mode", "--scenario", "Normal11", "--port", "62101",
            "--result", "artifacts/acceptance/test/simulator-result.json",
            "--ready-file", "artifacts/acceptance/test/simulator-ready.json"
        });

        Assert.True(options.TestMode);
        Assert.Equal(IPAddress.Loopback, options.ListenAddress);
        Assert.Equal(62111, options.Port620);
        Assert.Throws<ArgumentException>(() => SimulatorCommandLineOptions.Parse(new[]
        {
            "simulator.exe", "--test-mode", "--scenario", "Normal11", "--port", "62001",
            "--result", "artifacts/acceptance/test/result.json",
            "--ready-file", "artifacts/acceptance/test/ready.json"
        }));
    }

    [Fact]
    public void Simulator_acceptance_options_support_a_second_yard_listener()
    {
        var options = SimulatorCommandLineOptions.Parse(new[]
        {
            "simulator.exe", "--test-mode", "--scenario", "Normal11", "--port", "63101",
            "--port-620", "63111",
            "--result", "artifacts/acceptance/test/result.json",
            "--ready-file", "artifacts/acceptance/test/ready.json"
        });

        Assert.Equal(63101, options.Port);
        Assert.Equal(63111, options.Port620);
    }

    [Fact]
    public void Canonical_scenario_list_contains_eight_names()
    {
        Assert.Equal(new[]
        {
            "Normal11", "Uncoupling10", "TwoConsecutiveTrains", "TwoStationsConcurrent",
            "ClearReappearingTags", "SparseSlots", "MultipleHeads", "NoHead"
        }, AcceptanceScenario.All.Select(item => item.Name));
    }

    [Fact]
    public void Scenario_data_contains_the_expected_head_and_sparse_slot_cases()
    {
        var multipleHeads = AcceptanceScenario.All.Single(item => item.Name == "MultipleHeads");
        var noHead = AcceptanceScenario.All.Single(item => item.Name == "NoHead");
        var sparse = AcceptanceScenario.All.Single(item => item.Name == "SparseSlots");

        Assert.Contains((ushort)0x0001, multipleHeads.Trains.Single().VehicleRfids);
        Assert.Contains((ushort)0x0003, multipleHeads.Trains.Single().VehicleRfids);
        Assert.All(noHead.Trains.Single().VehicleRfids, value => Assert.True(value > 0x000A));
        Assert.Equal(new[] { 2, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 }, sparse.Trains.Single().OccupiedSlotIndexes);
    }

    [Fact]
    public void Responder_logs_clear_and_returns_a_real_40_byte_frame_after_clearing_slots()
    {
        var station = new SimulatorStation { Address = 0x01, Slots = new ushort[14] };
        station.Slots[2] = 0x001D;
        var responder = new RfidSimulatorResponder(new[] { station }, 0);

        var response = responder.CreateResponse(BuildRequest(0x01, RfidPollCommand.Clear));

        Assert.NotNull(response);
        Assert.Equal(40, response!.Length);
        Assert.Equal(1, responder.RequestLogs.Count(item => item.Command == RfidPollCommand.Clear));
        Assert.All(station.Slots, value => Assert.Equal((ushort)0, value));
        Assert.Equal(0, response[7]);
        Assert.Equal(40, responder.ResponseLogs.Single().FrameLength);
    }

    [Fact]
    public void Responder_reports_byte7_from_all_fourteen_physical_slots()
    {
        var station = new SimulatorStation { Address = 0x01, Slots = new ushort[14] };
        station.Slots[2] = 0x001D;
        station.Slots[13] = 0x0021;
        var responder = new RfidSimulatorResponder(new[] { station }, 0);

        var response = responder.CreateResponse(BuildRequest(0x01, RfidPollCommand.Read));

        Assert.NotNull(response);
        Assert.Equal(2, response![7]);
        Assert.Equal(new ushort[] { 0x001D, 0x0021 }, responder.ResponseLogs.Single().Slots.Where(value => value != 0));
    }

    private static byte[] BuildRequest(byte address, RfidPollCommand command)
    {
        return RfidRequestFrameBuilder.Build(new RfidStationConfig
        {
            Address = address,
            Mode = 0x04,
            CommandBytes = new byte[4],
            RequestPayload = new byte[28],
            DestinationEndpoint = new IPEndPoint(IPAddress.Loopback, 62101)
        }, command);
    }
}
