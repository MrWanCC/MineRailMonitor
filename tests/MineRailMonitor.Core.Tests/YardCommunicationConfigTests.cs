using MineRailMonitor.Core.Models;
using System.Net;

namespace MineRailMonitor.Core.Tests;

public sealed class YardCommunicationConfigTests
{
    [Fact]
    public void Rejects_invalid_yard_listener_configuration()
    {
        var config = new YardCommunicationConfig
        {
            YardId = "560",
            ListenIp = "not-an-ip",
            ListenPort = 70000,
            Enabled = true
        };

        var errors = config.Validate();

        Assert.Contains(errors, error => error.IndexOf("IP地址无效", StringComparison.Ordinal) >= 0);
        Assert.Contains(errors, error => error.IndexOf("端口无效", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Resolves_external_alarm_endpoint_from_independent_ip()
    {
        var config = new YardCommunicationConfig
        {
            YardId = "620",
            ListenIp = IPAddress.Loopback.ToString(),
            ListenPort = 62003,
            ExternalAlarmListenIp = "192.168.16.118",
            ExternalAlarmListenPort = 58112,
            Enabled = true
        };

        Assert.True(config.TryResolveExternalAlarmEndpoint(out var endpoint));
        Assert.Equal(IPAddress.Parse("192.168.16.118"), endpoint.Address);
        Assert.Equal(58112, endpoint.Port);
    }

    [Fact]
    public void Rejects_enabled_external_alarm_without_a_fixed_local_port()
    {
        var config = new YardCommunicationConfig
        {
            YardId = "560",
            ListenIp = IPAddress.Loopback.ToString(),
            ListenPort = 62002,
            ExternalAlarmListenIp = IPAddress.Loopback.ToString(),
            ExternalAlarmListenPort = 0,
            Enabled = true
        };

        var errors = config.ValidateExternalAlarm(enabled: true);

        Assert.Contains(errors, error => error.IndexOf("本机固定收发端口", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Rejects_external_alarm_port_equal_to_rfid_listener_port()
    {
        var config = new YardCommunicationConfig
        {
            YardId = "560",
            ListenIp = IPAddress.Loopback.ToString(),
            ListenPort = 58112,
            ExternalAlarmListenIp = IPAddress.Loopback.ToString(),
            ExternalAlarmListenPort = 58112,
            Enabled = true
        };

        var errors = config.Validate();

        Assert.Contains(errors, error => error.IndexOf("不能复用 RFID 监听端口", StringComparison.Ordinal) >= 0);
    }
}
