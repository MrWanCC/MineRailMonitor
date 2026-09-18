using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace MineRailMonitor.Infrastructure.BlackBox;

public sealed class RawPacketBlackBoxWriter : IDisposable
{
    public const int DefaultQueueCapacity = 10000;
    public const int DefaultRetentionDays = 30;

    private readonly object _syncRoot = new();
    private readonly BlockingCollection<RawPacketBlackBoxRecord> _queue;
    private readonly Dictionary<string, CachedWriter> _writers = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _retentionDays;
    private readonly Func<DateTimeOffset> _nowProvider;
    private readonly Task _worker;
    private long _writtenCount;
    private long _droppedCount;
    private int _isRunning = 1;
    private int _disposeStarted;
    private DateTime? _lastPurgeDate;
    private string? _lastError;

    public RawPacketBlackBoxWriter(
        string rootDirectory,
        int queueCapacity = DefaultQueueCapacity,
        int retentionDays = DefaultRetentionDays,
        Func<DateTimeOffset>? nowProvider = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("黑匣子根目录不能为空。", nameof(rootDirectory));
        }
        if (queueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        }
        if (retentionDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays));
        }

        RootDirectory = Path.GetFullPath(rootDirectory);
        _retentionDays = retentionDays;
        _nowProvider = nowProvider ?? (() => DateTimeOffset.Now);
        _queue = new BlockingCollection<RawPacketBlackBoxRecord>(
            new ConcurrentQueue<RawPacketBlackBoxRecord>(),
            queueCapacity);
        _worker = Task.Factory.StartNew(
            ConsumeLoop,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public string RootDirectory { get; }

    public bool TryEnqueue(RawPacketBlackBoxRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));

        try
        {
            if (_queue.TryAdd(record))
            {
                return true;
            }
        }
        catch (ObjectDisposedException)
        {
            RecordDrop("黑匣子写入器已释放。");
            return false;
        }
        catch (InvalidOperationException)
        {
            RecordDrop("黑匣子写入器已停止接收新记录。");
            return false;
        }

        RecordDrop("黑匣子写入队列已满。");
        return false;
    }

    public RawPacketBlackBoxStatus GetSnapshot()
    {
        string? lastError;
        lock (_syncRoot)
        {
            lastError = _lastError;
        }

        return new RawPacketBlackBoxStatus(
            Volatile.Read(ref _isRunning) == 1,
            Interlocked.Read(ref _writtenCount),
            Interlocked.Read(ref _droppedCount),
            lastError,
            RootDirectory);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        try
        {
            _queue.CompleteAdding();
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            _worker.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            SetLastError(exception.Message);
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }

    private void ConsumeLoop()
    {
        try
        {
            PurgeIfNeeded(force: true);
            foreach (var record in _queue.GetConsumingEnumerable())
            {
                ProcessRecord(record);
            }
        }
        catch (Exception exception)
        {
            SetLastError(GetErrorMessage(exception));
        }
        finally
        {
            CloseAllWriters();
            Volatile.Write(ref _isRunning, 0);
        }
    }

    private void ProcessRecord(RawPacketBlackBoxRecord record)
    {
        try
        {
            PurgeIfNeeded(force: false);
            var date = record.Time.Date;
            var yardKey = SanitizeYardId(record.YardId);
            var writer = GetOrCreateWriter(yardKey, date);
            var json = JsonConvert.SerializeObject(record, Formatting.None);
            writer.Writer.WriteLine(json);
            Interlocked.Increment(ref _writtenCount);
            ClearLastError();
        }
        catch (Exception exception)
        {
            RemoveWriterAfterFailure(record.YardId, exception);
        }
    }

    private CachedWriter GetOrCreateWriter(string yardKey, DateTime date)
    {
        lock (_syncRoot)
        {
            if (_writers.TryGetValue(yardKey, out var existing) && existing.Date == date)
            {
                return existing;
            }

            if (existing is not null)
            {
                _writers.Remove(yardKey);
                CloseWriter(existing);
            }

            var dateDirectory = Path.Combine(RootDirectory, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dateDirectory);
            var filePath = Path.Combine(dateDirectory, yardKey + ".jsonl");
            var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: false)
            {
                AutoFlush = true
            };
            var cached = new CachedWriter(date, writer);
            _writers[yardKey] = cached;
            return cached;
        }
    }

    private void RemoveWriterAfterFailure(string yardId, Exception exception)
    {
        var yardKey = SanitizeYardId(yardId);
        lock (_syncRoot)
        {
            if (_writers.TryGetValue(yardKey, out var writer))
            {
                _writers.Remove(yardKey);
                CloseWriter(writer);
            }
        }

        Interlocked.Increment(ref _droppedCount);
        SetLastError(GetErrorMessage(exception));
    }

    private void PurgeIfNeeded(bool force)
    {
        DateTimeOffset now;
        try
        {
            now = _nowProvider();
        }
        catch (Exception exception)
        {
            SetLastError(GetErrorMessage(exception));
            return;
        }

        var today = now.Date;
        if (!force && _lastPurgeDate == today)
        {
            return;
        }

        if (_lastPurgeDate.HasValue && _lastPurgeDate.Value != today)
        {
            CloseAllWriters();
        }
        _lastPurgeDate = today;

        try
        {
            Directory.CreateDirectory(RootDirectory);
            var cutoff = today.AddDays(-(_retentionDays - 1));
            foreach (var directory in Directory.GetDirectories(RootDirectory))
            {
                var name = new DirectoryInfo(directory).Name;
                if (!DateTime.TryParseExact(
                        name,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date) ||
                    date >= cutoff)
                {
                    continue;
                }

                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception)
        {
            SetLastError(GetErrorMessage(exception));
        }
    }

    private void CloseAllWriters()
    {
        lock (_syncRoot)
        {
            foreach (var writer in _writers.Values.ToArray())
            {
                CloseWriter(writer);
            }
            _writers.Clear();
        }
    }

    private static void CloseWriter(CachedWriter writer)
    {
        try
        {
            writer.Writer.Flush();
        }
        catch
        {
        }

        try
        {
            writer.Writer.Dispose();
        }
        catch
        {
        }
    }

    private void RecordDrop(string message)
    {
        Interlocked.Increment(ref _droppedCount);
        SetLastError(message);
    }

    private void SetLastError(string message)
    {
        lock (_syncRoot)
        {
            _lastError = message;
        }
    }

    private void ClearLastError()
    {
        lock (_syncRoot)
        {
            _lastError = null;
        }
    }

    private static string SanitizeYardId(string? yardId)
    {
        var value = string.IsNullOrWhiteSpace(yardId) ? "UnknownYard" : yardId!.Trim();
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) || character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar
                ? '_'
                : character);
        }

        var sanitized = builder.ToString().Trim();
        return sanitized is "." or ".." or "" ? "UnknownYard" : sanitized;
    }

    private static string GetErrorMessage(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

    private sealed class CachedWriter
    {
        public CachedWriter(DateTime date, StreamWriter writer)
        {
            Date = date;
            Writer = writer;
        }

        public DateTime Date { get; }

        public StreamWriter Writer { get; }
    }
}
