using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public interface IYardRfidStationResolver
{
    YardRfidStationScope Resolve(string yardId);

    YardRfidStationScope ResolveAll();

    YardRfidStationScope ResolveUnbound();
}
