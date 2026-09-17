using System.Globalization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public sealed class RfidMapBindingResolution
{
    internal RfidMapBindingResolution(
        RfidMapBindingState state,
        RfidStationConfig? configuration,
        string effectiveName)
    {
        State = state;
        Configuration = configuration;
        EffectiveName = effectiveName;
    }

    public RfidMapBindingState State { get; }

    public RfidStationConfig? Configuration { get; }

    public string EffectiveName { get; }
}

public sealed class RfidLegacyBindingMigrationResult
{
    internal RfidLegacyBindingMigrationResult(int migratedCount, int ambiguousCount)
    {
        MigratedCount = migratedCount;
        AmbiguousCount = ambiguousCount;
    }

    public int MigratedCount { get; }

    public int AmbiguousCount { get; }
}

public static class RfidMapBindingResolver
{
    public static IReadOnlyList<string> FindRemovedReferencedStationIds(
        IEnumerable<StationConfig> stations,
        IEnumerable<RfidStationConfig> remainingConfigurations)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));
        if (remainingConfigurations is null) throw new ArgumentNullException(nameof(remainingConfigurations));

        var remainingIds = new HashSet<string>(
            remainingConfigurations
                .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.StationId))
                .Select(item => item.StationId.Trim()),
            StringComparer.OrdinalIgnoreCase);

        return stations
            .Where(station => station is not null)
            .SelectMany(station => station.Devices ?? Array.Empty<DeviceConfig>())
            .Where(device => device is not null &&
                            !string.IsNullOrWhiteSpace(device.RfidStationId) &&
                            !remainingIds.Contains(device.RfidStationId!.Trim()))
            .Select(device => device.RfidStationId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(stationId => stationId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static RfidMapBindingResolution Resolve(
        DeviceConfig device,
        IEnumerable<RfidStationConfig> configurations)
    {
        if (device is null) throw new ArgumentNullException(nameof(device));
        if (configurations is null) throw new ArgumentNullException(nameof(configurations));

        var effectiveName = string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name;
        var stationId = device.RfidStationId?.Trim();
        if (string.IsNullOrWhiteSpace(stationId))
        {
            return new RfidMapBindingResolution(RfidMapBindingState.Unbound, null, effectiveName);
        }

        var configuration = configurations.FirstOrDefault(item =>
            item is not null && string.Equals(item.StationId?.Trim(), stationId, StringComparison.OrdinalIgnoreCase));
        if (configuration is null)
        {
            return new RfidMapBindingResolution(RfidMapBindingState.MissingConfiguration, null, effectiveName);
        }

        if (!string.IsNullOrWhiteSpace(configuration.Name))
        {
            effectiveName = configuration.Name;
        }

        return new RfidMapBindingResolution(
            configuration.Enabled ? RfidMapBindingState.Offline : RfidMapBindingState.Disabled,
            configuration,
            effectiveName);
    }

    public static RfidLegacyBindingMigrationResult ApplyLegacyProtocolBindings(
        IEnumerable<StationConfig> stations,
        IEnumerable<RfidStationConfig> configurations)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));
        if (configurations is null) throw new ArgumentNullException(nameof(configurations));

        var configurationsByProtocol = configurations
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.StationId))
            .GroupBy(item => item.ProtocolAddress)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var migratedCount = 0;
        var ambiguousCount = 0;

        foreach (var station in stations)
        {
            if (station is null)
            {
                continue;
            }

            foreach (var device in station.Devices.Where(item => item is not null && item.Type == DeviceType.RfidStation))
            {
                if (!string.IsNullOrWhiteSpace(device.RfidStationId) ||
                    !TryParseProtocolAddress(device.ProtocolAddress, out var protocolAddress) ||
                    !configurationsByProtocol.TryGetValue(protocolAddress, out var matches))
                {
                    continue;
                }

                if (matches.Length == 1)
                {
                    device.RfidStationId = matches[0].StationId;
                    migratedCount++;
                }
                else if (matches.Length > 1)
                {
                    ambiguousCount++;
                }
            }
        }

        return new RfidLegacyBindingMigrationResult(migratedCount, ambiguousCount);
    }

    private static bool TryParseProtocolAddress(string? value, out byte address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value!.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(2);
        }

        return byte.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }
}
