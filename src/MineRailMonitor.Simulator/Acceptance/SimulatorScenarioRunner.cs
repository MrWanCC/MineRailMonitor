using System.Text.Json;
using System.IO;
using MineRailMonitor.Core.Acceptance;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Simulator.Communication;
using MineRailMonitor.Simulator.Models;
using MineRailMonitor.Simulator.Protocol;

namespace MineRailMonitor.Simulator.Acceptance;

public sealed class SimulatorScenarioRunner
{
    private static readonly TimeSpan VehicleInterval = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan ScenarioTimeout = TimeSpan.FromSeconds(60);
    private readonly AcceptanceScenarioDefinition _scenario;
    private readonly int _port;
    private readonly int _port620;
    private readonly string _resultPath;
    private readonly string _readyFile;

    public SimulatorScenarioRunner(AcceptanceScenarioDefinition scenario, int port, string resultPath, string readyFile)
        : this(scenario, port, port + 10, resultPath, readyFile)
    {
    }

    public SimulatorScenarioRunner(AcceptanceScenarioDefinition scenario, int port, int port620, string resultPath, string readyFile)
    {
        _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (port620 < 1024 || port620 > 65535) throw new ArgumentOutOfRangeException(nameof(port620));
        if (port == port620) throw new ArgumentException("Simulator yard listeners must use different UDP ports.", nameof(port620));
        if (string.IsNullOrWhiteSpace(resultPath)) throw new ArgumentException("Result path must not be empty.", nameof(resultPath));
        if (string.IsNullOrWhiteSpace(readyFile)) throw new ArgumentException("Ready file must not be empty.", nameof(readyFile));
        _port = port;
        _port620 = port620;
        _resultPath = Path.GetFullPath(resultPath);
        _readyFile = Path.GetFullPath(readyFile);
    }

