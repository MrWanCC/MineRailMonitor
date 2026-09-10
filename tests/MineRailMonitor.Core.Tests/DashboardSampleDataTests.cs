using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Core.Tests;

public sealed class DashboardSampleDataTests
{
    [Fact]
    public void CreateForStation_ReturnsReferenceDashboardStateFor560()
    {
        var snapshot = DashboardSampleData.CreateForStation("560");

        Assert.Equal(3, snapshot.Trains.Count);
        var selectedTrain = Assert.IsType<DashboardTrain>(snapshot.SelectedTrain);
        Assert.Equal("T003", selectedTrain.TrainNumber);
        Assert.Equal("-560水平  下部股道", selectedTrain.CurrentDevice);
        Assert.Equal("报警停车", selectedTrain.Status);
        Assert.Equal(5, snapshot.Events.Count);
        Assert.Equal("报警", snapshot.Events[0].Type);
    }

    [Fact]
    public void CreateForStation_ReturnsEmptyStateForOtherStations()
    {
        var snapshot = DashboardSampleData.CreateForStation("620");

        Assert.Empty(snapshot.Trains);
        Assert.Empty(snapshot.Events);
        Assert.Null(snapshot.SelectedTrain);
    }
}
