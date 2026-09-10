namespace MineRailMonitor.Core.Tests;

public sealed class SettingsPageMarkupTests
{
    [Fact]
    public void Settings_markup_exposes_all_station_endpoint_fields_and_row_controls()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("基站编号", markup);
        Assert.Contains("基站名称", markup);
        Assert.Contains("IP地址", markup);
        Assert.Contains("端口", markup);
        Assert.Contains("协议地址", markup);
        Assert.Contains("启用状态", markup);
        Assert.Contains("新增基站", markup);
        Assert.Contains("移除", markup);
    }

    private static string LocateSourceFile(params string[] segments)
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

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar.ToString(), segments));
    }
}
