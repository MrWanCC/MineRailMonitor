namespace MineRailMonitor.Infrastructure.Logging;

public interface ILogger
{
    void Information(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}
