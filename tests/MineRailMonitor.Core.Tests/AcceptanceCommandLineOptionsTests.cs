using System.Net;
using MineRailMonitor.Core.Acceptance;

namespace MineRailMonitor.Core.Tests;

public sealed class AcceptanceCommandLineOptionsTests
{
    [Fact]
    public void Acceptance_options_use_only_loopback_and_test_ports()
    {
        var options = AcceptanceCommandLineOptions.Parse(new[]
        {
            "--acceptance", "--database", "artifacts/acceptance/run/a.db",
            "--runtime-state", "artifacts/acceptance/run/runtime.json",
            "--log-dir", "artifacts/acceptance/run/logs",
            "--ready-file", "artifacts/acceptance/run/ready",
            "--stop-file", "artifacts/acceptance/run/stop",
            "--listen-port", "62102", "--simulator-port", "62101"
        });

        Assert.True(options.Enabled);
        Assert.Equal(IPAddress.Loopback, options.ListenAddress);
        Assert.Equal(IPAddress.Loopback, options.SimulatorAddress);
        Assert.Equal(62102, options.ListenPort);
        Assert.Equal(62101, options.SimulatorPort);
        Assert.Equal(2, options.InterVehicleTimeoutSeconds);
    }

    [Fact]
    public void Acceptance_options_reject_formal_database_path_before_startup()
    {
        Assert.Throws<ArgumentException>(() => AcceptanceCommandLineOptions.Parse(new[]
        {
            "--acceptance", "--database", "Data/MineRailMonitor.db",
            "--runtime-state", "artifacts/acceptance/run/runtime.json",
            "--log-dir", "artifacts/acceptance/run/logs",
            "--ready-file", "artifacts/acceptance/run/ready",
            "--stop-file", "artifacts/acceptance/run/stop",
            "--listen-port", "62102", "--simulator-port", "62101"
        }));
    }

    [Fact]
    public void Acceptance_options_reject_unknown_non_loopback_target_arguments()
    {
        Assert.Throws<ArgumentException>(() => AcceptanceCommandLineOptions.Parse(new[]
        {
            "--acceptance", "--database", "artifacts/acceptance/run/a.db",
            "--runtime-state", "artifacts/acceptance/run/runtime.json",
            "--log-dir", "artifacts/acceptance/run/logs",
            "--ready-file", "artifacts/acceptance/run/ready",
            "--stop-file", "artifacts/acceptance/run/stop",
            "--listen-port", "62102", "--simulator-port", "62101",
            "--station-address", "198.51.100.254"
        }));
    }

    [Fact]
    public void Normal_arguments_return_disabled_options_without_test_paths()
    {
        var options = AcceptanceCommandLineOptions.Parse(Array.Empty<string>());

        Assert.False(options.Enabled);
        Assert.Null(options.DatabasePath);
    }
}
