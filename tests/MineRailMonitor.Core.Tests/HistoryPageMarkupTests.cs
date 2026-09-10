namespace MineRailMonitor.Core.Tests;

public sealed class HistoryPageMarkupTests
{
    [Fact]
    public void History_page_exposes_the_required_filters_pagination_and_popup_detail_action()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml"));

        Assert.Contains("FromDateTextBox", markup, StringComparison.Ordinal);
        Assert.Contains("ToDateTextBox", markup, StringComparison.Ordinal);
        Assert.Contains("StationFilter", markup, StringComparison.Ordinal);
        Assert.Contains("HeadRfidFilter", markup, StringComparison.Ordinal);
        Assert.Contains("OutcomeFilter", markup, StringComparison.Ordinal);
        Assert.Contains("PreviousPageButton", markup, StringComparison.Ordinal);
        Assert.Contains("NextPageButton", markup, StringComparison.Ordinal);
        Assert.Contains("标准节数", markup, StringComparison.Ordinal);
        Assert.Contains("实际节数", markup, StringComparison.Ordinal);
        Assert.Contains("OnHistoryDetailsClick", markup, StringComparison.Ordinal);
        Assert.Contains("HistoryActionButtonStyle", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_initializes_sqlite_and_restores_pending_clear_before_polling()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("SqlitePassageRecordStore", markup, StringComparison.Ordinal);
        Assert.Contains("RestorePendingClear", markup, StringComparison.Ordinal);
        Assert.True(markup.IndexOf("RestorePendingClear", StringComparison.Ordinal) < markup.IndexOf("StartRfidPoller", StringComparison.Ordinal));
    }

    [Fact]
    public void History_page_data_grid_text_style_is_defined_in_shared_resources()
    {
        var sharedStyles = File.ReadAllText(Locate("src", "MineRailMonitor", "Styles", "Cards.xaml"));

        Assert.Contains("x:Key=\"DataGridTextElementStyle\"", sharedStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void History_page_station_filter_uses_stable_station_id_and_name()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml.cs"));

        Assert.Contains("Tag = station.StationId.Trim()", markup, StringComparison.Ordinal);
        Assert.Contains("Content = FormatStation(station.StationId, _stationNames)", markup, StringComparison.Ordinal);
        Assert.Contains("$\"{normalized} · {name}\"", markup, StringComparison.Ordinal);
        Assert.Contains("StationId = stationTag", markup, StringComparison.Ordinal);
        Assert.Contains("StationText = stationText", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("StationAddress = stationTag", markup, StringComparison.Ordinal);
        Assert.Contains("历史未识别基站", markup, StringComparison.Ordinal);
        Assert.Contains("PassageDetailsDialog", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void History_page_uses_explicit_datetime_filters_instead_of_date_only_controls()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "HistoryPage.xaml.cs"));

        Assert.Contains("FromDateTextBox", markup, StringComparison.Ordinal);
        Assert.Contains("ToDateTextBox", markup, StringComparison.Ordinal);
        Assert.Contains("yyyy-MM-dd HH:mm:ss", markup, StringComparison.Ordinal);
        Assert.Contains("ParseFilterDateTime", code, StringComparison.Ordinal);
        Assert.Contains("AddSeconds(1)", code, StringComparison.Ordinal);
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
