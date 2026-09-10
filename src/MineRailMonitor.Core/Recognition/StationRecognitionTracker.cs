using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Recognition;

public sealed class StationRecognitionTracker
{
    private readonly int _expectedVehicleCount;
    private readonly TimeSpan _interVehicleTimeout;
    private readonly ushort _emptyRfidValue;
    private readonly Dictionary<byte, StationRecognitionSession> _sessions = new();

    public StationRecognitionTracker(int expectedVehicleCount, TimeSpan interVehicleTimeout, ushort emptyRfidValue = 0)
    {
        if (expectedVehicleCount < 1 || expectedVehicleCount > 14)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVehicleCount));
        }
        if (interVehicleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interVehicleTimeout));
        }
        _expectedVehicleCount = expectedVehicleCount;
        _interVehicleTimeout = interVehicleTimeout;
        _emptyRfidValue = emptyRfidValue;
    }

    public IReadOnlyDictionary<byte, StationRecognitionSession> Sessions => _sessions;

    public StationRecognitionSession Apply(RfidStationFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (!_sessions.TryGetValue(frame.StationAddress, out var session))
        {
            session = new StationRecognitionSession(frame.StationAddress, _expectedVehicleCount, _interVehicleTimeout, _emptyRfidValue);
            _sessions.Add(frame.StationAddress, session);
        }
        session.Apply(frame);
        return session;
    }

    public void Evaluate(DateTimeOffset now)
    {
        foreach (var session in _sessions.Values)
        {
            session.Evaluate(now);
        }
    }
}
