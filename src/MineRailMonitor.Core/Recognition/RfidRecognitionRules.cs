using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Recognition;

public static class RfidRecognitionRules
{
    public const ushort HeadRfidMin = 0x0001;
    public const ushort HeadRfidMax = 0x000A;

    public static bool IsHeadRfid(ushort value) => value >= HeadRfidMin && value <= HeadRfidMax;

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

        var headRfids = uniqueRfids.Where(IsHeadRfid).ToArray();
        var warnings = new List<RfidHeadWarning>();
        if (uniqueRfids.Count > 0 && headRfids.Length == 0)
        {
            warnings.Add(RfidHeadWarning.MissingHeadTag);
        }

        var firstValidRfid = uniqueRfids.Count == 0 ? (ushort?)null : uniqueRfids[0];
        if (firstValidRfid.HasValue && !IsHeadRfid(firstValidRfid.Value))
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
