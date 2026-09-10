using MineRailMonitor.Core.Acceptance;

namespace MineRailMonitor.Core.Tests;

public sealed class AtomicFileWriterTests
{
    [Fact]
    public void WriteAllText_replaces_previous_snapshot_and_leaves_no_temp_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "runtime-state.json");

        try
        {
            AtomicFileWriter.WriteAllText(path, "{\"state\":\"old\"}");
            AtomicFileWriter.WriteAllText(path, "{\"state\":\"new\"}");

            Assert.Equal("{\"state\":\"new\"}", File.ReadAllText(path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