    public async Task<SimulatorScenarioResult> RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var stations = new[]
        {
            new SimulatorStation { Address = 0x01, Slots = new ushort[14], CommandBytes = new byte[4] },
            new SimulatorStation { Address = 0x04, Slots = new ushort[14], CommandBytes = new byte[4] }
        };
        var responder = new RfidSimulatorResponder(stations, emptySlotValue: 0);
        using var udpResponder560 = new SimulatorUdpResponder(System.Net.IPAddress.Loopback, _port);
        using var udpResponder620 = new SimulatorUdpResponder(System.Net.IPAddress.Loopback, _port620);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ScenarioTimeout);
        var responderTask = Task.WhenAll(
            udpResponder560.RunAsync(responder.CreateResponse, timeoutSource.Token),
            udpResponder620.RunAsync(responder.CreateResponse, timeoutSource.Token));
        var result = new SimulatorScenarioResult
        {
            Scenario = _scenario.Name,
            StartedAt = startedAt
        };

        try
        {
            AtomicFileWriter.WriteAllText(
                _readyFile,
                $"{{\"status\":\"ready\",\"scenario\":\"{_scenario.Name}\",\"ports\":{{\"560\":{_port},\"620\":{_port620}}}}}");
            await ExecuteScenarioAsync(_scenario, stations, responder, timeoutSource.Token).ConfigureAwait(false);
            result.Pass = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.FailureReason = $"Scenario timeout after {ScenarioTimeout.TotalSeconds:0} seconds.";
            throw new TimeoutException(result.FailureReason);
        }
        catch (Exception exception)
        {
            result.FailureReason = exception.ToString();
            throw;
        }
        finally
        {
            result.CompletedAt = DateTimeOffset.Now;
            result.DurationMs = Math.Max(0, (long)(result.CompletedAt - result.StartedAt).TotalMilliseconds);
            result.Requests = responder.RequestLogs;
            result.Responses = responder.ResponseLogs;
            result.FinalStations = stations
                .Select(station => new SimulatorStationSnapshot
                {
                    StationAddress = station.Address,
                    Slots = SnapshotSlots(station)
                })
                .ToArray();
            AtomicFileWriter.WriteAllText(
                _resultPath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            timeoutSource.Cancel();
            try
            {
                await responderTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        return result;
    }

    private static async Task ExecuteScenarioAsync(
        AcceptanceScenarioDefinition scenario,
        IReadOnlyList<SimulatorStation> stations,
        RfidSimulatorResponder responder,
        CancellationToken cancellationToken)
    {
        foreach (var station in stations)
        {
            lock (station)
            {
                Array.Clear(station.Slots, 0, station.Slots.Length);
            }
        }

        foreach (var stationAddress in new byte[] { 0x01, 0x04 })
        {
            await WaitForAsync(
                () => responder.RequestLogs.Any(item => item.StationAddress == stationAddress && item.Command == RfidPollCommand.Read),
                cancellationToken,
                $"上位机未开始读取基站 {stationAddress:X2}。").ConfigureAwait(false);
        }

        if (scenario.Name == "TwoStationsConcurrent")
        {
            await Task.WhenAll(scenario.Trains.Select(train => ApplyTrainAsync(train, stations, responder, cancellationToken))).ConfigureAwait(false);
            foreach (var stationAddress in new byte[] { 0x01, 0x04 })
            {
                await WaitForClearAndEmptyAsync(stationAddress, responder, minimumClearCount: 1, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        if (scenario.Name == "TwoConsecutiveTrains")
        {
            for (var trainIndex = 0; trainIndex < scenario.Trains.Count; trainIndex++)
            {
                var train = scenario.Trains[trainIndex];
                await ApplyTrainAsync(train, stations, responder, cancellationToken).ConfigureAwait(false);
                await WaitForClearAndEmptyAsync(train.StationAddress, responder, trainIndex + 1, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        var plan = scenario.Trains.Single();
        await ApplyTrainAsync(plan, stations, responder, cancellationToken).ConfigureAwait(false);
        if (!scenario.ReappearsOldTags)
        {
            await WaitForClearAndEmptyAsync(plan.StationAddress, responder, minimumClearCount: 1, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        await WaitForClearAsync(plan.StationAddress, responder, minimumClearCount: 1, cancellationToken: cancellationToken).ConfigureAwait(false);
        var stationModel = stations.Single(item => item.Address == plan.StationAddress);
        lock (stationModel)
        {
            stationModel.Slots[0] = plan.VehicleRfids[0];
            stationModel.Slots[1] = plan.VehicleRfids[1];
            stationModel.Slots[2] = plan.VehicleRfids[2];
        }

        var firstClearAt = responder.RequestLogs
            .Where(item => item.StationAddress == plan.StationAddress && item.Command == RfidPollCommand.Clear)
            .Select(item => item.ReceivedAt)
            .Last();
        await WaitForAsync(
            () => responder.ResponseLogs.Any(item => item.StationAddress == plan.StationAddress && item.SentAt >= firstClearAt && !item.IsEmpty),
            cancellationToken,
            "Clear后旧标签未在等待空槽期间重新出现。").ConfigureAwait(false);
        await WaitForAsync(
            () => responder.RequestLogs.Count(item => item.StationAddress == plan.StationAddress && item.Command == RfidPollCommand.Clear) >= 2,
            cancellationToken,
            "旧标签重新出现后上位机未再次发送Clear。").ConfigureAwait(false);
        await WaitForEmptyResponsesAsync(plan.StationAddress, responder, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyTrainAsync(
        AcceptanceTrainPlan plan,
        IReadOnlyList<SimulatorStation> stations,
        RfidSimulatorResponder responder,
        CancellationToken cancellationToken)
    {
        var station = stations.Single(item => item.Address == plan.StationAddress);
        var nextInsertionAt = DateTimeOffset.UtcNow;
        foreach (var item in plan.VehicleRfids.Select((rfid, index) => new { rfid, index }))
        {
            var delay = nextInsertionAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            lock (station)
            {
                station.Slots[plan.OccupiedSlotIndexes[item.index]] = item.rfid;
            }

            await WaitForAsync(
                () => responder.ResponseLogs.Any(response =>
                    response.StationAddress == plan.StationAddress &&
                    response.Slots.Contains(item.rfid)),
                cancellationToken,
                $"基站 {plan.StationAddress:X2} 未通过UDP响应观察到 RFID {item.rfid:X4}。").ConfigureAwait(false);
            nextInsertionAt = DateTimeOffset.UtcNow + VehicleInterval;
        }
    }

    private static async Task WaitForClearAndEmptyAsync(
        byte stationAddress,
        RfidSimulatorResponder responder,
        int minimumClearCount,
        CancellationToken cancellationToken)
    {
        await WaitForClearAsync(stationAddress, responder, minimumClearCount, cancellationToken).ConfigureAwait(false);
        await WaitForEmptyResponsesAsync(stationAddress, responder, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WaitForClearAsync(
        byte stationAddress,
        RfidSimulatorResponder responder,
        int minimumClearCount,
        CancellationToken cancellationToken)
    {
        await WaitForAsync(
            () => responder.RequestLogs.Count(item => item.StationAddress == stationAddress && item.Command == RfidPollCommand.Clear) >= minimumClearCount,
            cancellationToken,
            $"基站 {stationAddress:X2} 未收到上位机Clear请求。").ConfigureAwait(false);
    }

    private static async Task WaitForEmptyResponsesAsync(byte stationAddress, RfidSimulatorResponder responder, CancellationToken cancellationToken)
    {
        var clearAt = responder.RequestLogs
            .Where(item => item.StationAddress == stationAddress && item.Command == RfidPollCommand.Clear)
            .Select(item => item.ReceivedAt)
            .Last();
        await WaitForAsync(
            () => responder.ResponseLogs.Count(item => item.StationAddress == stationAddress && item.SentAt >= clearAt && item.IsEmpty) >= 2,
            cancellationToken,
            $"基站 {stationAddress:X2} 未完成连续两次空槽确认。").ConfigureAwait(false);
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken, string failureMessage)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<ushort> SnapshotSlots(SimulatorStation station)
    {
        lock (station)
        {
            return station.Slots.ToArray();
        }
    }
}
