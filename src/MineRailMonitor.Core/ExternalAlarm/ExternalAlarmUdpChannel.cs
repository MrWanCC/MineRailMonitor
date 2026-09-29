using System.Net;
using System.Net.Sockets;

namespace MineRailMonitor.Core.ExternalAlarm;

/// <summary>
/// Owns one fixed local UDP socket for both External Alarm status sending and
/// clear-command receiving.
/// </summary>
public sealed class ExternalAlarmUdpChannel : IExternalAlarmStatusSender, IDisposable
{
    private readonly object _syncRoot = new();
    private readonly IPEndPoint _listenEndpoint;
    private readonly IPEndPoint _targetEndpoint;
    private readonly ExternalAlarmFrameCodec _codec;
    private UdpClient? _client;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveTask;
    private bool _disposed;

    public ExternalAlarmUdpChannel(
        IPEndPoint listenEndpoint,
        IPEndPoint targetEndpoint,
        ExternalAlarmFrameCodec? codec = null)
    {
        if (listenEndpoint is null) throw new ArgumentNullException(nameof(listenEndpoint));
        if (targetEndpoint is null) throw new ArgumentNullException(nameof(targetEndpoint));
        if (listenEndpoint.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(listenEndpoint));
        }
        if (targetEndpoint.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(targetEndpoint));
        }

        _listenEndpoint = new IPEndPoint(listenEndpoint.Address, listenEndpoint.Port);
        _targetEndpoint = new IPEndPoint(targetEndpoint.Address, targetEndpoint.Port);
        _codec = codec ?? new ExternalAlarmFrameCodec();
    }

    public IPEndPoint ListenEndPoint => new(_listenEndpoint.Address, _listenEndpoint.Port);

    public IPEndPoint TargetEndPoint => new(_targetEndpoint.Address, _targetEndpoint.Port);

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _client is not null;
            }
        }
    }

    public event Action<ExternalAlarmClearCommand, IPEndPoint>? ClearReceived;

    public event Action<byte[], IPEndPoint, bool, DateTimeOffset>? FrameReceived;

    public event Action<string, IPEndPoint>? FrameRejected;

    public event Action<Exception>? ReceiveError;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            if (_client is not null)
            {
                return Task.CompletedTask;
            }

            var client = new UdpClient(_listenEndpoint);
            var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _client = client;
            _cancellationTokenSource = linkedCancellation;
            _receiveTask = ReceiveLoopAsync(client, linkedCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        cancellationToken.ThrowIfCancellationRequested();
        UdpClient client;
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            client = _client ?? throw new InvalidOperationException("External Alarm UDP 通道尚未启动。");
        }

        await client.SendAsync(payload, payload.Length, _targetEndpoint).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        UdpClient? client;
        CancellationTokenSource? cancellationTokenSource;
        Task? receiveTask;
        lock (_syncRoot)
        {
            client = _client;
            cancellationTokenSource = _cancellationTokenSource;
            receiveTask = _receiveTask;
            _client = null;
            _cancellationTokenSource = null;
            _receiveTask = null;
        }

        if (client is null)
        {
            cancellationTokenSource?.Dispose();
            return;
        }

        cancellationTokenSource?.Cancel();
        client.Dispose();
        await IgnoreTaskFailureAsync(receiveTask).ConfigureAwait(false);
        cancellationTokenSource?.Dispose();
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        StopAsync().GetAwaiter().GetResult();
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await client.ReceiveAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        ReceiveError?.Invoke(exception);
                    }

                    break;
                }

                var source = result.RemoteEndPoint;
                var receivedAt = DateTimeOffset.Now;
                var isValid = _codec.TryDecodeClear(result.Buffer, out var command);
                FrameReceived?.Invoke((byte[])result.Buffer.Clone(), source, isValid, receivedAt);
                if (isValid)
                {
                    ClearReceived?.Invoke(command, source);
                }
                else
                {
                    FrameRejected?.Invoke("外部报警清除帧校验失败。", source);
                }
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ReceiveError?.Invoke(exception);
        }
    }

    private static async Task IgnoreTaskFailureAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ExternalAlarmUdpChannel));
        }
    }
}
