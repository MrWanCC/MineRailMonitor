namespace MineRailMonitor.Core.Tests;

public sealed class ToolTipMarkupTests
{
    [Fact]
    public void Shared_tooltip_style_prevents_default_white_popup_surface()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Cards.xaml"));

        Assert.Contains("<Style TargetType=\"{x:Type ToolTip}\">", markup, StringComparison.Ordinal);
        Assert.Contains("Background=\"{StaticResource CardBackgroundBrush}\"", markup, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{StaticResource PanelBorderBrush}\"", markup, StringComparison.Ordinal);
        Assert.Contains("TextElement.Foreground=\"{TemplateBinding Foreground}\"", markup, StringComparison.Ordinal);
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

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar.ToString(), segments));
    }
}
