using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class OperationRecordWriteSafetyTests
{
    [Fact]
    public void Store_failure_is_swallowed_for_the_primary_operation_caller()
    {
        var appended = OperationRecordWriteSafety.TryAppend(new FailingOperationRecordStore(), new OperationRecord
        {
            OperationType = "修改车辆配置",
            Result = "成功"
        });

        Assert.False(appended);
    }

    private sealed class FailingOperationRecordStore : IOperationRecordStore
    {
        public void Append(OperationRecord record) => throw new IOException("simulated disk failure");

        public IReadOnlyList<OperationRecord> QueryPage(int pageIndex, int pageSize) => Array.Empty<OperationRecord>();
    }
}
