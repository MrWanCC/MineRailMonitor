using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public static class VehicleConfigurationRules
{
    public static IReadOnlyList<string> Validate(IEnumerable<VehicleConfig> vehicles)
    {
        if (vehicles is null) throw new ArgumentNullException(nameof(vehicles));

        var errors = new List<string>();
        var seenRfids = new HashSet<ushort>();
        var seenVehicleNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vehicleList = vehicles.ToArray();

        foreach (var vehicle in vehicleList)
        {
            if (vehicle is null)
            {
                errors.Add("车辆配置项不能为空。");
                continue;
            }

            var vehicleNo = vehicle.VehicleNo?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(vehicleNo))
            {
                errors.Add($"车辆 {vehicle.HeadRfid:X4} 的车辆编号必填。");
            }
            else if (!seenVehicleNos.Add(vehicleNo))
            {
                errors.Add($"车辆编号重复：{vehicleNo}。");
            }

            var headTags = new List<VehicleTagConfig>();
            var seenPositions = new HashSet<int>();
            var tags = vehicle.Tags ?? new List<VehicleTagConfig>();
            if (vehicle.Tags is null)
            {
                errors.Add($"车辆 {vehicle.HeadRfid:X4} 的标签列表不能为空。");
            }

            if (tags.Count > 14)
            {
                errors.Add($"车辆 {vehicle.HeadRfid:X4} 的总节数不能超过14节。");
            }

            foreach (var tag in tags)
            {
                if (tag is null)
                {
                    errors.Add($"车辆 {vehicle.HeadRfid:X4} 存在空标签配置。");
                    continue;
                }

                if (tag.Position is < 0 or > 13)
                {
                    errors.Add($"车辆 {vehicle.HeadRfid:X4} 的Position必须在0到13之间：{tag.Position}。");
                }

                if (!seenPositions.Add(tag.Position))
                {
                    errors.Add($"车辆 {vehicle.HeadRfid:X4} 的Position重复：{tag.Position}。");
                }

                if (tag.Rfid == 0)
                {
                    errors.Add($"车辆 {vehicle.HeadRfid:X4} 的RFID不能为0（Position {tag.Position}）。");
                }
                else if (!seenRfids.Add(tag.Rfid))
                {
                    errors.Add($"RFID重复绑定：{tag.Rfid:X4}。");
                }

                if (tag.Position == 0)
                {
                    headTags.Add(tag);
                    if (tag.Type != VehicleTagType.Head)
                    {
                        errors.Add($"车辆 {vehicle.HeadRfid:X4} 的Position 0类型必须是车头。");
                    }
                }
                else if (tag.Type != VehicleTagType.Carriage)
                {
                    errors.Add($"车辆 {vehicle.HeadRfid:X4} 的Position {tag.Position}类型必须是车节。");
                }
            }

            if (headTags.Count == 0)
            {
                errors.Add($"车辆 {vehicle.HeadRfid:X4} 必须配置Position 0车头标签。");
            }
            else if (headTags.Count == 1 && vehicle.HeadRfid != headTags[0].Rfid)
            {
                errors.Add($"车辆车头标签字段与Position 0 RFID不一致：{vehicle.HeadRfid:X4}。");
            }

            if (vehicle.HeadRfid == 0)
            {
                errors.Add("车辆车头RFID不能为0。");
            }
        }

        return errors;
    }
}
