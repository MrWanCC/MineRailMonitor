using System.Net;

namespace MineRailMonitor.Core.Models;

/// <summary>
/// The local UDP interface owned by one yard communication context.
/// </summary>
public sealed class YardCommunicationConfig
{
    public string YardId { get; set; } = string.Empty;

    public string ListenIp { get; set; } = string.Empty;

    public int ListenPort { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// True only for projects loaded without the new per-yard configuration.
    /// It is intentionally not persisted as a normal yard configuration.
    /// </summary>
    public bool IsLegacySharedListener { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(YardId) && !IsLegacySharedListener)
        {
            errors.Add("通信接口所属站场不能为空。");
        }

        if (!IPAddress.TryParse(ListenIp, out var address) || IPAddress.None.Equals(address))
        {
            errors.Add($"站场 {YardId} 的监听IP地址无效：{ListenIp}。");
        }

        if (ListenPort is < 1 or > 65535)
        {
            errors.Add($"站场 {YardId} 的监听端口无效：{ListenPort}。");
        }

        return errors;
    }

    public bool TryResolveEndpoint(out IPEndPoint endpoint)
    {
        if (IPAddress.TryParse(ListenIp, out var address) &&
            !IPAddress.None.Equals(address) &&
            ListenPort is >= 1 and <= 65535)
        {
            endpoint = new IPEndPoint(address, ListenPort);
            return true;
        }

        endpoint = new IPEndPoint(IPAddress.None, 0);
        return false;
    }
}
