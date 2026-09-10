namespace MineRailMonitor.Core.Models;

public sealed class DashboardSnapshot
{
    public DashboardSnapshot(
        IReadOnlyList<DashboardTrain> trains,
        IReadOnlyList<DashboardEvent> events,
        DashboardTrain? selectedTrain)
    {
        Trains = trains;
        Events = events;
        SelectedTrain = selectedTrain;
    }

    public IReadOnlyList<DashboardTrain> Trains { get; }

    public IReadOnlyList<DashboardEvent> Events { get; }

    public DashboardTrain? SelectedTrain { get; }
}

public sealed class DashboardTrain
{
    public DashboardTrain(int sequence, string trainNumber, string currentDevice, string direction, int speed, int carriageCount, string status, string lastTime)
    {
        Sequence = sequence;
        TrainNumber = trainNumber;
        CurrentDevice = currentDevice;
        Direction = direction;
        Speed = speed;
        CarriageCount = carriageCount;
        Status = status;
        LastTime = lastTime;
    }

    public int Sequence { get; }
    public string TrainNumber { get; }
    public string CurrentDevice { get; }
    public string Direction { get; }
    public int Speed { get; }
    public int CarriageCount { get; }
    public string Status { get; }
    public string LastTime { get; }
}

public sealed class DashboardEvent
{
    public DashboardEvent(string time, string trainNumber, string type, string message, string status)
    {
        Time = time;
        TrainNumber = trainNumber;
        Type = type;
        Message = message;
        Status = status;
    }

    public string Time { get; }
    public string TrainNumber { get; }
    public string Type { get; }
    public string Message { get; }
    public string Status { get; }
}

public static class DashboardSampleData
{
    public static DashboardSnapshot CreateForStation(string stationId)
    {
        if (!string.Equals(stationId, "560", StringComparison.OrdinalIgnoreCase))
        {
            return new DashboardSnapshot(Array.Empty<DashboardTrain>(), Array.Empty<DashboardEvent>(), null);
        }

        var trains = new[]
        {
            new DashboardTrain(1, "T001", "-560水平  上部股道", "→", 12, 10, "运行正常", "2024-12-18  14:26:12"),
            new DashboardTrain(2, "T002", "-560水平  中部股道", "→", 8, 10, "识别中", "2024-12-18  14:25:17"),
            new DashboardTrain(3, "T003", "-560水平  下部股道", "→", 0, 10, "报警停车", "2024-12-18  14:24:03")
        };
        var events = new[]
        {
            new DashboardEvent("14:24:03", "T003", "报警", "列车在-560水平下部股道停车", "未处理"),
            new DashboardEvent("14:21:19", "T002", "通过点", "列车通过Y6-11", "已处理"),
            new DashboardEvent("14:18:07", "T001", "通过点", "列车通过Y6-10", "已处理"),
            new DashboardEvent("14:15:32", "系统", "通信恢复", "下位机通信已恢复", "已处理"),
            new DashboardEvent("14:12:11", "系统", "通信中断", "-620水平采集设备通信中断", "已处理")
        };

        return new DashboardSnapshot(trains, events, trains[2]);
    }
}
