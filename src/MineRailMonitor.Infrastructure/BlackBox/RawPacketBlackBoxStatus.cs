namespace MineRailMonitor.Infrastructure.BlackBox;

public sealed class RawPacketBlackBoxStatus
{
    internal RawPacketBlackBoxStatus(
        bool isRunning,
        long writtenCount,
        long droppedCount,
        string? lastError,
        string rootDirectory)
    {
        IsRunning = isRunning;
        WrittenCount = writtenCount;
        DroppedCount = droppedCount;
        LastError = lastError;
        RootDirectory = rootDirectory;
    }

    public bool IsRunning { get; }

    public long WrittenCount { get; }

    public long DroppedCount { get; }

    public string? LastError { get; }

    public string RootDirectory { get; }
}
