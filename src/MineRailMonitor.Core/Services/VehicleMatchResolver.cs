using System.Net;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

/// <summary>
/// Resolves the configured vehicle and the expected number of vehicle sections
/// for an RFID frame without changing the active passage recognition flow.
/// </summary>
public sealed class VehicleMatchResolver
{
    private readonly ProjectConfig _project;
    private readonly VehicleConfigurationService _vehicleConfigurationService;

    public VehicleMatchResolver(ProjectConfig project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _vehicleConfigurationService = new VehicleConfigurationService(project);
    }

    public VehicleMatchResult Resolve(RfidStationFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));

        var headRfid = GetHeadRfid(frame);
        var vehicle = headRfid.HasValue
            ? _vehicleConfigurationService.FindByHeadRfid(headRfid.Value)
            : null;

        return CreateResult(headRfid, vehicle, FindStation(frame));
    }

    public VehicleMatchResult Resolve(RfidStationFrame frame, StationRuntimeState state)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (state is null) throw new ArgumentNullException(nameof(state));

        var headRfid = GetHeadRfid(frame);
        var vehicle = headRfid.HasValue
            ? _vehicleConfigurationService.FindByHeadRfid(headRfid.Value)
            : null;

        var station = (_project.RfidStations ?? Array.Empty<RfidStationConfig>())
            .Where(item => item is not null && item.Enabled && item.ProtocolAddress == state.StationAddress)
            .FirstOrDefault(item => string.Equals(
                item.StationId,
                state.StationId,
                StringComparison.OrdinalIgnoreCase));
        station ??= FindStation(frame);

        return CreateResult(headRfid, vehicle, station);
    }

    private VehicleMatchResult CreateResult(ushort? headRfid, VehicleConfig? vehicle, RfidStationConfig? station)
    {
        if (vehicle is not null)
        {
            return new VehicleMatchResult
            {
                IsMatched = true,
                HeadRfid = headRfid,
                VehicleNo = vehicle.VehicleNo,
                VehicleName = vehicle.VehicleName,
                ExpectedVehicleCount = vehicle.TotalSectionCount,
                Source = VehicleMatchSource.VehicleConfiguration
            };
        }

        if (station?.ExpectedVehicleCount is int stationExpectedCount)
        {
            return new VehicleMatchResult
            {
                IsMatched = false,
                HeadRfid = headRfid,
                ExpectedVehicleCount = stationExpectedCount,
                Source = VehicleMatchSource.StationConfiguration
            };
        }

        var globalExpectedCount = _project.RfidSettings.ExpectedVehicleCount;
        return new VehicleMatchResult
        {
            IsMatched = false,
            HeadRfid = headRfid,
            ExpectedVehicleCount = globalExpectedCount,
            Source = globalExpectedCount.HasValue
                ? VehicleMatchSource.GlobalConfiguration
                : VehicleMatchSource.Unconfigured
        };
    }

    private static ushort? GetHeadRfid(RfidStationFrame frame)
    {
        if (frame.RawRfidSlots is null || frame.RawRfidSlots.Count == 0)
        {
            return null;
        }

        var rfid = frame.RawRfidSlots[0];
        return rfid == 0 ? null : rfid;
    }

    private RfidStationConfig? FindStation(RfidStationFrame frame)
    {
        var candidates = (_project.RfidStations ?? Array.Empty<RfidStationConfig>())
            .Where(station => station is not null &&
                              station.Enabled &&
                              station.ProtocolAddress == frame.StationAddress)
            .ToArray();

        if (IsUsableEndpoint(frame.SourceEndpoint))
        {
            var endpointMatches = candidates
                .Where(station => station.TryResolveEndpoint(out var endpoint) &&
                                  endpoint.Equals(frame.SourceEndpoint))
                .ToArray();
            if (endpointMatches.Length == 1)
            {
                return endpointMatches[0];
            }

            if (endpointMatches.Length > 1 ||
                candidates.Length != 1 ||
                candidates.Any(station => station.TryResolveEndpoint(out _)))
            {
                return null;
            }

            return candidates[0];
        }

        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool IsUsableEndpoint(IPEndPoint? endpoint) =>
        endpoint is not null &&
        endpoint.Port is >= 1 and <= 65535 &&
        !IPAddress.None.Equals(endpoint.Address) &&
        !IPAddress.Any.Equals(endpoint.Address) &&
        !IPAddress.IPv6Any.Equals(endpoint.Address);
}
