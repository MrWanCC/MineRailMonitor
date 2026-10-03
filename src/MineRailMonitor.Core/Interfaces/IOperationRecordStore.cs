using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Interfaces;

public interface IOperationRecordStore
{
    void Append(OperationRecord record);

    IReadOnlyList<OperationRecord> QueryPage(int pageIndex, int pageSize);
}
