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

    /// <summary>
    /// Legacy source-port value retained for old project files.
    /// </summary>
    /// <remarks>
    /// Obsolete: runtime External Alarm communication does not use this property. The active
    /// local endpoint comes from <see cref="YardCommunicationConfig.ExternalAlarmListenIp" />
    /// and <see cref="YardCommunicationConfig.ExternalAlarmListenPort" />.
    /// </remarks>
    public int SourcePort { get; set; }

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

        if (SourcePort is < 0 or > 65535)
        {
            errors.Add($"站场 {YardId} 的报警转发本机发送端口无效：{SourcePort}。");
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

    /// <summary>
    /// Resolves the legacy source endpoint for compatibility tooling only.
    /// </summary>
    /// <remarks>Obsolete: the application runtime must not call this method.</remarks>
    [Obsolete("仅为旧配置兼容保留；运行时 External Alarm 不使用 SourcePort。", false)]
    public bool TryResolveSourceEndpoint(IPAddress localAddress, out IPEndPoint endpoint)
    {
        if (SourcePort is >= 1 and <= 65535 && localAddress is not null)
        {
            endpoint = new IPEndPoint(localAddress, SourcePort);
            return true;
        }

        endpoint = new IPEndPoint(IPAddress.None, 0);
        return false;
    }
}
