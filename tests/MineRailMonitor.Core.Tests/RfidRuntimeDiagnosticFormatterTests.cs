using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class RfidRuntimeDiagnosticFormatterTests
{
    [Theory]
    [InlineData("STATE_TRANSITION")]
    [InlineData("CLEAR_COMMAND_SENT")]
    [InlineData("EMPTY_CONFIRMATION")]
    [InlineData("EMPTY_CONFIRMATION_RESET")]
    [InlineData("NON_EMPTY_FRAME_AFTER_TERMINAL_STATE")]
    public void Critical_lifecycle_diagnostics_remain_available_when_verbose_frames_are_off(string eventType)
    {
        Assert.False(RfidFrameTextLoggingPolicy.ShouldWriteVerboseFrameLogs(
            acceptanceMode: false,
            debugBuild: false,
            explicitlyEnabled: false));

        var formatterType = typeof(RfidReceiveDiagnosticFormatter).Assembly.GetType(
            "MineRailMonitor.Core.Communication.RfidRuntimeDiagnosticFormatter");
        Assert.NotNull(formatterType);
        var formatMethod = formatterType!.GetMethod("FormatLifecycle");
        Assert.NotNull(formatMethod);

        var occurredAt = new DateTimeOffset(2026, 10, 3, 12, 34, 56, 789, TimeSpan.FromHours(8));
        var result = (string)formatMethod!.Invoke(null, new object?[]
        {
            "RFID-01",
            Guid.Parse("e7f126b5-68b8-4d66-a9de-b1ab458487f2"),
            occurredAt,
            eventType,
            PassageLifecycleState.WaitForEmpty,
            11,
            10,
            occurredAt.AddSeconds(-1),
            1,
            1,
            2,
            "diagnostic-test"
        })!;

        Assert.Contains($"Event={eventType}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Lifecycle_diagnostic_includes_station_passage_state_timing_and_clear_progress()
    {
        var formatterType = typeof(RfidReceiveDiagnosticFormatter).Assembly.GetType(
            "MineRailMonitor.Core.Communication.RfidRuntimeDiagnosticFormatter");
        Assert.NotNull(formatterType);
        var formatMethod = formatterType!.GetMethod("FormatLifecycle");
        Assert.NotNull(formatMethod);

        var occurredAt = new DateTimeOffset(2026, 10, 3, 12, 34, 56, 789, TimeSpan.FromHours(8));
        var lastNewVehicleAt = occurredAt.AddSeconds(-1);
        var passageId = Guid.Parse("e7f126b5-68b8-4d66-a9de-b1ab458487f2");
        var result = (string)formatMethod!.Invoke(null, new object?[]
        {
            "RFID-01",
            passageId,
            occurredAt,
            "EMPTY_CONFIRMATION",
            PassageLifecycleState.WaitForEmpty,
            11,
            10,
            lastNewVehicleAt,
            1,
            1,
            2,
            "Count=1/2"
        })!;

        Assert.Contains("Time=2026-10-03T12:34:56.7890000+08:00", result, StringComparison.Ordinal);
        Assert.Contains("StationId=RFID-01", result, StringComparison.Ordinal);
        Assert.Contains($"PassageId={passageId:D}", result, StringComparison.Ordinal);
        Assert.Contains("Event=EMPTY_CONFIRMATION", result, StringComparison.Ordinal);
        Assert.Contains("State=WaitForEmpty", result, StringComparison.Ordinal);
        Assert.Contains("Expected=11 Detected=10", result, StringComparison.Ordinal);
        Assert.Contains("LastNewVehicleAt=2026-10-03T12:34:55.7890000+08:00", result, StringComparison.Ordinal);
        Assert.Contains("ClearAttempts=1 EmptyConfirm=1/2", result, StringComparison.Ordinal);
    }
}
