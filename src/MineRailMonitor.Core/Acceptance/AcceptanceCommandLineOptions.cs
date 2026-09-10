using System.Net;
using System.Globalization;

namespace MineRailMonitor.Core.Acceptance;

public sealed class AcceptanceCommandLineOptions
{
    private const int DefaultListenPort = 62102;
    private const int DefaultSimulatorPort = 62101;
    private const int AcceptanceInterVehicleTimeoutSeconds = 2;

    private AcceptanceCommandLineOptions(
        bool enabled,
        string? databasePath,
        string? runtimeStatePath,
        string? logDirectory,
        string? readyFile,
        string? stopFile,
        int listenPort,
        int simulatorPort)
    {
        Enabled = enabled;
        DatabasePath = databasePath;
        RuntimeStatePath = runtimeStatePath;
        LogDirectory = logDirectory;
        ReadyFile = readyFile;
        StopFile = stopFile;
        ListenPort = listenPort;
        SimulatorPort = simulatorPort;
    }

    public static AcceptanceCommandLineOptions Disabled { get; } = new(
        enabled: false,
        databasePath: null,
        runtimeStatePath: null,
        logDirectory: null,
        readyFile: null,
        stopFile: null,
        listenPort: 0,
        simulatorPort: 0);

    public bool Enabled { get; }

    public IPAddress ListenAddress => IPAddress.Loopback;

    public IPAddress SimulatorAddress => IPAddress.Loopback;

    public int ListenPort { get; }

    public int SimulatorPort { get; }

    public int InterVehicleTimeoutSeconds => AcceptanceInterVehicleTimeoutSeconds;

    public string? DatabasePath { get; }

    public string? RuntimeStatePath { get; }

    public string? LogDirectory { get; }

    public string? ReadyFile { get; }

    public string? StopFile { get; }

    public static AcceptanceCommandLineOptions Parse(IEnumerable<string> args)
    {
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        var values = args.ToArray();
        var enabled = false;
        var databasePath = (string?)null;
        var runtimeStatePath = (string?)null;
        var logDirectory = (string?)null;
        var readyFile = (string?)null;
        var stopFile = (string?)null;
        var listenPort = DefaultListenPort;
        var simulatorPort = DefaultSimulatorPort;

        for (var index = 0; index < values.Length; index++)
        {
            var argument = values[index];
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            if (string.Equals(argument, "--acceptance", StringComparison.OrdinalIgnoreCase))
            {
                enabled = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                if (index == 0)
                {
                    continue;
                }

                throw new ArgumentException($"Unexpected acceptance argument: {argument}", nameof(args));
            }

            switch (argument.ToLowerInvariant())
            {
                case "--database":
                    databasePath = ReadValue(values, ref index, argument);
                    break;
                case "--runtime-state":
                    runtimeStatePath = ReadValue(values, ref index, argument);
                    break;
                case "--log-dir":
                    logDirectory = ReadValue(values, ref index, argument);
                    break;
                case "--ready-file":
                    readyFile = ReadValue(values, ref index, argument);
                    break;
                case "--stop-file":
                    stopFile = ReadValue(values, ref index, argument);
                    break;
                case "--listen-port":
                    listenPort = ParsePort(ReadValue(values, ref index, argument), argument);
                    break;
                case "--simulator-port":
                    simulatorPort = ParsePort(ReadValue(values, ref index, argument), argument);
                    break;
                default:
                    throw new ArgumentException($"Unknown acceptance argument: {argument}", nameof(args));
            }
        }

        if (!enabled)
        {
            return Disabled;
        }

        var fullDatabasePath = RequireAcceptancePath(databasePath, "--database", mustBeDatabase: true);
        var fullRuntimeStatePath = RequireAcceptancePath(runtimeStatePath, "--runtime-state", mustBeDatabase: false);
        var fullLogDirectory = RequireAcceptancePath(logDirectory, "--log-dir", mustBeDatabase: false);
        var fullReadyFile = RequireAcceptancePath(readyFile, "--ready-file", mustBeDatabase: false);
        var fullStopFile = RequireAcceptancePath(stopFile, "--stop-file", mustBeDatabase: false);

        if (listenPort == 62002 || listenPort == 62001 || simulatorPort == 62002 || simulatorPort == 62001)
        {
            throw new ArgumentException("Acceptance mode cannot use production UDP ports.", nameof(args));
        }

        if (listenPort == simulatorPort)
        {
            throw new ArgumentException("Acceptance listener and Simulator ports must differ.", nameof(args));
        }

        return new AcceptanceCommandLineOptions(
            enabled: true,
            databasePath: fullDatabasePath,
            runtimeStatePath: fullRuntimeStatePath,
            logDirectory: fullLogDirectory,
            readyFile: fullReadyFile,
            stopFile: fullStopFile,
            listenPort: listenPort,
            simulatorPort: simulatorPort);
    }

    public override string ToString() => Enabled
        ? $"Acceptance(loopback, listen={ListenPort}, simulator={SimulatorPort}, database={DatabasePath})"
        : "Disabled";

    private static string ReadValue(IReadOnlyList<string> values, ref int index, string option)
    {
        if (index + 1 >= values.Count || string.IsNullOrWhiteSpace(values[index + 1]) || values[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Acceptance option {option} requires a value.", nameof(values));
        }

        index++;
        return values[index];
    }

    private static int ParsePort(string value, string option)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port < 1024 || port > 65535)
        {
            throw new ArgumentException($"Acceptance option {option} must be a TCP/UDP port from 1024 to 65535.", nameof(value));
        }

        return port;
    }

    private static string RequireAcceptancePath(string? value, string option, bool mustBeDatabase)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Acceptance option {option} is required.", nameof(value));
        }

        var fullPath = Path.GetFullPath(value);
        var normalized = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var marker = string.Concat(
            Path.DirectorySeparatorChar,
            "artifacts",
            Path.DirectorySeparatorChar,
            "acceptance",
            Path.DirectorySeparatorChar);
        if (normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new ArgumentException($"Acceptance option {option} must stay under an artifacts\\acceptance directory.", nameof(value));
        }

        var productionDatabase = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Data", "MineRailMonitor.db"));
        if (string.Equals(normalized, productionDatabase.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Acceptance mode cannot use the production SQLite database.", nameof(value));
        }

        if (mustBeDatabase && !string.Equals(Path.GetExtension(fullPath), ".db", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Acceptance database path must use the .db extension.", nameof(value));
        }

        return fullPath;
    }
}
