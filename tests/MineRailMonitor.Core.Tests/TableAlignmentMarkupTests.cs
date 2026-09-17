namespace MineRailMonitor.Core.Tests;

public sealed class TableAlignmentMarkupTests
{
    [Fact]
    public void Station_buttons_render_a_left_aligned_icon_with_their_label()
    {
        var navigation = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Navigation.xaml"));

        Assert.Contains("StationButtonContentTemplate", navigation);
        Assert.Contains("TrainIconGeometry", navigation);
        Assert.Contains("ContentTemplate", navigation);
        Assert.Contains("HorizontalContentAlignment\" Value=\"Stretch\"", navigation);
        Assert.Contains("HorizontalAlignment=\"Left\"", navigation);
    }

    [Fact]
    public void Monitor_task_cards_use_horizontal_transport_alignment()
    {
        var cards = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Cards.xaml"));
        var monitor = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));

        Assert.Contains("HorizontalAlignment=\"Stretch\"", cards);
        Assert.Contains("HorizontalContentAlignment=\"Right\"", cards);
        Assert.Contains("OverridesDefaultStyle=\"True\"", cards);
        Assert.DoesNotContain("Width=\"28\"", cards);
        Assert.Contains("HorizontalContentAlignment\" Value=\"Center\"", cards);
        Assert.Contains("VerticalContentAlignment\" Value=\"Center\"", cards);
        Assert.Contains("x:Name=\"RfidTaskItemsControl\"", monitor);
        Assert.Contains("<StackPanel Orientation=\"Horizontal\" />", monitor);
        Assert.Contains("Text=\"{Binding ProgressText}\"", monitor);
        Assert.Contains("Text=\"车头\"", monitor);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", monitor);
    }

    [Fact]
    public void Map_and_station_preview_keep_a_visible_backdrop_behind_the_track_art()
    {
        var colors = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Colors.xaml"));
        var mapViewport = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapViewport.xaml"));
        var mainWindow = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml"));

        Assert.Contains("MapBackdropBrush", colors);
        Assert.Contains("Background=\"{StaticResource MapBackdropBrush}\"", mapViewport);
        Assert.Contains("Background=\"{StaticResource MapBackdropBrush}\"", mainWindow);
        Assert.Contains("Opacity=\"0.82\"", mapViewport);
        Assert.Contains("Opacity=\"0.82\"", mainWindow);
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
