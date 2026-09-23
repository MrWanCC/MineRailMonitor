using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class YardAlarmForwardConfigTests
{
    [Fact]
    public void Enabled_forward_requires_valid_ip_and_port()
    {
        var config = new YardAlarmForwardConfig
        {
            YardId = "560",
            Enabled = true,
            TargetIp = "not-an-ip",
            TargetPort = 65536
        };

        var errors = config.Validate();

        Assert.Contains(errors, error => error.IndexOf("目标IP地址无效", StringComparison.Ordinal) >= 0);
        Assert.Contains(errors, error => error.IndexOf("目标端口无效", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Disabled_forward_allows_empty_target_fields()
    {
        var config = new YardAlarmForwardConfig
        {
            YardId = "620",
            Enabled = false
        };

        Assert.Empty(config.Validate());
    }
}
