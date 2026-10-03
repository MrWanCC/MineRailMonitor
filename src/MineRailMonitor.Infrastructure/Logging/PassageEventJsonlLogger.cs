using System.Globalization;
using System.Text;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using Newtonsoft.Json;

namespace MineRailMonitor.Infrastructure.Logging;

public sealed class PassageEventJsonlLogger : IPassageEventLogger
{
    private readonly object _syncRoot = new();
    private readonly string _passageLogDirectory;

    public PassageEventJsonlLogger(string logsDirectory)
    {
        if (string.IsNullOrWhiteSpace(logsDirectory))
        {
            throw new ArgumentException("日志目录不能为空。", nameof(logsDirectory));
        }

        _passageLogDirectory = Path.Combine(logsDirectory, "Passage");
    }

    public void Write(PassageEvent passageEvent)
    {
        if (passageEvent is null) throw new ArgumentNullException(nameof(passageEvent));

        try
        {
            lock (_syncRoot)
            {
                Directory.CreateDirectory(_passageLogDirectory);
                var date = passageEvent.OccurredAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                var fileName = $"passage-event-{date}.jsonl";
                var path = Path.Combine(_passageLogDirectory, fileName);
                var json = JsonConvert.SerializeObject(passageEvent, Formatting.None);
                File.AppendAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Business event logging must not interrupt RFID recognition.
        }
    }
}
