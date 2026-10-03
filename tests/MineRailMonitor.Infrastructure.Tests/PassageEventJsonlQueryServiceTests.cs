using MineRailMonitor.Core.Models;
using MineRailMonitor.Infrastructure.Logging;
using Newtonsoft.Json;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class PassageEventJsonlQueryServiceTests
{
    [Fact]
    public void Queries_events_newest_first_across_files_and_returns_requested_page()
    {
        var logsDirectory = Path.Combine(Path.GetTempPath(), "MineRailMonitorTests", Guid.NewGuid().ToString("N"));
        var passageDirectory = Path.Combine(logsDirectory, "Passage");
        Directory.CreateDirectory(passageDirectory);
        try
        {
            WriteEvents(passageDirectory, "passage-event-20261001.jsonl",
                CreateEvent("older", new DateTimeOffset(2026, 10, 1, 23, 59, 0, TimeSpan.Zero)));
            WriteEvents(passageDirectory, "passage-event-20261002.jsonl",
                CreateEvent("middle", new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero)),
                CreateEvent("newest", new DateTimeOffset(2026, 10, 2, 10, 1, 0, TimeSpan.Zero)));

            var service = new PassageEventJsonlQueryService(logsDirectory);

            Assert.Equal(new[] { "newest", "middle" }, service.QueryPage(0, 2).Select(item => item.Reason));
            Assert.Equal(new[] { "older" }, service.QueryPage(1, 2).Select(item => item.Reason));
        }
        finally
        {
            if (Directory.Exists(logsDirectory))
            {
                Directory.Delete(logsDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Skips_malformed_lines_and_rejects_invalid_page_arguments()
    {
        var logsDirectory = Path.Combine(Path.GetTempPath(), "MineRailMonitorTests", Guid.NewGuid().ToString("N"));
        var passageDirectory = Path.Combine(logsDirectory, "Passage");
        Directory.CreateDirectory(passageDirectory);
        try
        {
            File.WriteAllLines(Path.Combine(passageDirectory, "passage-event-20261002.jsonl"), new[]
            {
                "{not-json}",
                JsonConvert.SerializeObject(CreateEvent("valid", new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero)))
            });
            var service = new PassageEventJsonlQueryService(logsDirectory);

            Assert.Equal("valid", Assert.Single(service.QueryPage(0, 10)).Reason);
            Assert.Throws<ArgumentOutOfRangeException>(() => service.QueryPage(-1, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => service.QueryPage(0, 0));
        }
        finally
        {
            if (Directory.Exists(logsDirectory))
            {
                Directory.Delete(logsDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Queries_one_passage_in_ascending_event_time_order()
    {
        var logsDirectory = Path.Combine(Path.GetTempPath(), "MineRailMonitorTests", Guid.NewGuid().ToString("N"));
        var passageDirectory = Path.Combine(logsDirectory, "Passage");
        var passageId = Guid.NewGuid();
        Directory.CreateDirectory(passageDirectory);
        try
        {
            WriteEvents(passageDirectory, "passage-event-20261002.jsonl",
                CreateEvent(passageId, "alarm", new DateTimeOffset(2026, 10, 2, 10, 0, 35, TimeSpan.Zero)),
                CreateEvent(Guid.NewGuid(), "other", new DateTimeOffset(2026, 10, 2, 10, 0, 2, TimeSpan.Zero)),
                CreateEvent(passageId, "start", new DateTimeOffset(2026, 10, 2, 10, 0, 1, TimeSpan.Zero)),
                CreateEvent(passageId, "progress", new DateTimeOffset(2026, 10, 2, 10, 0, 5, TimeSpan.Zero)));
            var service = new PassageEventJsonlQueryService(logsDirectory);

            Assert.Equal(new[] { "start", "progress", "alarm" }, service.QueryByPassageId(passageId).Select(item => item.Reason));
        }
        finally
        {
            if (Directory.Exists(logsDirectory))
            {
                Directory.Delete(logsDirectory, recursive: true);
            }
        }
    }

    private static PassageEvent CreateEvent(string id, DateTimeOffset occurredAt) => CreateEvent(Guid.NewGuid(), id, occurredAt);

    private static PassageEvent CreateEvent(Guid passageId, string id, DateTimeOffset occurredAt) => new()
    {
        PassageId = passageId,
        EventType = "PASSAGE_PROGRESS",
        OccurredAt = occurredAt,
        Reason = id
    };

    private static void WriteEvents(string directory, string fileName, params PassageEvent[] events) =>
        File.WriteAllLines(Path.Combine(directory, fileName), events.Select(JsonConvert.SerializeObject));
}
