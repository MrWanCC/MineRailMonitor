using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Communication;

namespace MineRailMonitor.Core.Tests;

public sealed class VehicleMatchDiagnosticFormatterTests
{
    [Fact]
    public void Formats_vehicle_match_with_head_tag_and_section_count()
    {
        var vehicle = new VehicleConfig
        {
            HeadRfid = 1,
            Name = "01号矿车",
            Tags = new List<VehicleTagConfig>
            {
                new() { Position = 0, Rfid = 1, Type = VehicleTagType.Head },
                new() { Position = 1, Rfid = 17, Type = VehicleTagType.Carriage },
                new() { Position = 2, Rfid = 18, Type = VehicleTagType.Carriage },
                new() { Position = 3, Rfid = 19, Type = VehicleTagType.Carriage }
            }
        };

        var result = Format(1, vehicle);

        Assert.Contains("[VehicleMatch]", result, StringComparison.Ordinal);
        Assert.Contains("车头RFID: 01", result, StringComparison.Ordinal);
        Assert.Contains("匹配车辆: 01号矿车", result, StringComparison.Ordinal);
        Assert.Contains("配置节数: 4", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Formats_unconfigured_head_tag()
    {
        var result = Format(1, null);

        Assert.Contains("未配置车辆: 车头RFID=01", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Formats_resolved_vehicle_match_result_and_source()
    {
        var result = VehicleMatchDiagnosticFormatter.Format(new VehicleMatchResult
        {
            IsMatched = true,
            HeadRfid = 1,
            VehicleName = "01号矿车",
            ExpectedVehicleCount = 4,
            Source = VehicleMatchSource.VehicleConfiguration
        });

        Assert.Contains("配置节数: 4", result, StringComparison.Ordinal);
        Assert.Contains("来源: 车辆配置", result, StringComparison.Ordinal);
    }

    private static string Format(ushort headRfid, VehicleConfig? vehicle) =>
        VehicleMatchDiagnosticFormatter.Format(headRfid, vehicle);
}
