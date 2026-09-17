using System.Linq;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

/// <summary>
/// Identifies changes that require rebuilding the communication/runtime
/// pipeline. Display name is metadata-only; communication yard ownership is
/// part of the pipeline membership and therefore requires a restart.
/// </summary>
public static class RfidStationConfigurationChangeRules
{
    public static bool RequiresRuntimeRestart(
        IEnumerable<RfidStationConfig> previous,
        IEnumerable<RfidStationConfig> candidate)
    {
        if (previous is null) throw new ArgumentNullException(nameof(previous));
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));

        var previousList = previous.Where(item => item is not null).ToArray();
        var candidateList = candidate.Where(item => item is not null).ToArray();
        var previousById = previousList
            .GroupBy(item => item.StationId?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var candidateById = candidateList
            .GroupBy(item => item.StationId?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (previousById.Count != previousList.Length ||
            candidateById.Count != candidateList.Length ||
            previousById.Count != candidateById.Count ||
            previousById.Keys.Any(id => !candidateById.ContainsKey(id)))
        {
            return true;
        }

        foreach (var pair in previousById)
        {
            if (!AreCommunicationSettingsEqual(pair.Value, candidateById[pair.Key]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreCommunicationSettingsEqual(
        RfidStationConfig previous,
        RfidStationConfig candidate) =>
        previous.TryResolveEndpoint(out var previousEndpoint) ==
            candidate.TryResolveEndpoint(out var candidateEndpoint) &&
        (!previous.TryResolveEndpoint(out _) || previousEndpoint.Equals(candidateEndpoint)) &&
        previous.ProtocolAddress == candidate.ProtocolAddress &&
        string.Equals(
            previous.YardId?.Trim(),
            candidate.YardId?.Trim(),
            StringComparison.OrdinalIgnoreCase) &&
        previous.Enabled == candidate.Enabled &&
        previous.Mode == candidate.Mode &&
        previous.CrcHigh == candidate.CrcHigh &&
        previous.CrcLow == candidate.CrcLow &&
        previous.CommandBytes.SequenceEqual(candidate.CommandBytes) &&
        previous.RequestPayload.SequenceEqual(candidate.RequestPayload);
}
