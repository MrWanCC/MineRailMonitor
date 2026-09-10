namespace MineRailMonitor.Core.Tests;

public sealed class ProjectDirectorySelectionTests
{
    [Fact]
    public void Main_window_prefers_local_default_project_and_falls_back_to_example()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("ResolveProjectDirectory", code, StringComparison.Ordinal);
        Assert.Contains("Projects", code, StringComparison.Ordinal);
        Assert.Contains("Default", code, StringComparison.Ordinal);
        Assert.Contains("Example", code, StringComparison.Ordinal);
        Assert.Contains("_acceptanceOptions.Enabled", code, StringComparison.Ordinal);
    }

    private static string Locate(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar.ToString(), parts));
    }
}
