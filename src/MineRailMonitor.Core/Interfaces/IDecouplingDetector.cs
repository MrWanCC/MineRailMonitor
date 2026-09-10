using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Interfaces;

public interface IDecouplingDetector
{
    DeviceStatus Evaluate(TrainSession session, DateTimeOffset now);
}
