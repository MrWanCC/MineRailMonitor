using System.Net;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class Phase32SqliteIntegrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Normal_passage_is_saved_pending_then_cleared_in_real_sqlite()
    {
        using var database = new TemporaryDatabase();
        var coordinator = CreateCoordinator(database.Store);
        var values = CreateElevenVehicleValues();

        coordinator.ProcessFrame(CreateFrame(0x01, Start, values));
        Assert.Equal(PassageClearState.PendingClear, Assert.Single(database.Store.Records).ClearState);
        Assert.Equal(RfidPollCommand.Clear, coordinator.GetCommand(0x01));

        coordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddMilliseconds(200));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(1), Array.Empty<ushort>()));
        coordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(2), Array.Empty<ushort>()));

        var record = Assert.Single(database.Store.Records);
        Assert.Equal(PassageClearState.Cleared, record.ClearState);
        Assert.Equal(11, record.RfidObservations.Count);
    }

    [Fact]
    public void Pending_clear_survives_restart_and_does_not_create_a_duplicate_passage()
    {
        using var database = new TemporaryDatabase();
        var firstCoordinator = CreateCoordinator(database.Store);
        firstCoordinator.ProcessFrame(CreateFrame(0x01, Start, CreateElevenVehicleValues()));
        var originalId = Assert.Single(database.Store.Records).PassageId;
        database.Store.Dispose();

        using var restartedStore = new SqlitePassageRecordStore(database.Path);
        var restartedCoordinator = CreateCoordinator(restartedStore);
        restartedCoordinator.RestorePendingClear(restartedStore.GetPendingClear());

        var restoredState = restartedCoordinator.States[0x01];
        Assert.Equal(PassageLifecycleState.Clearing, restoredState.LifecycleState);
        Assert.Equal(originalId, restoredState.LastPassageRecord!.PassageId);
        Assert.Single(restartedStore.Records);

        restartedCoordinator.MarkCommandSent(0x01, RfidPollCommand.Clear, Start.AddSeconds(1));
        restartedCoordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(2), Array.Empty<ushort>()));
        restartedCoordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(3), Array.Empty<ushort>()));

        Assert.Equal(PassageLifecycleState.Idle, restartedCoordinator.States[0x01].LifecycleState);
        Assert.Single(restartedStore.Records);
        Assert.Equal(PassageClearState.Cleared, restartedStore.GetDetails(originalId)!.ClearState);
    }

    [Fact]
    public void Pending_clear_is_marked_cleared_after_restart_when_hardware_is_already_empty()
    {
        using var database = new TemporaryDatabase();
        var firstCoordinator = CreateCoordinator(database.Store);
        firstCoordinator.ProcessFrame(CreateFrame(0x01, Start, CreateElevenVehicleValues()));
        var originalId = Assert.Single(database.Store.Records).PassageId;
        database.Store.Dispose();

        using var restartedStore = new SqlitePassageRecordStore(database.Path);
        var restartedCoordinator = CreateCoordinator(restartedStore);
        restartedCoordinator.RestorePendingClear(restartedStore.GetPendingClear());

        restartedCoordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(1), Array.Empty<ushort>()));
        restartedCoordinator.ProcessFrame(CreateFrame(0x01, Start.AddSeconds(2), Array.Empty<ushort>()));

        Assert.Equal(PassageLifecycleState.Idle, restartedCoordinator.States[0x01].LifecycleState);
        Assert.Single(restartedStore.Records);
        Assert.Equal(PassageClearState.Cleared, restartedStore.GetDetails(originalId)!.ClearState);
    }

    private static RfidRuntimeCoordinator CreateCoordinator(SqlitePassageRecordStore store) =>
        new(
            new byte[] { 0x01 },
            new RfidSettings { ExpectedVehicleCount = 11, InterVehicleTimeoutSeconds = 30 },
            store,
            new RfidRuntimePolicy(TimeSpan.FromSeconds(5), 3, 2, 3));

    private static ushort[] CreateElevenVehicleValues() =>
        Enumerable.Range(0, 11)
            .Select(index => (ushort)(index == 0 ? 0x0001 : 0x0011 + index - 1))
            .ToArray();

    private static RfidStationFrame CreateFrame(byte address, DateTimeOffset at, IReadOnlyList<ushort> values)
    {
        var slots = new ushort[14];
        values.Take(14).ToArray().CopyTo(slots, 0);
        return new RfidStationFrame
        {
            StationAddress = address,
            Mode = 0x04,
            HeadRfid = slots[0],
            RawRfidSlots = slots,
            ValidRfids = slots.Where(value => value != 0).ToArray(),
            ReportedCardCount = (byte)values.Count,
            ActualNonZeroSlotCount = values.Count,
            ReceivedAt = at,
            SourceEndpoint = new IPEndPoint(IPAddress.Loopback, 62001)
        };
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "passages.db");
            Store = new SqlitePassageRecordStore(Path);
        }

        public string Path { get; }

        public SqlitePassageRecordStore Store { get; }

        public void Dispose()
        {
            Store.Dispose();
            foreach (var file in Directory.EnumerateFiles(_directory))
            {
                File.Delete(file);
            }
            Directory.Delete(_directory);
        }
    }
}
