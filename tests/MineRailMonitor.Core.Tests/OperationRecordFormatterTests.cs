using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class OperationRecordFormatterTests
{
    [Fact]
    public void Vehicle_add_creates_a_success_record_with_vehicle_identity_and_tag_count()
    {
        var records = ConfigurationOperationRecordFormatter.GetVehicleChanges(
            Array.Empty<VehicleConfig>(),
            new[] { CreateVehicle("K001", 1, "1号矿车", 3) });

        var record = Assert.Single(records);
        Assert.Equal("新增车辆配置", record.OperationType);
        Assert.Equal("车辆", record.TargetType);
        Assert.Equal("K001", record.TargetName);
        Assert.Equal("成功", record.Result);
        Assert.Contains("车头RFID: 01", record.AfterValue, StringComparison.Ordinal);
        Assert.Contains("总节数: 3节", record.AfterValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Vehicle_edit_records_before_and_after_values()
    {
        var records = ConfigurationOperationRecordFormatter.GetVehicleChanges(
            new[] { CreateVehicle("K001", 1, "1号矿车", 6) },
            new[] { CreateVehicle("K001", 1, "1号矿车", 7) });

        var record = Assert.Single(records);
        Assert.Equal("修改车辆配置", record.OperationType);
        Assert.Contains("总节数: 6节", record.BeforeValue, StringComparison.Ordinal);
        Assert.Contains("总节数: 7节", record.AfterValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Vehicle_number_change_is_recorded_as_an_edit_when_head_rfid_is_unchanged()
    {
        var records = ConfigurationOperationRecordFormatter.GetVehicleChanges(
            new[] { CreateVehicle("K001", 1, "1号矿车", 7) },
            new[] { CreateVehicle("K002", 1, "1号矿车", 7) });

        var record = Assert.Single(records);
        Assert.Equal("修改车辆配置", record.OperationType);
        Assert.Equal("K002", record.TargetName);
        Assert.Equal("车辆编号: K001", record.BeforeValue);
        Assert.Equal("车辆编号: K002", record.AfterValue);
    }

    [Fact]
    public void Vehicle_delete_records_the_removed_configuration()
    {
        var records = ConfigurationOperationRecordFormatter.GetVehicleChanges(
            new[] { CreateVehicle("K001", 1, "1号矿车", 7) },
            Array.Empty<VehicleConfig>());

        var record = Assert.Single(records);
        Assert.Equal("删除车辆配置", record.OperationType);
        Assert.Equal("K001", record.TargetName);
        Assert.Contains("总节数: 7节", record.BeforeValue, StringComparison.Ordinal);
        Assert.True(string.IsNullOrEmpty(record.AfterValue));
    }

    [Fact]
    public void Rfid_settings_record_contains_only_changed_fields()
    {
        var records = ConfigurationOperationRecordFormatter.GetRfidSettingsChanges(
            new RfidSettings { PollIntervalMs = 200, ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            new RfidSettings { PollIntervalMs = 300, ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 });

        var record = Assert.Single(records);
        Assert.Equal("修改RFID参数", record.OperationType);
        Assert.Equal("轮询周期: 200ms", record.BeforeValue);
        Assert.Equal("轮询周期: 300ms", record.AfterValue);
    }

    [Fact]
    public void Station_edit_records_only_changed_key_fields()
    {
        var before = CreateStation("RFID-560-01", "560-01", "192.168.1.100", 62001);
        var after = CreateStation("RFID-560-01", "560-01", "192.168.1.101", 62001);

        var record = Assert.Single(ConfigurationOperationRecordFormatter.GetStationChanges(
            new[] { before }, new[] { after }));

        Assert.Equal("修改基站配置", record.OperationType);
        Assert.Equal("基站", record.TargetType);
        Assert.Equal("560-01", record.TargetName);
        Assert.Equal("IP地址: 192.168.1.100", record.BeforeValue);
        Assert.Equal("IP地址: 192.168.1.101", record.AfterValue);
    }

    [Fact]
    public void Yard_communication_edit_records_only_changed_fields()
    {
        var before = new YardCommunicationConfig
        {
            YardId = "560",
            ListenIp = "127.0.0.1",
            ListenPort = 62001,
            ExternalAlarmListenIp = "127.0.0.1",
            ExternalAlarmListenPort = 62002,
            Enabled = true
        };
        var after = new YardCommunicationConfig
        {
            YardId = "560",
            ListenIp = "127.0.0.1",
            ListenPort = 62001,
            ExternalAlarmListenIp = "127.0.0.1",
            ExternalAlarmListenPort = 62003,
            Enabled = true
        };

        var record = Assert.Single(ConfigurationOperationRecordFormatter.GetYardCommunicationChanges(
            new[] { before }, new[] { after }));

        Assert.Equal("COMMUNICATION_CONFIG_CHANGED", record.OperationType);
        Assert.Equal("站场通信配置", record.TargetType);
        Assert.Equal("560", record.TargetName);
        Assert.Equal("外部报警监听端口: 62002", record.BeforeValue);
        Assert.Equal("外部报警监听端口: 62003", record.AfterValue);
    }

    [Fact]
    public void Yard_alarm_forward_add_is_recorded_as_communication_configuration_change()
    {
        var record = Assert.Single(ConfigurationOperationRecordFormatter.GetYardAlarmForwardChanges(
            Array.Empty<YardAlarmForwardConfig>(),
            new[]
            {
                new YardAlarmForwardConfig
                {
                    YardId = "620",
                    Enabled = true,
                    TargetIp = "127.0.0.1",
                    TargetPort = 63001
                }
            }));

        Assert.Equal("COMMUNICATION_CONFIG_CHANGED", record.OperationType);
        Assert.Equal("外部报警转发配置", record.TargetType);
        Assert.Equal("620", record.TargetName);
        Assert.Contains("目标IP: 127.0.0.1", record.AfterValue, StringComparison.Ordinal);
        Assert.Contains("目标端口: 63001", record.AfterValue, StringComparison.Ordinal);
    }

    private static VehicleConfig CreateVehicle(string vehicleNo, ushort headRfid, string name, int totalCount)
    {
        var tags = new List<VehicleTagConfig>
        {
            new() { Position = 0, Rfid = headRfid, Type = VehicleTagType.Head }
        };
        for (var position = 1; position < totalCount; position++)
        {
            tags.Add(new VehicleTagConfig
            {
                Position = position,
                Rfid = (ushort)(headRfid + position),
                Type = VehicleTagType.Carriage
            });
        }

        return new VehicleConfig
        {
            VehicleNo = vehicleNo,
            HeadRfid = headRfid,
            VehicleName = name,
            Tags = tags
        };
    }

    private static RfidStationConfig CreateStation(string id, string name, string ipAddress, int port) => new()
    {
        StationId = id,
        Name = name,
        IpAddress = ipAddress,
        Port = port,
        ProtocolAddress = 1,
        Enabled = true
    };
}
