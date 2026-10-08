using System.Net;
using MineRailMonitor.Core.Acceptance;
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
    public void Canonical_scenario_list_contains_existing_scenarios_and_U09_to_U13()
    {
        Assert.Equal(new[]
        {
            "Normal11", "Uncoupling10", "TwoConsecutiveTrains", "TwoStationsConcurrent",
            "ClearReappearingTags", "SparseSlots", "MultipleHeads", "NoHead",
            "VehicleNormal7", "VehicleUncoupling7Of6", "VehicleUnconfiguredFallback",
            "VehicleLateSeventh7", "VehicleRepeatedTagReads", "VehicleHeadAppearsLate7",
            "UdpU01Normal11", "UdpU02Uncoupling10Of11", "UdpU03LateEleventhCompletes",
            "UdpU04RepeatedTagsTimeout", "UdpU05HeadAppearsLate", "UdpU06UnknownHeadFallback",
            "UdpU07ConsecutiveAfterClear", "UdpU08ImmediateConsecutive",
            "U09_11CarMissingFive", "U10_11CarLateCompletion", "U11_CustomCountNotFixed11",
            "U12_Test11Normal", "U13_Test11MissingOne", "U14_UnverifiedNoFallback"
        }, AcceptanceScenario.All.Select(item => item.Name));
    }

    [Fact]
    public void U14_uses_an_unmatched_head_and_distinct_tags_to_reach_unverified_without_fallback()
    {
        var train = AcceptanceScenario.Parse("U14_UnverifiedNoFallback").Trains.Single();

        Assert.Equal(new ushort[] { 0xC001, 0xC101, 0xC102, 0xC103 }, train.VehicleRfids);
        Assert.Equal(4, train.OccupiedSlotIndexes.Count);
    }

    [Fact]
    public void Udp_acceptance_scenarios_use_the_confirmed_eleven_tag_inputs()
    {
        var normal = AcceptanceScenario.Parse("UdpU01Normal11").Trains.Single();
        var uncoupling = AcceptanceScenario.Parse("UdpU02Uncoupling10Of11").Trains.Single();
        var lateTag = AcceptanceScenario.Parse("UdpU03LateEleventhCompletes").Trains.Single();
        var repeated = AcceptanceScenario.Parse("UdpU04RepeatedTagsTimeout").Trains.Single();
        var lateHead = AcceptanceScenario.Parse("UdpU05HeadAppearsLate").Trains.Single();
        var unknownHead = AcceptanceScenario.Parse("UdpU06UnknownHeadFallback").Trains.Single();

        Assert.Equal(11, normal.VehicleRfids.Count);
        Assert.Equal(10, uncoupling.VehicleRfids.Count);
        Assert.Equal(11, lateTag.VehicleRfids.Count);
        Assert.Equal(TimeSpan.FromSeconds(27), lateTag.DelayBeforeFinalRfid);
        Assert.Equal(10, repeated.VehicleRfids.Count);
        Assert.Equal(11, lateHead.VehicleRfids.Count);
        Assert.Equal(11, unknownHead.VehicleRfids.Count);
        Assert.Equal(0, lateHead.OccupiedSlotIndexes[lateHead.OccupiedSlotIndexes.Count - 1]);
        Assert.Equal(1, lateHead.OccupiedSlotIndexes[0]);
        Assert.Equal((ushort)0x2001, unknownHead.VehicleRfids[0]);

        var immediateConsecutive = AcceptanceScenario.Parse("UdpU08ImmediateConsecutive");
        Assert.True(immediateConsecutive.SendNextTrainImmediatelyAfterClear);
        Assert.Equal(2, immediateConsecutive.Trains.Count);
    }

    [Fact]
    public void Simulator_supplement_preset_waits_twenty_five_seconds_before_the_seventh_tag()
    {
        using var station = new SimulatorStationContext(new SimulatorStationConfig
        {
            StationName = "验收模拟基站",
            ListenIp = "127.0.0.1",
            ListenPort = 62101,
            ProtocolAddress = 1
        });
        station.LoadScenario(
            "11节补扫完成",
            AcceptanceVehicleCatalog.Test11Rfids,
            delayBeforeRfidIndex: 6,
            delayBeforeRfid: TimeSpan.FromSeconds(25));

        Assert.True(station.StartScenario(TimeSpan.FromMilliseconds(450)));
        DateTimeOffset sixthTagAt = default;
        while (station.Playback.CurrentIndex < 6)
        {
            sixthTagAt = station.NextScenarioStepAt!.Value;
            Assert.True(station.TickScenario(sixthTagAt));
        }

        Assert.Equal(6, station.Playback.CurrentIndex);
        Assert.Equal(TimeSpan.FromSeconds(25), station.NextScenarioStepAt!.Value - sixthTagAt);
    }

    [Fact]
    public void Vehicle_scenario_inputs_use_a_known_seven_tag_train_and_an_unknown_head()
    {
        var normal = AcceptanceScenario.Parse("VehicleNormal7").Trains.Single();
        var uncoupling = AcceptanceScenario.Parse("VehicleUncoupling7Of6").Trains.Single();
        var unconfigured = AcceptanceScenario.Parse("VehicleUnconfiguredFallback").Trains.Single();

        Assert.Equal(new ushort[] { 1, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16 }, normal.VehicleRfids);
        Assert.Equal(new ushort[] { 1, 0x11, 0x12, 0x13, 0x14, 0x15 }, uncoupling.VehicleRfids);
        Assert.Equal(new ushort[] { 2, 0x21, 0x22, 0x23, 0x24, 0x25 }, unconfigured.VehicleRfids);
    }

    [Fact]
    public void Vehicle_integrity_scenarios_cover_late_tag_duplicate_reads_and_late_head_slot()
    {
        var lateTag = AcceptanceScenario.Parse("VehicleLateSeventh7").Trains.Single();
        var repeatedReads = AcceptanceScenario.Parse("VehicleRepeatedTagReads").Trains.Single();
        var lateHead = AcceptanceScenario.Parse("VehicleHeadAppearsLate7").Trains.Single();

        Assert.Equal(7, lateTag.VehicleRfids.Count);
        Assert.Single(repeatedReads.VehicleRfids);
        Assert.Equal(lateHead.VehicleRfids.Count, lateHead.OccupiedSlotIndexes.Count);
        Assert.Equal((ushort)0x1001, lateHead.VehicleRfids[6]);
        Assert.Equal(0, lateHead.OccupiedSlotIndexes[6]);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 0 }, lateHead.OccupiedSlotIndexes);
    }

    [Fact]
    public void U09_to_U13_use_fixed_acceptance_vehicle_RFID_sequences()
    {
        var missingFive = AcceptanceScenario.Parse("U09_11CarMissingFive").Trains.Single();
        var lateCompletion = AcceptanceScenario.Parse("U10_11CarLateCompletion").Trains.Single();
        var customCount = AcceptanceScenario.Parse("U11_CustomCountNotFixed11").Trains.Single();
        var normalEleven = AcceptanceScenario.Parse("U12_Test11Normal").Trains.Single();
        var missingOne = AcceptanceScenario.Parse("U13_Test11MissingOne").Trains.Single();

        var test11 = new ushort[] { 0xA001, 0xA002, 0xA003, 0xA004, 0xA005, 0xA006, 0xA007, 0xA008, 0xA009, 0xA010, 0xA011 };
        var test07 = new ushort[] { 0xB001, 0xB002, 0xB003, 0xB004, 0xB005, 0xB006, 0xB007 };
        Assert.Equal(test11.Take(6), missingFive.VehicleRfids);
        Assert.Equal(test11, lateCompletion.VehicleRfids);
        Assert.Equal(6, lateCompletion.DelayBeforeRfidIndex);
        Assert.Equal(TimeSpan.FromSeconds(25), lateCompletion.DelayBeforeRfid);
        Assert.Equal(test07, customCount.VehicleRfids);
        Assert.Equal(test11, normalEleven.VehicleRfids);
        Assert.Equal(test11.Take(10), missingOne.VehicleRfids);
        Assert.Equal("11节缺5节", AcceptanceScenario.Parse("U09_11CarMissingFive").DisplayName);
        Assert.Equal("11节补扫完成", AcceptanceScenario.Parse("U10_11CarLateCompletion").DisplayName);
        Assert.Equal("7节正常通过", AcceptanceScenario.Parse("U11_CustomCountNotFixed11").DisplayName);
        Assert.Equal("11节正常通过", AcceptanceScenario.Parse("U12_Test11Normal").DisplayName);
        Assert.Equal("11节缺1节", AcceptanceScenario.Parse("U13_Test11MissingOne").DisplayName);

        var acceptanceVehicles = AcceptanceVehicleCatalog.CreateVehicles();
        Assert.Equal(new[] { "TEST-11", "TEST-07" }, acceptanceVehicles.Select(vehicle => vehicle.VehicleNo));
        Assert.Equal(test11, acceptanceVehicles[0].Tags.Select(tag => tag.Rfid));
        Assert.Equal(test07, acceptanceVehicles[1].Tags.Select(tag => tag.Rfid));
    }

    [Fact]
    public void Scenario_data_contains_the_expected_head_and_sparse_slot_cases()
    {
        var multipleHeads = AcceptanceScenario.All.Single(item => item.Name == "MultipleHeads");
        var noHead = AcceptanceScenario.All.Single(item => item.Name == "NoHead");
        var sparse = AcceptanceScenario.All.Single(item => item.Name == "SparseSlots");

        Assert.Contains((ushort)0x03E9, multipleHeads.Trains.Single().VehicleRfids);
        Assert.Contains((ushort)0x03EA, multipleHeads.Trains.Single().VehicleRfids);
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
