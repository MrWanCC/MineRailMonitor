namespace MineRailMonitor.Core.Tests;

public sealed class ExternalAlarmCompositionMarkupTests
{
    [Fact]
    public void Main_window_creates_injects_and_updates_external_alarm_status_coordinators()
    {
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("CreateExternalAlarmStatusCoordinators(project)", code, StringComparison.Ordinal);
        Assert.Contains("externalAlarmStatusCoordinators:", code, StringComparison.Ordinal);
        Assert.Contains("ApplyExternalAlarmStatusCoordinators", code, StringComparison.Ordinal);
        Assert.Contains(
            "ExternalAlarmStatusSendFailed += OnYardExternalAlarmStatusSendFailed",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_logs_external_alarm_status_failures_without_touching_rfid_health()
    {
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("外部报警状态 UDP 发送失败", code, StringComparison.Ordinal);
        Assert.Contains("snapshot.StationAddress", code, StringComparison.Ordinal);
        Assert.DoesNotContain("context.SetError", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_rfidListenerHealthy = false", code, StringComparison.Ordinal);
    }

    private static string LocateSourceFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = segments.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar.ToString(), segments));
    }
}
