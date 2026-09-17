using System.Globalization;
using System.IO;
using System.Net;

namespace MineRailMonitor.Simulator.Acceptance;

public sealed class SimulatorCommandLineOptions
{
    private SimulatorCommandLineOptions(bool testMode, string? scenario, int port, int port620, string? resultPath, string? readyFile)
    {
        TestMode = testMode;
        Scenario = scenario;
        Port = port;
        Port620 = port620;
        ResultPath = resultPath;
        ReadyFile = readyFile;
    }

    public bool TestMode { get; }

    public string? Scenario { get; }

    public int Port { get; }

    public int Port620 { get; }

    public string? ResultPath { get; }

    public string? ReadyFile { get; }

    public IPAddress ListenAddress => IPAddress.Loopback;

    public static SimulatorCommandLineOptions Parse(IEnumerable<string> args)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));

        var values = args.ToArray();
        var testMode = false;
        var scenario = (string?)null;
        var resultPath = (string?)null;
        var readyFile = (string?)null;
        var port = 62101;
        var port620 = 62111;

        for (var index = 0; index < values.Length; index++)
        {
            var argument = values[index];
            if (string.IsNullOrWhiteSpace(argument)) continue;
            if (string.Equals(argument, "--test-mode", StringComparison.OrdinalIgnoreCase))
            {
                testMode = true;
                continue;
            }
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                if (index == 0) continue;
                throw new ArgumentException($"Unexpected Simulator argument: {argument}", nameof(args));
            }

            switch (argument.ToLowerInvariant())
            {
                case "--scenario":
                    scenario = ReadValue(values, ref index, argument);
                    break;
                case "--result":
                    resultPath = ReadValue(values, ref index, argument);
                    break;
                case "--ready-file":
                    readyFile = ReadValue(values, ref index, argument);
                    break;
                case "--port":
                    var portText = ReadValue(values, ref index, argument);
                    if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1024 || port > 65535)
                    {
                        throw new ArgumentException("Simulator test port must be from 1024 to 65535.", nameof(args));
                    }
                    break;
                case "--port-620":
                    var port620Text = ReadValue(values, ref index, argument);
                    if (!int.TryParse(port620Text, NumberStyles.None, CultureInfo.InvariantCulture, out port620) || port620 < 1024 || port620 > 65535)
                    {
                        throw new ArgumentException("Simulator test port for yard 620 must be from 1024 to 65535.", nameof(args));
                    }
                    break;
                default:
                    throw new ArgumentException($"Unknown Simulator argument: {argument}", nameof(args));
            }
        }

        if (!testMode)
        {
            return new SimulatorCommandLineOptions(false, null, 0, 0, null, null);
        }

        if (port == 62001 || port == 62002 || port620 == 62001 || port620 == 62002)
        {
            throw new ArgumentException("Simulator acceptance mode cannot use production UDP ports.", nameof(args));
        }
        if (string.IsNullOrWhiteSpace(scenario) || string.IsNullOrWhiteSpace(resultPath) || string.IsNullOrWhiteSpace(readyFile))
        {
            throw new ArgumentException("Simulator test mode requires --scenario, --result, and --ready-file.", nameof(args));
        }

        _ = AcceptanceScenario.Parse(scenario!);
        if (port == port620)
        {
            throw new ArgumentException("Simulator yard listeners must use different UDP ports.", nameof(args));
        }

        return new SimulatorCommandLineOptions(true, scenario, port, port620, Path.GetFullPath(resultPath), Path.GetFullPath(readyFile));
    }

    private static string ReadValue(IReadOnlyList<string> values, ref int index, string option)
    {
        if (index + 1 >= values.Count || string.IsNullOrWhiteSpace(values[index + 1]) || values[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Simulator option {option} requires a value.", nameof(values));
        }

        index++;
        return values[index];
    }
}
