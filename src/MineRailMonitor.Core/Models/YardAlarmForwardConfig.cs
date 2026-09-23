using System.Net;

namespace MineRailMonitor.Core.Models;

/// <summary>
/// Optional external UDP destination for uncoupling alarms belonging to one yard.
/// </summary>
public sealed class YardAlarmForwardConfig
{
    public string YardId { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string TargetIp { get; set; } = string.Empty;

    public int TargetPort { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(YardId))
        {
            errors.Add("报警转发所属站场不能为空。");
        }

        if (!Enabled)
        {
            return errors;
        }

        if (!IPAddress.TryParse(TargetIp, out var address) ||
            IPAddress.None.Equals(address) ||
            IPAddress.Any.Equals(address) ||
            IPAddress.IPv6Any.Equals(address))
        {
            errors.Add($"站场 {YardId} 的报警转发目标IP地址无效：{TargetIp}。");
        }

        if (TargetPort is < 1 or > 65535)
        {
            errors.Add($"站场 {YardId} 的报警转发目标端口无效：{TargetPort}。");
        }

        return errors;
    }

    public bool TryResolveEndpoint(out IPEndPoint endpoint)
    {
        if (Enabled &&
            IPAddress.TryParse(TargetIp, out var address) &&
            !IPAddress.None.Equals(address) &&
            !IPAddress.Any.Equals(address) &&
            !IPAddress.IPv6Any.Equals(address) &&
            TargetPort is >= 1 and <= 65535)
        {
            endpoint = new IPEndPoint(address, TargetPort);
            return true;
        }

        endpoint = new IPEndPoint(IPAddress.None, 0);
        return false;
    }
}
