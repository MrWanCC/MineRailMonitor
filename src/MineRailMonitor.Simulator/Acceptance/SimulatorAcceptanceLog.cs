using MineRailMonitor.Core.Communication;

namespace MineRailMonitor.Simulator.Acceptance;

public sealed class SimulatorRequestLog
{
    public DateTimeOffset ReceivedAt { get; set; }

    public byte StationAddress { get; set; }

    public RfidPollCommand Command { get; set; }

    public int FrameLength { get; set; }

    public string FrameHex { get; set; } = string.Empty;
}

public sealed class SimulatorResponseLog
{
    public DateTimeOffset SentAt { get; set; }

    public byte StationAddress { get; set; }

    public int FrameLength { get; set; }

    public byte ReportedCardCount { get; set; }

    public bool IsEmpty { get; set; }

    public IReadOnlyList<ushort> Slots { get; set; } = Array.Empty<ushort>();
}

public sealed class SimulatorStationSnapshot
{
    public byte StationAddress { get; set; }

    public IReadOnlyList<ushort> Slots { get; set; } = Array.Empty<ushort>();
}

public sealed class SimulatorScenarioResult
{
    public string Scenario { get; set; } = string.Empty;

    public bool Pass { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public long DurationMs { get; set; }

    public string? FailureReason { get; set; }

    public IReadOnlyList<SimulatorRequestLog> Requests { get; set; } = Array.Empty<SimulatorRequestLog>();

    public IReadOnlyList<SimulatorResponseLog> Responses { get; set; } = Array.Empty<SimulatorResponseLog>();

    public IReadOnlyList<SimulatorStationSnapshot> FinalStations { get; set; } = Array.Empty<SimulatorStationSnapshot>();
}
