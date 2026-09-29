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
    /// Optional local IP used by the external alarm UDP channel.
    /// Empty keeps backward compatibility by falling back to ListenIp.
    /// </summary>
    public string ExternalAlarmListenIp { get; set; } = string.Empty;

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
        else if (ExternalAlarmListenPort > 0 &&
                 !TryResolveExternalAlarmListenAddress(out _))
        {
            errors.Add($"站场 {YardId} 的外部报警监听IP地址无效：{GetExternalAlarmListenIp()}。");
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
        if (TryResolveExternalAlarmListenAddress(out var address) &&
            !IPAddress.None.Equals(address) &&
            ExternalAlarmListenPort is >= 1 and <= 65535)
        {
            endpoint = new IPEndPoint(address, ExternalAlarmListenPort);
            return true;
        }

        endpoint = new IPEndPoint(IPAddress.None, 0);
        return false;
    }

    public IReadOnlyList<string> ValidateExternalAlarm(bool enabled)
    {
        if (!enabled || ExternalAlarmListenPort > 0)
        {
            return Array.Empty<string>();
        }

        return new[]
        {
            $"站场 {YardId} 的外部报警本机固定收发端口必须填写（1~65535）。"
        };
    }

    private bool TryResolveExternalAlarmListenAddress(out IPAddress address)
    {
        if (IPAddress.TryParse(GetExternalAlarmListenIp(), out address!) &&
            !IPAddress.None.Equals(address))
        {
            return true;
        }

        address = IPAddress.None;
        return false;
    }

    private string GetExternalAlarmListenIp() =>
        string.IsNullOrWhiteSpace(ExternalAlarmListenIp) ? ListenIp : ExternalAlarmListenIp;
}
