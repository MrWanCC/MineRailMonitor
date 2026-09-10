namespace MineRailMonitor.Core.Tests;

public sealed class StyledMessageDialogMarkupTests
{
    [Fact]
    public void Annotation_feedback_uses_themed_message_dialog_instead_of_system_message_box()
    {
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));
        var appCode = File.ReadAllText(Locate("src", "MineRailMonitor", "App.xaml.cs"));

        Assert.Contains("StyledMessageDialog", monitorCode);
        Assert.DoesNotContain("MessageBox.Show", monitorCode);
        Assert.Contains("StyledMessageDialog", appCode);
        Assert.DoesNotContain("MessageBox.Show", appCode);
    }

    private static string Locate(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = segments.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Unable to locate {Path.Combine(segments)} from the test directory.");
    }
}
