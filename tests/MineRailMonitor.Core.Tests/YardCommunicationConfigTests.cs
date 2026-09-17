using MineRailMonitor.Core.Models;

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
}
