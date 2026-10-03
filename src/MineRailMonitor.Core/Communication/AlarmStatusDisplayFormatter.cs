using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Communication;

public static class AlarmStatusDisplayFormatter
{
    public static string Format(AlarmStatus? status) => status switch
    {
        AlarmStatus.Unconfirmed => "未确认",
        AlarmStatus.Acknowledged => "已确认",
        AlarmStatus.Recovered => "已恢复",
        _ => "无需确认"
    };
}
