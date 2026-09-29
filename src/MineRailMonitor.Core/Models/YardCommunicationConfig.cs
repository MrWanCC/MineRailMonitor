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

    /// <summary>
    /// Independent UDP port used by the external system to send alarm clear commands.
    /// Zero disables the receiver for backward-compatible project manifests.
    /// </summary>
    public int ExternalAlarmListenPort { get; set; }

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

        if (ExternalAlarmListenPort is < 0 or > 65535)
        {
            errors.Add($"站场 {YardId} 的外部报警监听端口无效：{ExternalAlarmListenPort}。");
        }
        else if (ExternalAlarmListenPort > 0 && ExternalAlarmListenPort == ListenPort)
        {
            errors.Add($"站场 {YardId} 的外部报警监听端口不能复用 RFID 监听端口：{ExternalAlarmListenPort}。");
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

    public bool TryResolveExternalAlarmEndpoint(out IPEndPoint endpoint)
    {
        if (IPAddress.TryParse(ListenIp, out var address) &&
            !IPAddress.None.Equals(address) &&
            ExternalAlarmListenPort is >= 1 and <= 65535)
        {
            endpoint = new IPEndPoint(address, ExternalAlarmListenPort);
            return true;
        }

        endpoint = new IPEndPoint(IPAddress.None, 0);
        return false;
    }
}
