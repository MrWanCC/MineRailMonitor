using System.Globalization;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Communication;

/// <summary>
/// Formats low-volume lifecycle diagnostics without participating in RFID recognition.
/// </summary>
public static class RfidRuntimeDiagnosticFormatter
{
    public static string FormatLifecycle(
        string stationId,
        Guid? passageId,
        DateTimeOffset occurredAt,
        string eventType,
        PassageLifecycleState lifecycleState,
        int? expectedCount,
        int detectedCount,
        DateTimeOffset? lastNewVehicleAt,
        int clearAttempts,
        int consecutiveEmptyReads,
        int emptyConfirmationReads,
        string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(stationId)) throw new ArgumentException("StationId must not be empty.", nameof(stationId));
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("Event type must not be empty.", nameof(eventType));
        if (emptyConfirmationReads < 1) throw new ArgumentOutOfRangeException(nameof(emptyConfirmationReads));

        var passageText = passageId?.ToString("D") ?? "-";
        var lastNewVehicleText = lastNewVehicleAt?.ToString("O", CultureInfo.InvariantCulture) ?? "-";
        return $"[RFIDLifecycle] Time={occurredAt.ToString("O", CultureInfo.InvariantCulture)} " +
               $"StationId={stationId} PassageId={passageText} Event={eventType} State={lifecycleState} " +
               $"Expected={expectedCount?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
               $"Detected={detectedCount.ToString(CultureInfo.InvariantCulture)} " +
               $"LastNewVehicleAt={lastNewVehicleText} " +
               $"ClearAttempts={clearAttempts.ToString(CultureInfo.InvariantCulture)} " +
               $"EmptyConfirm={consecutiveEmptyReads.ToString(CultureInfo.InvariantCulture)}/{emptyConfirmationReads.ToString(CultureInfo.InvariantCulture)} " +
               $"Detail={detail ?? "-"}";
    }
}
