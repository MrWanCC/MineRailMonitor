using MineRailMonitor.Core.Communication;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidFrameTextLoggingPolicyTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    public void Verbose_frame_text_logging_requires_diagnostic_mode(
        bool acceptanceMode,
        bool debugBuild,
        bool explicitlyEnabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            RfidFrameTextLoggingPolicy.ShouldWriteVerboseFrameLogs(
                acceptanceMode,
                debugBuild,
                explicitlyEnabled));
    }
}
