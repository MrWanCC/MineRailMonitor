using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Services;

public static class OperationRecordWriteSafety
{
    public static bool TryAppend(IOperationRecordStore store, OperationRecord record)
    {
        if (store is null || record is null)
        {
            return false;
        }

        try
        {
            store.Append(record);
            return true;
        }
        catch
        {
            // Operation logging is best-effort and must not interrupt the primary action.
            return false;
        }
    }
}
