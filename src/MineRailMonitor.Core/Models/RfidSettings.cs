namespace MineRailMonitor.Core.Models;

public sealed class RfidSettings
{
    public int PollIntervalMs { get; set; } = 200;

    public int? ExpectedVehicleCount { get; set; }

    public int InterVehicleTimeoutSeconds { get; set; } = 30;

    public ushort EmptyRfidValue { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (PollIntervalMs <= 0)
        {
            errors.Add("RFID轮询间隔必须大于0。");
        }
        if (ExpectedVehicleCount is < 1 or > 14)
        {
            errors.Add("全局回退节数必须在1到14之间；留空表示不启用全局回退。");
        }
        if (InterVehicleTimeoutSeconds <= 0)
        {
            errors.Add("相邻车辆识别超时必须大于0。");
        }
        return errors;
    }
}
