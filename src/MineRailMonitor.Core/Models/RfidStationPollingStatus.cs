namespace MineRailMonitor.Core.Models;

public sealed class RfidStationPollingStatus
{
    public const string ResponseTimeoutError = "设备响应超时";

    private readonly object _syncRoot = new();
    private readonly Queue<DateTimeOffset> _pendingRequests = new();
    private DateTimeOffset? _lastRequestAt;
    private DateTimeOffset? _lastResponseAt;
    private long _requestCount;
    private long _responseCount;
    private long _timeoutCount;
    private long _consecutiveTimeoutCount;
    private long? _lastResponseMilliseconds;
    private DateTimeOffset? _lastErrorAt;
    private string? _lastError;
    private bool _isOnline;

    public string StationId { get; internal set; } = string.Empty;

    public RfidStationEndpointKey? EndpointKey { get; internal set; }

    public byte StationAddress { get; internal set; }

    public DateTimeOffset? LastRequestAt
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastRequestAt;
            }
        }
    }

    public DateTimeOffset? LastResponseAt
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastResponseAt;
            }
        }
    }

    public long RequestCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _requestCount;
            }
        }
    }

    public long ResponseCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _responseCount;
            }
        }
    }

    public DateTimeOffset? LastErrorAt
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastErrorAt;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastError;
            }
        }
    }

    public long SentCount => RequestCount;

    public long ReceivedCount => ResponseCount;

    public long TimeoutCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _timeoutCount;
            }
        }
    }

    public long ConsecutiveTimeoutCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _consecutiveTimeoutCount;
            }
        }
    }

    public DateTimeOffset? LastSentAt => LastRequestAt;

    public DateTimeOffset? LastReceivedAt => LastResponseAt;

    public long? LastResponseMilliseconds
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastResponseMilliseconds;
            }
        }
    }

    public bool IsOnline
    {
        get
        {
            lock (_syncRoot)
            {
                return _isOnline;
            }
        }
    }

    internal void RecordSent(DateTimeOffset sentAt)
    {
        lock (_syncRoot)
        {
            _lastRequestAt = sentAt;
            _requestCount++;
            _pendingRequests.Enqueue(sentAt);

            if (!string.Equals(_lastError, ResponseTimeoutError, StringComparison.Ordinal))
            {
                _lastError = null;
            }
        }
    }

    internal void RecordSendError(DateTimeOffset errorAt, string error)
    {
        lock (_syncRoot)
        {
            _lastErrorAt = errorAt;
            _lastError = error;
        }
    }

    internal int RecordTimeoutsIfDue(DateTimeOffset now, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        lock (_syncRoot)
        {
            var timeoutCount = 0;
            while (_pendingRequests.Count > 0 && now - _pendingRequests.Peek() >= timeout)
            {
                _pendingRequests.Dequeue();
                timeoutCount++;
            }

            if (timeoutCount == 0)
            {
                return 0;
            }

            _timeoutCount += timeoutCount;
            _consecutiveTimeoutCount += timeoutCount;
            _lastErrorAt = now;
            _lastError = ResponseTimeoutError;
            return timeoutCount;
        }
    }

    internal void RecordResponse(DateTimeOffset receivedAt)
    {
        lock (_syncRoot)
        {
            _lastResponseAt = receivedAt;
            _responseCount++;

            // The protocol has no request id. The poller sends requests in FIFO
            // order, so consume only the oldest request that could have produced
            // this response. Any later request remains pending and can still
            // reach the timeout path independently.
            DateTimeOffset? matchingRequestAt = null;
            if (_pendingRequests.Count > 0 && _pendingRequests.Peek() <= receivedAt)
            {
                matchingRequestAt = _pendingRequests.Dequeue();
            }
            else if (_pendingRequests.Count == 0 && _lastRequestAt.HasValue && receivedAt >= _lastRequestAt.Value)
            {
                // Preserve the existing behavior for a late response after its
                // request has already been timed out and removed from the queue.
                matchingRequestAt = _lastRequestAt;
            }

            if (matchingRequestAt.HasValue)
            {
                _lastResponseMilliseconds = (long)(receivedAt - matchingRequestAt.Value).TotalMilliseconds;
            }
            else
            {
                _lastResponseMilliseconds = null;
            }
            _consecutiveTimeoutCount = 0;
            _isOnline = true;
            if (string.Equals(_lastError, ResponseTimeoutError, StringComparison.Ordinal))
            {
                _lastErrorAt = null;
                _lastError = null;
            }
        }
    }

    internal void SetOnlineState(bool isOnline)
    {
        lock (_syncRoot)
        {
            _isOnline = isOnline;
        }
    }
}
