using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public sealed class YardRfidStationBindingDiagnostic
{
    internal YardRfidStationBindingDiagnostic(
        string yardId,
        string rfidStationId,
        string? deviceId,
        string message)
    {
        YardId = yardId;
        RfidStationId = rfidStationId;
        DeviceId = deviceId;
        Message = message;
    }

    public string YardId { get; }

    public string RfidStationId { get; }

    public string? DeviceId { get; }

    public string Message { get; }
}

public sealed class YardRfidStationScope
{
    internal YardRfidStationScope(
        string? yardId,
        bool isGlobal,
        bool isUnbound,
        IReadOnlyList<string> rfidStationIds,
        IReadOnlyList<RfidStationConfig> rfidStations,
        IReadOnlyList<RfidStationConfig> disabledStations,
        IReadOnlyList<RfidStationConfig> unmappedStations,
        IReadOnlyList<YardRfidStationBindingDiagnostic> missingConfigurations,
        IReadOnlyList<YardRfidStationBindingDiagnostic> duplicateBindings)
    {
        YardId = yardId;
        IsGlobal = isGlobal;
        IsUnbound = isUnbound;
        RfidStationIds = rfidStationIds;
        RfidStations = rfidStations;
        DisabledStations = disabledStations;
        UnmappedStations = unmappedStations;
        MissingConfigurations = missingConfigurations;
        DuplicateBindings = duplicateBindings;
    }

    public string? YardId { get; }

    public bool IsGlobal { get; }

    public bool IsUnbound { get; }

    public IReadOnlyList<string> RfidStationIds { get; }

    public IReadOnlyList<RfidStationConfig> RfidStations { get; }

    public IReadOnlyList<RfidStationConfig> DisabledStations { get; }

    /// <summary>
    /// Stations assigned to this yard for communication but not yet placed on its map.
    /// </summary>
    public IReadOnlyList<RfidStationConfig> UnmappedStations { get; }

    public IReadOnlyList<YardRfidStationBindingDiagnostic> MissingConfigurations { get; }

    public IReadOnlyList<YardRfidStationBindingDiagnostic> DuplicateBindings { get; }
}
