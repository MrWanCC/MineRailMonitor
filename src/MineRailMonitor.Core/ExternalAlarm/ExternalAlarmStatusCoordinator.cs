using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.ExternalAlarm;

public sealed class ExternalAlarmStatusCoordinator : IDisposable
{
    private readonly ExternalAlarmFrameCodec _codec;
    private readonly IExternalAlarmStatusSender _sender;

    public ExternalAlarmStatusCoordinator(
        ExternalAlarmFrameCodec codec,
        IExternalAlarmStatusSender sender)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public event Action<ExternalAlarmStationSnapshot, Exception>? SendFailed;

    public async Task PublishAsync(
        StationRuntimeState state,
        CancellationToken cancellationToken = default)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        var snapshot = ExternalAlarmStatusMapper.FromRuntimeState(state);
        try
        {
            await _sender.SendAsync(_codec.Encode(snapshot), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SendFailed?.Invoke(snapshot, exception);
        }
    }

    public void Dispose()
    {
        if (_sender is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
