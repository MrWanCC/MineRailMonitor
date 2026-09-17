using MineRailMonitor.Simulator.Models;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Simulator.Acceptance;

namespace MineRailMonitor.Simulator.Protocol;

public sealed class RfidSimulatorResponder
{
    private readonly IReadOnlyDictionary<byte, SimulatorStation> _stations;
    private readonly ushort _emptySlotValue;
    private readonly object _logSyncRoot = new();
    private readonly List<SimulatorRequestLog> _requestLogs = new();
    private readonly List<SimulatorResponseLog> _responseLogs = new();

    public RfidSimulatorResponder(IEnumerable<SimulatorStation> stations, ushort emptySlotValue)
    {
        if (stations is null)
        {
            throw new ArgumentNullException(nameof(stations));
        }

        _stations = stations.ToDictionary(station => station.Address);
        _emptySlotValue = emptySlotValue;
    }

    public byte[]? CreateResponse(byte[] request)
    {
        if (!IsValidRequest(request))
        {
            return null;
        }
        if (!_stations.TryGetValue(request[2], out var station))
        {
            UnknownAddressReceived?.Invoke(request[2]);
            return null;
        }

        var command = request[4] == 0x01 ? RfidPollCommand.Clear : RfidPollCommand.Read;
        AddRequestLog(new SimulatorRequestLog
        {
            ReceivedAt = DateTimeOffset.Now,
            StationAddress = request[2],
            Command = command,
            FrameLength = request.Length,
            FrameHex = BitConverter.ToString(request).Replace('-', ' ')
        });

        byte[] response;
        ushort[] slots;
        lock (station)
        {
            if (command == RfidPollCommand.Clear)
            {
                // Clear only the simulator's station cache. A later write can model a tag
                // still present in the sensing area being detected again.
                Array.Clear(station.Slots, 0, station.Slots.Length);
            }

            slots = (ushort[])station.Slots.Clone();
            response = RfidResponseFrameBuilder.Build(new SimulatorFrameInput
            {
                Address = station.Address,
                CommandBytes = (byte[])station.CommandBytes.Clone(),
                Slots = slots,
                EmptySlotValue = _emptySlotValue,
                CrcHigh = station.CrcHigh,
                CrcLow = station.CrcLow
            });
        }

        AddResponseLog(new SimulatorResponseLog
        {
            SentAt = DateTimeOffset.Now,
            StationAddress = station.Address,
            FrameLength = response.Length,
            ReportedCardCount = response[7],
            IsEmpty = slots.All(value => value == _emptySlotValue),
            Slots = slots
        });

        return response;
    }

    /// <summary>
    /// Updates the simulated station cache from the manual scene player.
    /// This only changes the values returned by future Read frames.
    /// </summary>
    public bool UpdateSlots(byte address, IReadOnlyList<ushort> slots)
    {
        if (slots is null)
        {
            throw new ArgumentNullException(nameof(slots));
        }
        if (slots.Count != ScenarioPlaybackState.SlotCapacity)
        {
            throw new ArgumentException($"A simulator station must contain {ScenarioPlaybackState.SlotCapacity} slots.", nameof(slots));
        }
        if (!_stations.TryGetValue(address, out var station))
        {
            return false;
        }

        lock (station)
        {
            station.Slots = slots.ToArray();
        }

        return true;
    }

    public event Action<byte>? UnknownAddressReceived;

    public IReadOnlyList<SimulatorRequestLog> RequestLogs
    {
        get
        {
            lock (_logSyncRoot)
            {
                return _requestLogs.ToArray();
            }
        }
    }

    public IReadOnlyList<SimulatorResponseLog> ResponseLogs
    {
        get
        {
            lock (_logSyncRoot)
            {
                return _responseLogs
                    .Select(item => new SimulatorResponseLog
                    {
                        SentAt = item.SentAt,
                        StationAddress = item.StationAddress,
                        FrameLength = item.FrameLength,
                        ReportedCardCount = item.ReportedCardCount,
                        IsEmpty = item.IsEmpty,
                        Slots = item.Slots.ToArray()
                    })
                    .ToArray();
            }
        }
    }

    private void AddRequestLog(SimulatorRequestLog entry)
    {
        lock (_logSyncRoot)
        {
            _requestLogs.Add(entry);
        }
    }

    private void AddResponseLog(SimulatorResponseLog entry)
    {
        lock (_logSyncRoot)
        {
            _responseLogs.Add(entry);
        }
    }

    private static bool IsValidRequest(byte[]? request) =>
        request is { Length: RfidFrameLayout.FrameLength } &&
        request[0] == 0xB0 && request[1] == 0xB0 && request[3] == 0x04 &&
        request[38] == 0xAA && request[39] == 0xAA;
}
