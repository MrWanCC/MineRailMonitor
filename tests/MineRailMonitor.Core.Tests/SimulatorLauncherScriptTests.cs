namespace MineRailMonitor.Core.Tests;

public sealed class SimulatorLauncherScriptTests
{
    [Fact]
    public void Simulator_launcher_uses_the_current_project_directory()
    {
        var script = File.ReadAllText(LocateLauncherScript());

        Assert.Contains("set \"ROOT=%~dp0\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MineRailMonitor-Clean", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MineRailMonitor.Simulator\\MineRailMonitor.Simulator.csproj", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet build \"%PROJ%\" --configuration Debug --no-restore", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bin\\Release\\net48", script, StringComparison.OrdinalIgnoreCase);
    }

    private static string LocateLauncherScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "一键启动-RFID读卡分站模拟器.cmd");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Unable to locate the simulator launcher script.");
    }
}
