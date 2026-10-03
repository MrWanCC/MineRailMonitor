using System.Globalization;
using System.Text;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using Newtonsoft.Json;

namespace MineRailMonitor.Infrastructure.Logging;

public sealed class OperationRecordJsonlStore : IOperationRecordStore
{
    private readonly object _syncRoot = new();
    private readonly string _operationLogDirectory;

    public OperationRecordJsonlStore(string logsDirectory)
    {
        if (string.IsNullOrWhiteSpace(logsDirectory))
        {
            throw new ArgumentException("日志目录不能为空。", nameof(logsDirectory));
        }

        _operationLogDirectory = Path.Combine(logsDirectory, "Operation");
    }

    public void Append(OperationRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));

        try
        {
            lock (_syncRoot)
            {
                Directory.CreateDirectory(_operationLogDirectory);
                var date = record.Time.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                var path = Path.Combine(_operationLogDirectory, $"operation-{date}.jsonl");
                var json = JsonConvert.SerializeObject(record, Formatting.None);
                File.AppendAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Audit logging is best-effort and must not interrupt the primary operation.
        }
    }

    public IReadOnlyList<OperationRecord> QueryPage(int pageIndex, int pageSize)
    {
        if (pageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "页索引不能小于0。");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "每页数量必须大于0。");
        }

        var records = ReadAllRecords();
        var skip = (long)pageIndex * pageSize;
        if (skip >= records.Count)
        {
            return Array.Empty<OperationRecord>();
        }

        return records
            .OrderByDescending(item => item.Time)
            .Skip((int)skip)
            .Take(pageSize)
            .ToArray();
    }

    private List<OperationRecord> ReadAllRecords()
    {
        if (!Directory.Exists(_operationLogDirectory))
        {
            return new List<OperationRecord>();
        }

        var records = new List<OperationRecord>();
        string[] paths;
        try
        {
            paths = Directory.GetFiles(
                _operationLogDirectory,
                "operation-*.jsonl",
                SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return records;
        }
        catch (UnauthorizedAccessException)
        {
            return records;
        }

        foreach (var path in paths)
        {
            ReadFile(path, records);
        }

        return records;
    }

    private static void ReadFile(string path, ICollection<OperationRecord> records)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                try
                {
                    var record = JsonConvert.DeserializeObject<OperationRecord>(line);
                    if (record is not null)
                    {
                        records.Add(record);
                    }
                }
                catch (JsonException)
                {
                    // Ignore a malformed or partially written line and keep other records available.
                }
            }
        }
        catch (IOException)
        {
            // A log file may be rotated or temporarily unavailable while it is read.
        }
        catch (UnauthorizedAccessException)
        {
            // Continue reading other files if a single file is inaccessible.
        }
    }
}
