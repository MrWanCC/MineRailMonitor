namespace MineRailMonitor.Core.Tests;

public sealed class VehicleMatchLoggingIntegrationTests
{
    [Fact]
    public void Logs_the_resolved_vehicle_match_from_the_runtime_coordinator()
    {
        var sourcePath = LocateSourceFile("src", "MineRailMonitor", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("VehicleMatchResolved += LogVehicleMatch", source, StringComparison.Ordinal);
        Assert.Contains("private void LogVehicleMatch(VehicleMatchResult result)", source, StringComparison.Ordinal);
        Assert.Contains("VehicleMatchDiagnosticFormatter.Format(result)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LogVehicleMatch(parsedFrame)", source, StringComparison.Ordinal);

        var coordinatorPath = LocateSourceFile("src", "MineRailMonitor.Core", "Services", "RfidRuntimeCoordinator.cs");
        var coordinator = File.ReadAllText(coordinatorPath);
        var resolveIndex = coordinator.IndexOf("_vehicleMatchResolver.Resolve(frame, state)", StringComparison.Ordinal);
        var sessionIndex = coordinator.IndexOf("new StationRecognitionSession(", resolveIndex, StringComparison.Ordinal);
        Assert.True(resolveIndex >= 0);
        Assert.True(sessionIndex > resolveIndex);
        Assert.Contains("VehicleMatchResolved?.Invoke(vehicleMatchResult)", coordinator, StringComparison.Ordinal);
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
