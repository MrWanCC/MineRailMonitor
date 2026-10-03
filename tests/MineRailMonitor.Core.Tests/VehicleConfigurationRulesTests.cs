using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class VehicleConfigurationRulesTests
{
    [Fact]
    public void Valid_configuration_counts_head_as_a_section()
    {
        var vehicle = CreateVehicle(1, 17, 18);

        Assert.Empty(VehicleConfigurationRules.Validate(new[] { vehicle }));
        Assert.Equal(3, vehicle.TotalSectionCount);
    }

    [Fact]
    public void Requires_vehicle_number_and_rejects_duplicate_numbers_case_insensitively()
    {
        var missingNumber = CreateVehicle(1);
        missingNumber.VehicleNo = " ";
        var first = CreateVehicle(2);
        first.VehicleNo = "K001";
        var duplicate = CreateVehicle(3);
        duplicate.VehicleNo = "k001";

        var errors = VehicleConfigurationRules.Validate(new[] { missingNumber, first, duplicate });

        Assert.Contains(errors, error => error.Contains("车辆编号") && error.Contains("必填"));
        Assert.Contains(errors, error => error.Contains("车辆编号重复"));
    }

    [Fact]
    public void Allows_vehicle_names_to_repeat_when_vehicle_numbers_are_unique()
    {
        var first = CreateVehicle(1);
        first.VehicleNo = "K001";
        first.VehicleName = "1号矿车";
        var second = CreateVehicle(2);
        second.VehicleNo = "K002";
        second.VehicleName = "1号矿车";

        Assert.Empty(VehicleConfigurationRules.Validate(new[] { first, second }));
    }

    [Fact]
    public void Rejects_rfid_bound_more_than_once_across_vehicles()
    {
        var first = CreateVehicle(1, 17);
        var second = CreateVehicle(2, 17);

        Assert.Contains("重复", string.Join(Environment.NewLine, VehicleConfigurationRules.Validate(new[] { first, second })));
    }

    [Fact]
    public void Rejects_duplicate_positions()
    {
        var vehicle = CreateVehicle(1, 17);
        vehicle.Tags.Add(new VehicleTagConfig { Position = 1, Rfid = 18, Type = VehicleTagType.Carriage });

        Assert.Contains("Position", string.Join(Environment.NewLine, VehicleConfigurationRules.Validate(new[] { vehicle })));
    }

    [Fact]
    public void Requires_position_zero_and_head_type()
    {
        var noHead = new VehicleConfig
        {
            HeadRfid = 17,
            Tags = new List<VehicleTagConfig>
            {
                new() { Position = 1, Rfid = 17, Type = VehicleTagType.Carriage }
            }
        };
        var wrongHeadType = CreateVehicle(1);
        wrongHeadType.Tags[0].Type = VehicleTagType.Carriage;

        Assert.NotEmpty(VehicleConfigurationRules.Validate(new[] { noHead }));
        Assert.NotEmpty(VehicleConfigurationRules.Validate(new[] { wrongHeadType }));
    }

    [Fact]
    public void Rejects_zero_rfid_and_positions_outside_protocol_slots()
    {
        var zeroRfid = CreateVehicle(1);
        zeroRfid.Tags[0].Rfid = 0;
        var invalidPosition = CreateVehicle(1);
        invalidPosition.Tags.Add(new VehicleTagConfig { Position = 14, Rfid = 17, Type = VehicleTagType.Carriage });

        Assert.NotEmpty(VehicleConfigurationRules.Validate(new[] { zeroRfid }));
        Assert.NotEmpty(VehicleConfigurationRules.Validate(new[] { invalidPosition }));
    }

    [Fact]
    public void Requires_head_rfid_to_match_position_zero_and_carriages_to_use_carriage_type()
    {
        var mismatchedHead = CreateVehicle(2);
        mismatchedHead.HeadRfid = 1;
        var wrongCarriageType = CreateVehicle(1, 17);
        wrongCarriageType.Tags[1].Type = VehicleTagType.Head;

        Assert.NotEmpty(VehicleConfigurationRules.Validate(new[] { mismatchedHead }));
        Assert.NotEmpty(VehicleConfigurationRules.Validate(new[] { wrongCarriageType }));
    }

    [Fact]
    public void Rejects_more_than_fourteen_slots()
    {
        var vehicle = CreateVehicle(1);
        for (var position = 1; position <= 14; position++)
        {
            vehicle.Tags.Add(new VehicleTagConfig
            {
                Position = position,
                Rfid = (ushort)(100 + position),
                Type = VehicleTagType.Carriage
            });
        }

        Assert.NotEmpty(VehicleConfigurationRules.Validate(new[] { vehicle }));
    }

    private static VehicleConfig CreateVehicle(ushort headRfid, params ushort[] carriageRfids)
    {
        var tags = new List<VehicleTagConfig>
        {
            new() { Position = 0, Rfid = headRfid, Type = VehicleTagType.Head }
        };
        for (var index = 0; index < carriageRfids.Length; index++)
        {
            tags.Add(new VehicleTagConfig
            {
                Position = index + 1,
                Rfid = carriageRfids[index],
                Type = VehicleTagType.Carriage
            });
        }

        return new VehicleConfig { VehicleNo = $"K{headRfid:X4}", HeadRfid = headRfid, Tags = tags };
    }
}
