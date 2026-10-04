namespace MineRailMonitor.Core.Models;

public sealed class PassagePersistenceFault
{
    private readonly byte[] _lastVehiclePacket;

    internal PassagePersistenceFault(
        PassageRecord record,
        int failureCount,
        Exception exception,
        DateTimeOffset failedAt,
        byte[]? lastVehiclePacket)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        FailureCount = failureCount;
        LastException = exception?.ToString() ?? throw new ArgumentNullException(nameof(exception));
        FailedAt = failedAt;
        LastAttemptAt = failedAt;
        _lastVehiclePacket = lastVehiclePacket is { Length: > 0 }
            ? (byte[])lastVehiclePacket.Clone()
            : Array.Empty<byte>();
    }

    public PassageRecord Record { get; }

    public int FailureCount { get; internal set; }

    public string LastException { get; internal set; }

    public DateTimeOffset FailedAt { get; }

    public DateTimeOffset LastAttemptAt { get; internal set; }

    public DateTimeOffset? ClearedAt { get; internal set; }

    public byte[] LastVehiclePacket => (byte[])_lastVehiclePacket.Clone();

    internal void RecordFailure(Exception exception, DateTimeOffset attemptedAt)
    {
        FailureCount++;
        LastException = exception.ToString();
        LastAttemptAt = attemptedAt;
    }

    public string ToDiagnosticMessage()
    {
        var lastExceptionMessage = LastException
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? LastException;
        return $"数据库保存失败；PassageId={Record.PassageId}；失败次数={FailureCount}；最后异常={lastExceptionMessage}；等待恢复";
    }
}
