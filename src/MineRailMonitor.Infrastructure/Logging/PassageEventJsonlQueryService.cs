using System.Text;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using Newtonsoft.Json;

namespace MineRailMonitor.Infrastructure.Logging;

public sealed class PassageEventJsonlQueryService : IPassageEventQueryService
{
    private readonly string _passageLogDirectory;

    public PassageEventJsonlQueryService(string logsDirectory)
    {
        if (string.IsNullOrWhiteSpace(logsDirectory))
        {
            throw new ArgumentException("日志目录不能为空。", nameof(logsDirectory));
        }

        _passageLogDirectory = Path.Combine(logsDirectory, "Passage");
    }

    public IReadOnlyList<PassageEvent> QueryPage(int pageIndex, int pageSize)
    {
        if (pageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "页索引不能小于0。");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "每页数量必须大于0。");
        }

        if (!Directory.Exists(_passageLogDirectory))
        {
            return Array.Empty<PassageEvent>();
        }

        var events = ReadAllEvents();
        var skip = (long)pageIndex * pageSize;
        if (skip >= events.Count)
        {
            return Array.Empty<PassageEvent>();
        }

        return events
            .OrderByDescending(item => item.OccurredAt)
            .Skip((int)skip)
            .Take(pageSize)
            .ToArray();
    }

    public IReadOnlyList<PassageEvent> QueryByPassageId(Guid passageId) => ReadAllEvents()
        .Where(item => item.PassageId == passageId)
        .OrderBy(item => item.OccurredAt)
        .ToArray();

    private List<PassageEvent> ReadAllEvents()
    {
        if (!Directory.Exists(_passageLogDirectory))
        {
            return new List<PassageEvent>();
        }

        var events = new List<PassageEvent>();
        foreach (var path in Directory.GetFiles(_passageLogDirectory, "passage-event-*.jsonl", SearchOption.TopDirectoryOnly))
        {
            ReadFile(path, events);
        }
        return events;
    }

    private static void ReadFile(string path, ICollection<PassageEvent> events)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                try
                {
                    var passageEvent = JsonConvert.DeserializeObject<PassageEvent>(line);
                    if (passageEvent is not null)
                    {
                        events.Add(passageEvent);
                    }
                }
                catch (JsonException)
                {
                    // A partially written or malformed line should not hide other event records.
                }
            }
        }
        catch (IOException)
        {
            // A log file can be rotated or temporarily unavailable while it is being read.
        }
        catch (UnauthorizedAccessException)
        {
            // Continue reading other files when a single file is inaccessible.
        }
    }
}
