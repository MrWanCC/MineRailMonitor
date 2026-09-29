namespace MineRailMonitor.Core.ExternalAlarm;

public interface IExternalAlarmStatusSender
{
    Task SendAsync(byte[] payload, CancellationToken cancellationToken);
}
