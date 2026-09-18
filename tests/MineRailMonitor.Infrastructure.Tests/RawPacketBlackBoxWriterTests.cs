using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MineRailMonitor.Infrastructure.BlackBox;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class RawPacketBlackBoxWriterTests
{
    [Fact]
    public void BlackBox_writes_one_valid_json_line_per_packet()
    {
        using var directory = new TemporaryDirectory();
        var recordTime = new DateTimeOffset(2026, 9, 18, 10, 20, 30, TimeSpan.FromHours(8));
        using var writer = new RawPacketBlackBoxWriter(directory.Path);

        Assert.True(writer.TryEnqueue(CreateRecord(recordTime, "560", "TX")));
        writer.Dispose();

        var path = Path.Combine(directory.Path, "2026-09-18", "560.jsonl");
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.Single(lines);
        var json = JsonConvert.DeserializeObject<JObject>(lines[0]);
        Assert.NotNull(json);
        Assert.Equal(1, json!["schemaVersion"]!.Value<int>());
        Assert.Equal("560", json["yard"]!.Value<string>());
        Assert.Equal("127.0.0.1:62001", json["local"]!.Value<string>());
        Assert.Equal("127.0.0.1:10001", json["remote"]!.Value<string>());
        Assert.Equal("TX", json["direction"]!.Value<string>());
        foreach (var propertyName in new[]
                 {
                     "schemaVersion", "time", "direction", "yard", "stationId", "local", "remote",
                     "protocolAddress", "length", "valid", "validationError", "command", "hex"
                 })
        {
            Assert.NotNull(json[propertyName]);
        }
        Assert.Null(json["yardId"]);
        Assert.Null(json["localEndPoint"]);
        Assert.Null(json["remoteEndPoint"]);
        Assert.Equal(1, writer.GetSnapshot().WrittenCount);
    }

    [Fact]
    public void Separates_yards_in_the_same_date_partition()
    {
        using var directory = new TemporaryDirectory();
        var recordTime = new DateTimeOffset(2026, 9, 18, 10, 20, 30, TimeSpan.Zero);
        using var writer = new RawPacketBlackBoxWriter(directory.Path);

        Assert.True(writer.TryEnqueue(CreateRecord(recordTime, "560", "RX")));
        Assert.True(writer.TryEnqueue(CreateRecord(recordTime, "620", "RX")));
        writer.Dispose();

        Assert.Single(ReadJsonLines(directory.Path, "560"));
        Assert.Single(ReadJsonLines(directory.Path, "620"));
    }

    [Fact]
    public void TryEnqueue_after_dispose_returns_false_and_does_not_throw()
    {
        using var directory = new TemporaryDirectory();
        var writer = new RawPacketBlackBoxWriter(directory.Path);
        writer.Dispose();

        var exception = Record.Exception(() => Assert.False(writer.TryEnqueue(CreateRecord(DateTimeOffset.Now, "560", "RX"))));

        Assert.Null(exception);
        Assert.True(writer.GetSnapshot().DroppedCount >= 1);
    }

    [Fact]
    public async Task Bounded_queue_rejects_when_full_and_counts_the_drop()
    {
        using var directory = new TemporaryDirectory();
        var workerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWorker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var writer = new RawPacketBlackBoxWriter(
            directory.Path,
            queueCapacity: 1,
            nowProvider: () =>
            {
                workerEntered.TrySetResult(true);
                releaseWorker.Task.GetAwaiter().GetResult();
                return DateTimeOffset.Now;
            });

        await workerEntered.Task;
        Assert.True(writer.TryEnqueue(CreateRecord(DateTimeOffset.Now, "560", "RX")));
        Assert.False(writer.TryEnqueue(CreateRecord(DateTimeOffset.Now, "560", "RX")));
        Assert.Equal(1, writer.GetSnapshot().DroppedCount);

        releaseWorker.TrySetResult(true);
    }

    [Fact]
    public async Task Failed_file_write_removes_cached_writer_and_retries_later_records()
    {
        using var directory = new TemporaryDirectory();
        var recordTime = new DateTimeOffset(2026, 9, 18, 10, 20, 30, TimeSpan.Zero);
        var dateDirectory = Path.Combine(directory.Path, "2026-09-18");
        var conflictingPath = Path.Combine(dateDirectory, "560.jsonl");
        Directory.CreateDirectory(conflictingPath);
        using var writer = new RawPacketBlackBoxWriter(directory.Path);

        Assert.True(writer.TryEnqueue(CreateRecord(recordTime, "560", "TX")));
        await WaitForAsync(() => writer.GetSnapshot().DroppedCount == 1);
        Assert.NotNull(writer.GetSnapshot().LastError);

        Directory.Delete(conflictingPath);
        Assert.True(writer.TryEnqueue(CreateRecord(recordTime, "560", "RX")));
        writer.Dispose();

        var snapshot = writer.GetSnapshot();
        Assert.Equal(1, snapshot.WrittenCount);
        Assert.Equal(1, snapshot.DroppedCount);
        Assert.Null(snapshot.LastError);
        Assert.Single(File.ReadAllLines(Path.Combine(dateDirectory, "560.jsonl")));
    }

    [Fact]
    public void BlackBox_flushes_pending_records_on_dispose()
    {
        using var directory = new TemporaryDirectory();
        var recordTime = new DateTimeOffset(2026, 9, 18, 10, 20, 30, TimeSpan.Zero);
        using var writer = new RawPacketBlackBoxWriter(directory.Path);

        for (var index = 0; index < 100; index++)
        {
            Assert.True(writer.TryEnqueue(CreateRecord(recordTime, "560", "RX")));
        }

        writer.Dispose();

        var lines = ReadJsonLines(directory.Path, "560");
        Assert.Equal(100, lines.Count);
        Assert.All(lines, json => Assert.Equal(1, json["schemaVersion"]!.Value<int>()));
    }

    [Fact]
    public async Task Retention_uses_injected_clock_and_runs_again_when_clock_date_changes()
    {
        using var directory = new TemporaryDirectory();
        var today = new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
        var currentTime = today;
        var oldDirectory = Path.Combine(directory.Path, "2026-09-17");
        Directory.CreateDirectory(oldDirectory);
        File.WriteAllText(Path.Combine(oldDirectory, "560.jsonl"), "old\n");

        using var writer = new RawPacketBlackBoxWriter(
            directory.Path,
            retentionDays: 1,
            nowProvider: () => currentTime);
        Assert.True(writer.TryEnqueue(CreateRecord(today, "560", "RX")));
        await WaitForAsync(() => writer.GetSnapshot().WrittenCount == 1);
        Assert.False(Directory.Exists(oldDirectory));

        currentTime = today.AddDays(1);
        Assert.True(writer.TryEnqueue(CreateRecord(currentTime, "560", "RX")));
        writer.Dispose();

        Assert.False(Directory.Exists(Path.Combine(directory.Path, "2026-09-18")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "2026-09-19", "560.jsonl")));
    }

    [Fact]
    public void BlackBox_retention_removes_only_expired_date_directories()
    {
        using var directory = new TemporaryDirectory();
        var today = new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
        var retainedBoundary = today.AddDays(-29).ToString("yyyy-MM-dd");
        var expiredBoundary = today.AddDays(-30).ToString("yyyy-MM-dd");
        var todayDirectory = Path.Combine(directory.Path, today.ToString("yyyy-MM-dd"));
        var retainedDirectory = Path.Combine(directory.Path, retainedBoundary);
        var expiredDirectory = Path.Combine(directory.Path, expiredBoundary);
        var nonDateDirectory = Path.Combine(directory.Path, "not-a-date");
        Directory.CreateDirectory(todayDirectory);
        Directory.CreateDirectory(retainedDirectory);
        Directory.CreateDirectory(expiredDirectory);
        Directory.CreateDirectory(nonDateDirectory);
        File.WriteAllText(Path.Combine(retainedDirectory, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(expiredDirectory, "delete.txt"), "delete");
        File.WriteAllText(Path.Combine(nonDateDirectory, "keep.txt"), "keep");

        using var writer = new RawPacketBlackBoxWriter(
            directory.Path,
            retentionDays: 30,
            nowProvider: () => today);
        Assert.True(writer.TryEnqueue(CreateRecord(today, "560", "RX")));
        writer.Dispose();

        Assert.True(Directory.Exists(todayDirectory));
        Assert.True(Directory.Exists(retainedDirectory));
        Assert.False(Directory.Exists(expiredDirectory));
        Assert.True(Directory.Exists(nonDateDirectory));
    }

    [Fact]
    public async Task Snapshot_reports_running_root_and_counters()
    {
        using var directory = new TemporaryDirectory();
        using var writer = new RawPacketBlackBoxWriter(directory.Path, queueCapacity: 2);

        var running = writer.GetSnapshot();
        Assert.True(running.IsRunning);
        Assert.Equal(Path.GetFullPath(directory.Path), running.RootDirectory);
        Assert.Equal(0, running.WrittenCount);
        Assert.Equal(0, running.DroppedCount);

        Assert.True(writer.TryEnqueue(CreateRecord(DateTimeOffset.Now, "560", "RX")));
        await WaitForAsync(() => writer.GetSnapshot().WrittenCount == 1);
        Assert.Null(writer.GetSnapshot().LastError);
    }

    private static RawPacketBlackBoxRecord CreateRecord(
        DateTimeOffset time,
        string yardId,
        string direction) => new()
        {
            Time = time,
            Direction = direction,
            YardId = yardId,
            StationId = "RFID-" + yardId + "-01",
            ProtocolAddress = "01",
            LocalEndPoint = "127.0.0.1:62001",
            RemoteEndPoint = "127.0.0.1:10001",
            Length = 40,
            Hex = "B0 B0 01 04",
            Valid = true,
            Command = direction == "TX" ? "Read" : null
        };

    private static IReadOnlyList<JObject> ReadJsonLines(string rootDirectory, string yardId)
    {
        var dateDirectory = Directory.GetDirectories(rootDirectory).Single(path =>
            DateTime.TryParseExact(
                new DirectoryInfo(path).Name,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _));
        return File.ReadAllLines(Path.Combine(dateDirectory, yardId + ".jsonl"))
            .Select(line => JsonConvert.DeserializeObject<JObject>(line)!)
            .ToArray();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected black box writer state was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MineRailMonitor-BlackBox-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
