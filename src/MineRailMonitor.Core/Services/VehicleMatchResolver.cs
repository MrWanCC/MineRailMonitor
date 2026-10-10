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

        return CreateResult(headRfid, vehicle);
    }

    public VehicleMatchResult Resolve(RfidStationFrame frame, StationRuntimeState state)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (state is null) throw new ArgumentNullException(nameof(state));

        var headRfid = GetHeadRfid(frame);
        var vehicle = headRfid.HasValue
            ? _vehicleConfigurationService.FindByHeadRfid(headRfid.Value)
            : null;

        return CreateResult(headRfid, vehicle);
    }

    private VehicleMatchResult CreateResult(ushort? headRfid, VehicleConfig? vehicle)
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

}
