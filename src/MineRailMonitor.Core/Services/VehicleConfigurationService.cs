using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public sealed class VehicleConfigurationService
{
    private readonly ProjectConfig _project;

    public VehicleConfigurationService(ProjectConfig project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public VehicleConfig? FindByHeadRfid(ushort rfid)
    {
        if (rfid == 0)
        {
            return null;
        }

        return (_project.Vehicles ?? Array.Empty<VehicleConfig>())
            .FirstOrDefault(vehicle => vehicle is not null && vehicle.Enabled && vehicle.HeadRfid == rfid);
    }
}
