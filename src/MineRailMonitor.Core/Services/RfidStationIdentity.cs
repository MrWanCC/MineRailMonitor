using System.Globalization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public static class RfidStationIdentity
{
    public static string CreateScopedId(string yardId, int localNumber)
    {
        if (string.IsNullOrWhiteSpace(yardId))
        {
            throw new ArgumentException("站场编号不能为空。", nameof(yardId));
        }

        if (localNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(localNumber), localNumber, "站场内编号必须大于 0。");
        }

        return $"RFID-{NormalizeYardKey(yardId)}-{localNumber.ToString("00", CultureInfo.InvariantCulture)}";
    }

    public static string GetDisplayId(string? stationId, string? yardId)
    {
        var normalizedStationId = stationId?.Trim() ?? string.Empty;
        if (TryGetLocalNumber(normalizedStationId, yardId, out var localNumber))
        {
            return CreateScopedId(yardId!, localNumber);
        }

        return !string.IsNullOrWhiteSpace(yardId) &&
               TryGetLegacyLocalNumber(normalizedStationId, out localNumber)
            ? CreateScopedId(yardId!, localNumber)
            : normalizedStationId;
    }

    public static bool TryGetLocalNumber(string stationId, string? yardId, out int localNumber)
    {
        localNumber = 0;
        if (string.IsNullOrWhiteSpace(stationId) || string.IsNullOrWhiteSpace(yardId))
        {
            return false;
        }

        var prefix = $"RFID-{NormalizeYardKey(yardId!)}-";
        var normalizedStationId = stationId.Trim();
        if (!normalizedStationId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = normalizedStationId.Substring(prefix.Length);
        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out localNumber)
            && localNumber > 0;
    }

    public static IReadOnlyDictionary<string, string> BuildLegacyMigrationMap(
        IEnumerable<RfidStationConfig> stations)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));

        var migration = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var station in stations.Where(item => item is not null))
        {
            if (string.IsNullOrWhiteSpace(station.YardId) ||
                !TryGetLocalNumber(station.StationId, station.YardId, out var localNumber) ||
                localNumber < 1)
            {
                continue;
            }

            var legacyId = $"RFID-{localNumber.ToString("00", CultureInfo.InvariantCulture)}";
            if (string.Equals(legacyId, station.StationId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddMigrationAlias(migration, legacyId, station.StationId);

            // The previous Default project assigned 07-12 to the six 620
            // stations. Keep those records attached when 620 is renumbered
            // independently from 01.
            if (string.Equals(NormalizeYardKey(station.YardId!), "620", StringComparison.OrdinalIgnoreCase) &&
                localNumber <= 6)
            {
                var previousNumber = localNumber + 6;
                AddMigrationAlias(
                    migration,
                    $"RFID-{previousNumber.ToString("00", CultureInfo.InvariantCulture)}",
                    station.StationId);
                AddMigrationAlias(
                    migration,
                    CreateScopedId(station.YardId!, previousNumber),
                    station.StationId);
            }
        }

        return migration;
    }

    private static void AddMigrationAlias(
        IDictionary<string, string> migration,
        string alias,
        string target)
    {
        if (!migration.ContainsKey(alias))
        {
            migration[alias] = target.Trim();
        }
    }

    private static string NormalizeYardKey(string yardId)
    {
        var trimmed = yardId.Trim();
        var buffer = new char[trimmed.Length];
        var length = 0;
        foreach (var character in trimmed)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToUpperInvariant(character);
            }
        }

        if (length == 0)
        {
            throw new ArgumentException("站场编号必须包含字母或数字。", nameof(yardId));
        }

        return new string(buffer, 0, length);
    }

    private static bool TryGetLegacyLocalNumber(string stationId, out int localNumber)
    {
        localNumber = 0;
        const string prefix = "RFID-";
        if (!stationId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(
                   stationId.Substring(prefix.Length),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out localNumber)
            && localNumber > 0;
    }
}
