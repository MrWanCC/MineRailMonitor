using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Interfaces;

public interface IPassageEventLogger
{
    void Write(PassageEvent passageEvent);
}
