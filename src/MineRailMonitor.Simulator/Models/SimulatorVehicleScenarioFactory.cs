using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Simulator.Models;

public static class SimulatorVehicleScenarioFactory
{
    public static SimulatorVehicleScenario CreateNormal(VehicleConfig vehicle)
    {
        var rfids = GetOrderedRfids(vehicle);
        return new SimulatorVehicleScenario($"{GetVehicleName(vehicle)} · {rfids.Length}节正常通过", rfids);
    }

    public static SimulatorVehicleScenario CreateMissingFive(VehicleConfig vehicle)
    {
        var rfids = GetOrderedRfids(vehicle);
        if (rfids.Length <= 5)
        {
            throw new InvalidOperationException("该车辆不足6个标签，不能构造缺5节场景。");
        }

        return new SimulatorVehicleScenario(
            $"{GetVehicleName(vehicle)} · {rfids.Length}节缺5节",
            rfids.Take(rfids.Length - 5).ToArray());
    }

    public static SimulatorVehicleScenario CreateMissingOne(VehicleConfig vehicle)
    {
        var rfids = GetOrderedRfids(vehicle);
        if (rfids.Length <= 1)
        {
            throw new InvalidOperationException("该车辆没有可省略的车节标签。");
        }

        return new SimulatorVehicleScenario(
            $"{GetVehicleName(vehicle)} · {rfids.Length}节缺1节",
            rfids.Take(rfids.Length - 1).ToArray());
    }

    public static SimulatorVehicleScenario CreateLateCompletion(VehicleConfig vehicle)
    {
        var rfids = GetOrderedRfids(vehicle);
        if (rfids.Length <= 1)
        {
            throw new InvalidOperationException("该车辆没有可延迟补扫的车节标签。");
        }

        return new SimulatorVehicleScenario(
            $"{GetVehicleName(vehicle)} · {rfids.Length}节补扫完成",
            rfids,
            Math.Min(6, rfids.Length - 1),
            TimeSpan.FromSeconds(25));
    }

    public static SimulatorVehicleScenario CreateUnconfiguredVehicle(
        IEnumerable<VehicleConfig> configuredVehicles,
        int expectedCount)
    {
        if (configuredVehicles is null)
        {
            throw new ArgumentNullException(nameof(configuredVehicles));
        }
        if (expectedCount is < 1 or > 14)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedCount), "模拟节数必须在1到14之间。");
        }

        var configuredRfids = configuredVehicles
            .Where(vehicle => vehicle is not null)
            .SelectMany(vehicle => vehicle.Tags ?? new List<VehicleTagConfig>())
            .Select(tag => tag.Rfid)
            .ToHashSet();
        var sequence = new List<ushort>(expectedCount);
        for (var candidate = (int)ushort.MaxValue; candidate > 0 && sequence.Count < expectedCount; candidate--)
        {
            var rfid = (ushort)candidate;
            if (!configuredRfids.Contains(rfid))
            {
                sequence.Add(rfid);
            }
        }

        if (sequence.Count != expectedCount)
        {
            throw new InvalidOperationException("无法生成不与项目车辆重复的未配置车辆标签。");
        }

        return new SimulatorVehicleScenario($"未配置车辆 · {expectedCount}节", sequence);
    }

    public static ushort[] GetOrderedRfids(VehicleConfig vehicle)
    {
        if (vehicle is null)
        {
            throw new ArgumentNullException(nameof(vehicle));
        }

        var rfids = (vehicle.Tags ?? new List<VehicleTagConfig>())
            .OrderBy(tag => tag.Position)
            .Select(tag => tag.Rfid)
            .ToArray();
        if (rfids.Length == 0)
        {
            throw new InvalidOperationException("所选车辆没有配置RFID标签。");
        }

        return rfids;
    }

    public static string GetVehicleName(VehicleConfig vehicle)
    {
        if (vehicle is null)
        {
            throw new ArgumentNullException(nameof(vehicle));
        }

        if (!string.IsNullOrWhiteSpace(vehicle.VehicleName))
        {
            return vehicle.VehicleName;
        }
        if (!string.IsNullOrWhiteSpace(vehicle.VehicleNo))
        {
            return vehicle.VehicleNo;
        }

        return $"RFID-{vehicle.HeadRfid:X4}";
    }
}
