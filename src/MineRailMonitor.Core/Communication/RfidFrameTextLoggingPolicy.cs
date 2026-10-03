namespace MineRailMonitor.Core.Communication;

public static class RfidFrameTextLoggingPolicy
{
    public static bool ShouldWriteVerboseFrameLogs(
        bool acceptanceMode,
        bool debugBuild,
        bool explicitlyEnabled) =>
        acceptanceMode || debugBuild || explicitlyEnabled;
}
