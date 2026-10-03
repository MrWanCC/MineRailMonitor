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
                        0,
                        key),
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
            var stationSnapshot = string.Equals(
                snapshot.StationId,
                key,
                StringComparison.OrdinalIgnoreCase)
                ? snapshot
                : new ExternalAlarmStationSnapshot(
                    snapshot.StationAddress,
                    snapshot.Status,
                    snapshot.AccumulatedVehicleCount,
                    key,
                    snapshot.AlarmLatched,
                    snapshot.AlarmTime);
            var incoming = NormalizeIncomingSnapshot(stationSnapshot, effectiveUpdatedAt);
            if (_snapshots.TryGetValue(key, out var existing) &&
                existing.Snapshot.AlarmLatched &&
                incoming.Status == ExternalAlarmStatus.UncouplingAlarm &&
                existing.Snapshot.AlarmTime.HasValue)
            {
                incoming = new ExternalAlarmStationSnapshot(
                    incoming.StationAddress,
                    incoming.Status,
                    incoming.AccumulatedVehicleCount,
                    incoming.StationId,
                    alarmLatched: true,
                    alarmTime: existing.Snapshot.AlarmTime);
            }

            if (_snapshots.TryGetValue(key, out var previous) &&
                previous.IgnoreAlarmUntilNonAlarm &&
                incoming.Status == ExternalAlarmStatus.UncouplingAlarm)
            {
                return;
            }

            var clearAlarmTransition = _snapshots.TryGetValue(key, out previous) &&
                previous.IgnoreAlarmUntilNonAlarm &&
                incoming.Status != ExternalAlarmStatus.UncouplingAlarm;
            if (_snapshots.TryGetValue(key, out previous) &&
                previous.Snapshot.StationAddress == incoming.StationAddress &&
                previous.Snapshot.Status == incoming.Status &&
                previous.Snapshot.AccumulatedVehicleCount == incoming.AccumulatedVehicleCount &&
                previous.Snapshot.AlarmLatched == incoming.AlarmLatched)
            {
                if (clearAlarmTransition)
                {
                    _snapshots[key] = new SnapshotEntry(incoming, previous.UpdatedAt);
                }

                return;
            }

            if (_snapshots.TryGetValue(key, out previous) &&
                previous.Snapshot.AlarmLatched &&
                incoming.Status != ExternalAlarmStatus.UncouplingAlarm)
            {
                return;
            }

            // A completed/cleared passage reports Normal first. Empty RFID
            // frames that follow must not replace that state before the
            // two-minute retention window expires.
            if (_snapshots.TryGetValue(key, out previous) &&
                previous.Snapshot.Status == ExternalAlarmStatus.Normal &&
                incoming.Status == ExternalAlarmStatus.NoVehicle &&
                effectiveUpdatedAt - previous.UpdatedAt < DefaultNormalRetention)
            {
                if (clearAlarmTransition)
                {
                    _snapshots[key] = new SnapshotEntry(
                        previous.Snapshot,
                        previous.UpdatedAt);
                }

                return;
            }

            _snapshots[key] = new SnapshotEntry(incoming, effectiveUpdatedAt);
        }
    }

    public bool TryReleaseAlarm(
        byte stationAddress,
        DateTimeOffset releasedAt,
        out ExternalAlarmStationSnapshot snapshot)
    {
        lock (_syncRoot)
        {
            var matches = _snapshots
                .Where(pair => pair.Value.Snapshot.StationAddress == stationAddress)
                .Where(pair => pair.Value.Snapshot.AlarmLatched)
                .ToArray();
            if (matches.Length != 1)
            {
                snapshot = null!;
                return false;
            }

            var current = matches[0].Value.Snapshot;
            snapshot = new ExternalAlarmStationSnapshot(
                current.StationAddress,
                ExternalAlarmStatus.Normal,
                current.AccumulatedVehicleCount,
                current.StationId);
            _snapshots[matches[0].Key] = new SnapshotEntry(
                snapshot,
                releasedAt,
                ignoreAlarmUntilNonAlarm: true);
            return true;
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
                !entry.Snapshot.AlarmLatched &&
                now - entry.UpdatedAt >= normalRetention)
            {
                entry = new SnapshotEntry(
                    new ExternalAlarmStationSnapshot(
                        entry.Snapshot.StationAddress,
                        ExternalAlarmStatus.NoVehicle,
                        0,
                        entry.Snapshot.StationId),
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
        public SnapshotEntry(
            ExternalAlarmStationSnapshot snapshot,
            DateTimeOffset updatedAt,
            bool ignoreAlarmUntilNonAlarm = false)
        {
            Snapshot = snapshot;
            UpdatedAt = updatedAt;
            IgnoreAlarmUntilNonAlarm = ignoreAlarmUntilNonAlarm;
        }

        public ExternalAlarmStationSnapshot Snapshot { get; }

        public DateTimeOffset UpdatedAt { get; }

        public bool IgnoreAlarmUntilNonAlarm { get; }
    }

    private static ExternalAlarmStationSnapshot NormalizeIncomingSnapshot(
        ExternalAlarmStationSnapshot snapshot,
        DateTimeOffset updatedAt)
    {
        if (snapshot.Status != ExternalAlarmStatus.UncouplingAlarm ||
            (snapshot.AlarmLatched && snapshot.AlarmTime.HasValue))
        {
            return snapshot;
        }

        return new ExternalAlarmStationSnapshot(
            snapshot.StationAddress,
            snapshot.Status,
            snapshot.AccumulatedVehicleCount,
            snapshot.StationId,
            alarmLatched: true,
            alarmTime: updatedAt);
    }
}
