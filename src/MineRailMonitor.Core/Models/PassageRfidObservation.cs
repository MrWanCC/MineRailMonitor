namespace MineRailMonitor.Core.Models;

public sealed class PassageRfidObservation
{
    public PassageRfidObservation(
        int sequenceNo,
        ushort rfidValue,
        DateTimeOffset firstSeenAt,
        int batchNo)
    {
        if (sequenceNo < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNo));
        }
        if (batchNo < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchNo));
        }

        SequenceNo = sequenceNo;
        RfidValue = rfidValue;
        FirstSeenAt = firstSeenAt;
        BatchNo = batchNo;
    }

    public int SequenceNo { get; }

    public ushort RfidValue { get; }

    public DateTimeOffset FirstSeenAt { get; }

    public int BatchNo { get; }
}
