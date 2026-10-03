using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Recognition;

public static class RfidRecognitionRules
{
    /// <summary>
    /// Returns the RFID occupying protocol slot 0. The numeric RFID value does
    /// not determine whether a tag is a head tag.
    /// </summary>
    public static ushort? GetPositionZeroRfid(RfidStationFrame frame, ushort emptyRfidValue)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (frame.RawRfidSlots is { Count: > 0 })
        {
            var positionZeroRfid = frame.RawRfidSlots[0];
            return positionZeroRfid == emptyRfidValue ? null : positionZeroRfid;
        }

        // Compatibility for callers that only provide the parser's HeadRfid
        // projection and have no raw slot array.
        return frame.HeadRfid == emptyRfidValue ? null : frame.HeadRfid;
    }

    public static RfidRecognitionAnalysis Analyze(RfidStationFrame frame, ushort emptyRfidValue)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        var sourceRfids = GetSourceRfids(frame, emptyRfidValue);
        var uniqueRfids = new List<ushort>();
        var seen = new HashSet<ushort>();
        foreach (var rfid in sourceRfids)
        {
            if (rfid == emptyRfidValue || !seen.Add(rfid))
            {
                continue;
            }

            uniqueRfids.Add(rfid);
        }

        var positionZeroRfid = GetPositionZeroRfid(frame, emptyRfidValue);
        var headRfids = positionZeroRfid.HasValue
            ? new[] { positionZeroRfid.Value }
            : Array.Empty<ushort>();
        var warnings = new List<RfidHeadWarning>();
        if (uniqueRfids.Count > 0 && headRfids.Length == 0)
        {
            warnings.Add(RfidHeadWarning.MissingHeadTag);
        }

        var firstValidRfid = uniqueRfids.Count == 0 ? (ushort?)null : uniqueRfids[0];
        if (firstValidRfid.HasValue && !headRfids.Contains(firstValidRfid.Value))
        {
            warnings.Add(RfidHeadWarning.FirstTagIsNotHead);
        }

        if (headRfids.Length > 1)
        {
            warnings.Add(RfidHeadWarning.MultipleHeadTags);
        }

        return new RfidRecognitionAnalysis(uniqueRfids, headRfids, firstValidRfid, warnings);
    }

    private static IEnumerable<ushort> GetSourceRfids(RfidStationFrame frame, ushort emptyRfidValue)
    {
        if (frame.ValidRfids is not null && frame.ValidRfids.Count > 0)
        {
            return frame.ValidRfids;
        }

        if (frame.RawRfidSlots is not null && frame.RawRfidSlots.Any(value => value != emptyRfidValue))
        {
            return frame.RawRfidSlots;
        }

        return new[] { frame.HeadRfid }.Concat(frame.WagonRfids ?? Array.Empty<ushort>());
    }
}

public sealed class RfidRecognitionAnalysis
{
    public RfidRecognitionAnalysis(
        IReadOnlyList<ushort> uniqueRfids,
        IReadOnlyList<ushort> headRfids,
        ushort? firstValidRfid,
        IReadOnlyList<RfidHeadWarning> headWarnings)
    {
        UniqueRfids = uniqueRfids;
        HeadRfids = headRfids;
        FirstValidRfid = firstValidRfid;
        HeadWarnings = headWarnings;
    }

    public IReadOnlyList<ushort> UniqueRfids { get; }

    public IReadOnlyList<ushort> HeadRfids { get; }

    public ushort? FirstValidRfid { get; }

    public IReadOnlyList<RfidHeadWarning> HeadWarnings { get; }
}
