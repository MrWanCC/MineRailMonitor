using System.Net;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Protocol;

namespace MineRailMonitor.Core.Communication;

public sealed class RfidStationPoller : IRfidStationPoller
{
    private readonly IReadOnlyList<RfidStationConfig> _stations;
    private readonly TimeSpan _interval;
    private readonly IRfidRequestSender _sender;
    private readonly IRfidTimeProvider _timeProvider;
    private readonly IRfidPollCommandProvider? _commandProvider;
    private readonly TimeSpan _requestTimeout;
    private readonly IReadOnlyList<RfidStationEndpointKey> _stationKeys;
    private readonly Dictionary<RfidStationEndpointKey, RfidStationPollingStatus> _statuses;
    private readonly RfidProtocolAddressCollectionView<RfidStationPollingStatus> _statusView;

    public RfidStationPoller(
        IEnumerable<RfidStationConfig> stations,
        int pollIntervalMs,
        IRfidRequestSender sender,
        IRfidTimeProvider timeProvider,
        IRfidPollCommandProvider? commandProvider = null,
        TimeSpan? requestTimeout = null)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));
        if (pollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(pollIntervalMs));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _commandProvider = commandProvider;
        _requestTimeout = requestTimeout ?? RfidRuntimePolicy.Default.OfflineTimeout;
        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }
        _stations = stations.Where(station => station.Enabled).ToArray();
        if (_stations.Count == 0) throw new ArgumentException("At least one enabled RFID station is required.", nameof(stations));
        var duplicateCommunicationKeys = RfidStationConfig.FindDuplicateCommunicationKeys(_stations);
        if (duplicateCommunicationKeys.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, duplicateCommunicationKeys), nameof(stations));
        }
        _interval = TimeSpan.FromMilliseconds(pollIntervalMs);
        _stationKeys = _stations
            .Select((station, index) => CreateStationKey(station, index))
            .ToArray();
        _statuses = new Dictionary<RfidStationEndpointKey, RfidStationPollingStatus>();
        for (var index = 0; index < _stations.Count; index++)
        {
            var stationKey = _stationKeys[index];
            _statuses.Add(
                stationKey,
                new RfidStationPollingStatus
                {
                    StationId = _stations[index].StationId,
                    EndpointKey = stationKey,
                    StationAddress = _stations[index].ProtocolAddress
                });
        }
        _statusView = new RfidProtocolAddressCollectionView<RfidStationPollingStatus>(_statuses.Values, status => status.StationAddress);
    }

    public IReadOnlyDictionary<byte, RfidStationPollingStatus> StationStatuses => _statusView;

    public IReadOnlyDictionary<RfidStationEndpointKey, RfidStationPollingStatus> EndpointStatuses => _statuses;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var index = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            EvaluateTimeouts(_timeProvider.UtcNow, _requestTimeout);
            var station = _stations[index];
            var key = _stationKeys[index];
            var command = _commandProvider is IRfidEndpointPollCommandProvider endpointProvider
                ? endpointProvider.GetCommand(station)
                : _commandProvider?.GetCommand(station.ProtocolAddress) ?? RfidPollCommand.Read;
            var status = _statuses[key];
            try
            {
                if (!station.TryResolveEndpoint(out var endpoint))
                {
                    throw new InvalidOperationException($"RFID基站端点配置无效：{station.StationId}。");
                }

                await _sender.SendAsync(
                    command == RfidPollCommand.Read
                        ? RfidRequestFrameBuilder.Build(station)
                        : RfidRequestFrameBuilder.Build(station, command),
                    endpoint,
                    cancellationToken).ConfigureAwait(false);
                var requestedAt = _timeProvider.UtcNow;
                status.RecordSent(requestedAt);
                if (_commandProvider is IRfidEndpointPollCommandProvider endpointCommandProvider)
                {
                    endpointCommandProvider.MarkCommandSent(station, command, requestedAt);
                }
                else
                {
                    _commandProvider?.MarkCommandSent(station.ProtocolAddress, command, requestedAt);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                status.RecordSendError(_timeProvider.UtcNow, exception.Message);
            }
            index = (index + 1) % _stations.Count;
            if (!cancellationToken.IsCancellationRequested)
            {
                await _timeProvider.DelayAsync(_interval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public bool RecordResponse(IPEndPoint sourceEndpoint, byte protocolAddress, DateTimeOffset receivedAt)
    {
        if (sourceEndpoint is null)
        {
            return false;
        }

        if (!TryGetStatus(sourceEndpoint, protocolAddress, out var status))
        {
            return false;
        }

        status.RecordResponse(receivedAt);
        return true;
    }

    public bool RecordSent(IPEndPoint destinationEndpoint, byte protocolAddress, DateTimeOffset sentAt)
    {
        if (destinationEndpoint is null || !TryGetStatus(destinationEndpoint, protocolAddress, out var status))
        {
            return false;
        }

        status.RecordSent(sentAt);
        return true;
    }

    public void EvaluateTimeouts(DateTimeOffset now, TimeSpan timeout)
    {
        foreach (var status in _statuses.Values)
        {
            status.RecordTimeoutsIfDue(now, timeout);
        }
    }

    public void SynchronizeOnlineStates(IReadOnlyDictionary<RfidStationEndpointKey, StationRuntimeState> runtimeStates)
    {
        if (runtimeStates is null)
        {
            throw new ArgumentNullException(nameof(runtimeStates));
        }

        foreach (var status in _statuses.Values)
        {
            if (status.EndpointKey.HasValue && runtimeStates.TryGetValue(status.EndpointKey.Value, out var state))
            {
                status.SetOnlineState(state.CommunicationState == StationCommunicationState.Online);
            }
        }
    }

    private bool TryGetStatus(IPEndPoint endpoint, byte protocolAddress, out RfidStationPollingStatus status)
    {
        var stationIndex = _stations
            .Select((item, index) => new { Station = item, Index = index })
            .FirstOrDefault(item =>
                item.Station.ProtocolAddress == protocolAddress &&
                item.Station.TryResolveEndpoint(out var configuredEndpoint) &&
                configuredEndpoint.Port == endpoint.Port &&
                configuredEndpoint.Address.Equals(endpoint.Address));
        if (stationIndex is not null && _statuses.TryGetValue(_stationKeys[stationIndex.Index], out status!))
        {
            return true;
        }

        status = null!;
        return false;
    }

    private static RfidStationEndpointKey CreateStationKey(RfidStationConfig station, int index)
    {
        if (station.TryResolveEndpoint(out var endpoint))
        {
            return new RfidStationEndpointKey(endpoint, station.ProtocolAddress);
        }

        // Keep invalid legacy configurations reportable without collapsing two
        // stations that happen to share a protocol address. The send path still
        // reports the invalid endpoint instead of emitting a packet.
        return new RfidStationEndpointKey(new IPEndPoint(IPAddress.None, index + 1), station.ProtocolAddress);
    }
}
