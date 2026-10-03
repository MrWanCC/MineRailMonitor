using System.Net;
using System.Reflection;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class VehiclePassageIdentityTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Matched_vehicle_snapshot_is_saved_to_alarm_passage_and_displayed_with_counts()
    {
        var store = new InMemoryPassageRecordStore();
        var station = CreateStation();
        var vehicle = CreateVehicle(1, "01号矿车", 7);
        var coordinator = CreateCoordinator(station, store, new[] { vehicle });
        var firstFrame = CreateFrame(station, Start, 1, 0x11, 0x12, 0x13, 0x14, 0x15);

        var session = coordinator.ProcessFrame(firstFrame);
        coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(29.9), 1, 0x11, 0x12, 0x13, 0x14, 0x15));
        coordinator.Evaluate(Start.AddSeconds(30));

        Assert.NotNull(session);
        Assert.Equal(7, session!.ExpectedVehicleCount);
        var snapshot = GetProperty<object>(session, "VehicleMatchResult");
        Assert.NotNull(snapshot);
        Assert.True(GetProperty<bool>(snapshot!, "IsMatched"));
        Assert.Equal("K0001", GetProperty<string>(snapshot!, "VehicleNo"));
        Assert.Equal("01号矿车", GetProperty<string>(snapshot!, "VehicleName"));
        Assert.Equal((ushort)1, GetProperty<ushort?>(snapshot!, "HeadRfid"));
        Assert.Equal("01号矿车", GetProperty<string>(session, "VehicleName"));
        Assert.Equal("K0001", GetProperty<string>(session, "VehicleNo"));
        Assert.Equal((ushort)1, GetProperty<ushort?>(session, "VehicleHeadRfid"));

        var record = Assert.Single(store.Records);
        Assert.Equal(PassageOutcome.UncouplingAlarm, record.Outcome);
        Assert.Equal("脱节报警", record.AlarmMessage);
        Assert.Equal(7, record.ExpectedVehicleCount);
        Assert.Equal(6, record.DetectedVehicleCount);
        Assert.Equal("01号矿车", GetProperty<string>(record, "VehicleName"));
        Assert.Equal("K0001", GetProperty<string>(record, "VehicleNo"));
        Assert.Equal((ushort)1, GetProperty<ushort?>(record, "VehicleHeadRfid"));
        store.MarkAlarmAcknowledged(record.PassageId, Start.AddSeconds(31));
        var acknowledgedRecord = store.GetDetails(record.PassageId)!;
        Assert.Equal("01号矿车", GetProperty<string>(acknowledgedRecord, "VehicleName"));
        Assert.Equal("K0001", GetProperty<string>(acknowledgedRecord, "VehicleNo"));
        Assert.Equal((ushort)1, GetProperty<ushort?>(acknowledgedRecord, "VehicleHeadRfid"));
        Assert.Equal(
            "车辆：01号矿车，车头：01，实际：6/7节，缺少：1节",
            InvokeFormatter("FormatSummary", acknowledgedRecord));
        Assert.Equal(
            "车辆:\n01号矿车\n车头:\n01\n理论节数:\n7\n实际:\n6\n缺少:\n1节",
            InvokeFormatter("FormatDetails", acknowledgedRecord));
    }

    [Fact]
    public void Unmatched_vehicle_keeps_generic_alarm_and_null_vehicle_identity()
    {
        var store = new InMemoryPassageRecordStore();
        var station = CreateStation();
        var coordinator = CreateCoordinator(station, store, new[] { CreateVehicle(1, "01号矿车", 7) });

        coordinator.ProcessFrame(CreateFrame(station, Start, 2, 0x21, 0x22, 0x23, 0x24, 0x25));
        coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(29.9), 2, 0x21, 0x22, 0x23, 0x24, 0x25));
        coordinator.Evaluate(Start.AddSeconds(30));

        var record = Assert.Single(store.Records);
        Assert.Equal("脱节报警", record.AlarmMessage);
        Assert.Null(GetProperty<object?>(record, "VehicleName"));
        Assert.Null(GetProperty<ushort?>(record, "VehicleHeadRfid"));
        Assert.Equal("脱节报警", InvokeFormatter("FormatSummary", record));
        Assert.Equal("脱节报警", InvokeFormatter("FormatDetails", record));
    }

    [Fact]
    public void Passage_head_rfid_is_taken_from_position_zero_for_high_value_vehicle_tag()
    {
        var store = new InMemoryPassageRecordStore();
        var station = CreateStation();
        var coordinator = CreateCoordinator(station, store, new[] { CreateVehicle(0x1001, "高位车头", 7) });

        var firstFrame = CreateFrame(station, Start, 0x1001, 0x1101, 0x1102, 0x1103, 0x1104, 0x1105);
        firstFrame.HeadRfid = 0x0002;
        coordinator.ProcessFrame(firstFrame);
        coordinator.ProcessFrame(CreateFrame(station, Start.AddSeconds(29.9), 0x1001, 0x1101, 0x1102, 0x1103, 0x1104, 0x1105));
        coordinator.Evaluate(Start.AddSeconds(30));

        var state = Assert.Single(coordinator.States.Values);
        var record = Assert.Single(store.Records);
        Assert.Equal((ushort?)0x1001, state.CurrentHeadRfid);
        Assert.Equal((ushort?)0x1001, record.HeadRfid);
        Assert.Equal((ushort?)0x1001, record.VehicleHeadRfid);
        Assert.DoesNotContain(record.WarningMessages, message => message.Contains("车头槽位(Position 0)"));
    }

    private static RfidRuntimeCoordinator CreateCoordinator(
        RfidStationConfig station,
        InMemoryPassageRecordStore store,
        IReadOnlyList<VehicleConfig> vehicles) => new(
            new[] { station },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromSeconds(5), maxClearAttempts: 3, emptyConfirmationReads: 2),
            vehicles);

    private static RfidStationConfig CreateStation() => new()
    {
        StationId = "RFID-TEST-01",
        Name = "测试基站",
        IpAddress = "127.0.0.1",
        Port = 62001,
        ProtocolAddress = 0x01,
        ExpectedVehicleCount = 7,
        Enabled = true
    };

    private static VehicleConfig CreateVehicle(ushort headRfid, string name, int sectionCount) => new()
    {
        VehicleNo = $"K{headRfid:X4}",
        HeadRfid = headRfid,
        VehicleName = name,
        Enabled = true,
        Tags = Enumerable.Range(0, sectionCount)
            .Select(position => new VehicleTagConfig
            {
                Position = position,
                Rfid = position == 0 ? headRfid : (ushort)(0x10 + position),
                Type = position == 0 ? VehicleTagType.Head : VehicleTagType.Carriage
            })
            .ToList()
    };

    private static RfidStationFrame CreateFrame(RfidStationConfig station, DateTimeOffset at, params ushort[] rfids)
    {
        var slots = new ushort[14];
        rfids.CopyTo(slots, 0);
        station.TryResolveEndpoint(out var endpoint);
        return new RfidStationFrame
        {
            StationAddress = station.ProtocolAddress,
            ReceivedAt = at,
            RawRfidSlots = slots,
            HeadRfid = slots[0],
            ValidRfids = slots.Where(rfid => rfid != 0).ToArray(),
            ActualNonZeroSlotCount = slots.Count(rfid => rfid != 0),
            SourceEndpoint = endpoint
        };
    }

    private static T? GetProperty<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return (T?)property!.GetValue(target);
    }

    private static string InvokeFormatter(string methodName, PassageRecord record)
    {
        var formatter = typeof(PassageRecord).Assembly.GetType("MineRailMonitor.Core.Communication.PassageAlarmDisplayFormatter");
        Assert.NotNull(formatter);
        var method = formatter!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, new object[] { record }));
    }
}
