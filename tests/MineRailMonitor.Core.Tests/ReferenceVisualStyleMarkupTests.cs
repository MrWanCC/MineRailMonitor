namespace MineRailMonitor.Core.Tests;

public sealed class ReferenceVisualStyleMarkupTests
{
    [Fact]
    public void Navigation_and_dashboard_headers_use_the_windows_icon_font()
    {
        var navigation = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Navigation.xaml"));
        var mainWindow = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml"));
        var monitor = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));

        Assert.Contains("SegoeFluentIconStyle", navigation);
        Assert.Contains("Property=\"FontFamily\" Value=\"Segoe Fluent Icons\"", navigation);
        Assert.Contains("Style=\"{StaticResource SegoeFluentIconStyle}\"", mainWindow);
        Assert.Contains("Style=\"{StaticResource SegoeFluentIconStyle}\"", monitor);
    }

    [Fact]
    public void Right_information_cards_have_reference_headers_and_full_width_dividers()
    {
        var cards = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Cards.xaml"));
        var monitor = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));

        Assert.Contains("RightInfoCardStyle", cards);
        Assert.Contains("RightCardHeaderStyle", cards);
        Assert.Contains("RightCardBodyFrameStyle", cards);
        Assert.Contains("RightInfoRowStyle", cards);
        Assert.Contains("Style=\"{StaticResource RightInfoCardStyle}\"", monitor);
        Assert.Contains("Style=\"{StaticResource RightCardHeaderStyle}\"", monitor);
        Assert.Contains("Style=\"{StaticResource RightCardBodyFrameStyle}\"", monitor);
        Assert.Contains("Style=\"{StaticResource RightInfoRowStyle}\"", monitor);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"12\" />", monitor);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"13\" />", monitor);
        Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"0\" />", cards);
        Assert.Contains("<RowDefinition Height=\"30\" />", monitor);
        Assert.Contains("x:Name=\"SystemStatusCard\"", monitor);
    }

    [Fact]
    public void Reference_palette_keeps_the_cyan_edges_and_deep_blue_surfaces()
    {
        var colors = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Colors.xaml"));

        Assert.Contains("Color=\"#020F1F\"", colors);
        Assert.Contains("Color=\"#03182C\"", colors);
        Assert.Contains("Color=\"#008ED6\"", colors);
        Assert.Contains("Color=\"#0A3350\"", colors);
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
