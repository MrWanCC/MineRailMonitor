using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Interfaces;

public interface IPassageEventQueryService
{
    IReadOnlyList<PassageEvent> QueryPage(int pageIndex, int pageSize);

    IReadOnlyList<PassageEvent> QueryByPassageId(Guid passageId);
}
