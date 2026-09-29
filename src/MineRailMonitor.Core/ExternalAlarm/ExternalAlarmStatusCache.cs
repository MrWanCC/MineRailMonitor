namespace MineRailMonitor.Core.ExternalAlarm;

/// <summary>
/// Stores the latest external status snapshot for each station in one yard.
/// The owning coordinator provides the yard boundary.
/// </summary>
public sealed class ExternalAlarmStatusCache
{
    public static readonly TimeSpan DefaultNormalRetention = TimeSpan.FromMinutes(2);

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, SnapshotEntry> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);

    public void EnsureStation(
        string stationId,
        byte stationAddress,
        DateTimeOffset? updatedAt = null)
    {
        if (string.IsNullOrWhiteSpace(stationId))
        {
            throw new ArgumentException("外部报警状态缓存的基站编号不能为空。", nameof(stationId));
        }

        var key = stationId.Trim();
        lock (_syncRoot)
        {
            if (!_snapshots.ContainsKey(key))
            {
                _snapshots[key] = new SnapshotEntry(
                    new ExternalAlarmStationSnapshot(
                        stationAddress,
                        ExternalAlarmStatus.NoVehicle,
                        0),
                    updatedAt ?? DateTimeOffset.UtcNow);
            }
        }
    }

    public void Update(
        string stationId,
        ExternalAlarmStationSnapshot snapshot,
        DateTimeOffset? updatedAt = null)
    {
        if (string.IsNullOrWhiteSpace(stationId))
        {
            throw new ArgumentException("外部报警状态缓存的基站编号不能为空。", nameof(stationId));
        }

        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

        lock (_syncRoot)
        {
            var key = stationId.Trim();
            var effectiveUpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
            if (_snapshots.TryGetValue(key, out var previous) &&
                previous.Snapshot.StationAddress == snapshot.StationAddress &&
                previous.Snapshot.Status == snapshot.Status &&
                previous.Snapshot.AccumulatedVehicleCount == snapshot.AccumulatedVehicleCount)
            {
                return;
            }

            // A completed/cleared passage reports Normal first. Empty RFID
            // frames that follow must not replace that state before the
            // two-minute retention window expires.
            if (_snapshots.TryGetValue(key, out previous) &&
                previous.Snapshot.Status == ExternalAlarmStatus.Normal &&
                snapshot.Status == ExternalAlarmStatus.NoVehicle &&
                effectiveUpdatedAt - previous.UpdatedAt < DefaultNormalRetention)
            {
                return;
            }

            _snapshots[key] = new SnapshotEntry(snapshot, effectiveUpdatedAt);
        }
    }

    public bool TryGet(string stationId, out ExternalAlarmStationSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(stationId))
        {
            snapshot = null!;
            return false;
        }

        lock (_syncRoot)
        {
            if (_snapshots.TryGetValue(stationId.Trim(), out var entry))
            {
                snapshot = entry.Snapshot;
                return true;
            }

            snapshot = null!;
            return false;
        }
    }

    public bool TryGet(
        string stationId,
        out ExternalAlarmStationSnapshot snapshot,
        out DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(stationId))
        {
            snapshot = null!;
            updatedAt = default;
            return false;
        }

        lock (_syncRoot)
        {
            if (_snapshots.TryGetValue(stationId.Trim(), out var entry))
            {
                snapshot = entry.Snapshot;
                updatedAt = entry.UpdatedAt;
                return true;
            }

            snapshot = null!;
            updatedAt = default;
            return false;
        }
    }

    public ExternalAlarmStationSnapshot GetForSending(
        string stationId,
        DateTimeOffset now,
        TimeSpan normalRetention = default)
    {
        if (string.IsNullOrWhiteSpace(stationId))
        {
            throw new ArgumentException("外部报警状态缓存的基站编号不能为空。", nameof(stationId));
        }

        if (normalRetention == default)
        {
            normalRetention = DefaultNormalRetention;
        }

        lock (_syncRoot)
        {
            if (!_snapshots.TryGetValue(stationId.Trim(), out var entry))
            {
                throw new KeyNotFoundException($"未找到外部报警状态缓存：{stationId}。");
            }

            if (entry.Snapshot.Status == ExternalAlarmStatus.Normal &&
                now - entry.UpdatedAt >= normalRetention)
            {
                entry = new SnapshotEntry(
                    new ExternalAlarmStationSnapshot(
                        entry.Snapshot.StationAddress,
                        ExternalAlarmStatus.NoVehicle,
                        0),
                    now);
                _snapshots[stationId.Trim()] = entry;
            }

            return entry.Snapshot;
        }
    }

    public IReadOnlyList<ExternalAlarmStationSnapshot> GetAll()
    {
        lock (_syncRoot)
        {
            return _snapshots.Values.Select(entry => entry.Snapshot).ToArray();
        }
    }

    private sealed class SnapshotEntry
    {
        public SnapshotEntry(ExternalAlarmStationSnapshot snapshot, DateTimeOffset updatedAt)
        {
            Snapshot = snapshot;
            UpdatedAt = updatedAt;
        }

        public ExternalAlarmStationSnapshot Snapshot { get; }

        public DateTimeOffset UpdatedAt { get; }
    }
}
