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
    private readonly IReadOnlyList<RfidStationEndpointKey> _stationKeys;
    private readonly Dictionary<RfidStationEndpointKey, RfidStationPollingStatus> _statuses;
    private readonly RfidProtocolAddressCollectionView<RfidStationPollingStatus> _statusView;

    public RfidStationPoller(
        IEnumerable<RfidStationConfig> stations,
        int pollIntervalMs,
        IRfidRequestSender sender,
        IRfidTimeProvider timeProvider,
        IRfidPollCommandProvider? commandProvider = null)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));
        if (pollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(pollIntervalMs));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _commandProvider = commandProvider;
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
            _statuses.Add(
                _stationKeys[index],
                new RfidStationPollingStatus { StationAddress = _stations[index].ProtocolAddress });
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
                status.LastRequestAt = requestedAt;
                status.RequestCount++;
                status.LastError = null;
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
                status.LastErrorAt = _timeProvider.UtcNow;
                status.LastError = exception.Message;
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

        var stationIndex = _stations
            .Select((item, index) => new { Station = item, Index = index })
            .FirstOrDefault(item =>
            item.Station.ProtocolAddress == protocolAddress &&
            item.Station.TryResolveEndpoint(out var endpoint) &&
            endpoint.Port == sourceEndpoint.Port &&
            endpoint.Address.Equals(sourceEndpoint.Address));
        if (stationIndex is null || !_statuses.TryGetValue(_stationKeys[stationIndex.Index], out var status))
        {
            return false;
        }

        status.LastResponseAt = receivedAt;
        status.ResponseCount++;
        return true;
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
