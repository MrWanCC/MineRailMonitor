namespace MineRailMonitor.Core.Tests;

public sealed class ReferenceVisualStyleMarkupTests
{
    [Fact]
    public void Application_icons_are_project_owned_vector_resources()
    {
        var icons = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Icons.xaml"));
        var navigation = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Navigation.xaml"));
        var mainWindow = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml"));
        var monitor = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var settings = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("<Style x:Key=\"ProjectIconPathStyle\" TargetType=\"Path\">", navigation);
        Assert.Contains("<Geometry x:Key=\"MonitorIconGeometry\">", icons);
        Assert.Contains("<Geometry x:Key=\"SettingsIconGeometry\">", icons);
        Assert.Contains("Data=\"{StaticResource MonitorIconGeometry}\"", mainWindow);
        Assert.Contains("Data=\"{StaticResource SettingsIconGeometry}\"", settings);

        Assert.DoesNotContain("Segoe Fluent Icons", navigation);
        Assert.DoesNotContain("Segoe Fluent Icons", mainWindow);
        Assert.DoesNotContain("Segoe Fluent Icons", monitor);
        Assert.DoesNotContain("Segoe Fluent Icons", settings);
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
        Assert.Contains("<RowDefinition Height=\"42\" />", monitor);
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
