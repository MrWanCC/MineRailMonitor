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
        Assert.Contains("ItemsSource=\"{Binding StationIdOptions}\"", markup);
        Assert.Contains("SelectedItem=\"{Binding StationId, UpdateSourceTrigger=PropertyChanged}\"", markup);
        Assert.Contains("IsEditable=\"False\"", markup);
        Assert.Contains("设备协议地址（Byte2）", markup);
        Assert.Contains("IsReadOnly=\"True\"", markup);
        Assert.Contains("Grid.Column=\"6\"", markup);
        Assert.Contains("Content=\"启用\"", markup);
        Assert.Contains("SettingsToggleStyle", markup);
        var toggleStart = markup.IndexOf("<CheckBox Grid.Column=\"6\"", StringComparison.Ordinal);
        Assert.True(toggleStart >= 0);
        var toggleEnd = markup.IndexOf(" />", toggleStart, StringComparison.Ordinal);
        Assert.True(toggleEnd > toggleStart);
        var toggleMarkup = markup.Substring(toggleStart, toggleEnd - toggleStart + 3);
        Assert.Contains("HorizontalAlignment=\"Left\"", toggleMarkup);
        Assert.Contains("Margin=\"12,0,0,0\"", toggleMarkup);
        Assert.DoesNotContain("<TextBlock Text=\"基站编号\" Style=\"{StaticResource SettingsTableCellLabelStyle}\"", markup);
        Assert.DoesNotContain("<TextBlock Text=\"基站名称\" Style=\"{StaticResource SettingsTableCellLabelStyle}\"", markup);
        Assert.DoesNotContain("<TextBlock Text=\"IP地址\" Style=\"{StaticResource SettingsTableCellLabelStyle}\"", markup);
        Assert.DoesNotContain("<TextBlock Text=\"端口\" Style=\"{StaticResource SettingsTableCellLabelStyle}\"", markup);
        Assert.DoesNotContain("<TextBlock Text=\"协议地址\" Style=\"{StaticResource SettingsTableCellLabelStyle}\"", markup);
    }

    [Fact]
    public void Settings_section_icons_reserve_a_fixed_column_before_the_titles()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("<ColumnDefinition Width=\"52\" />", markup, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"48\" />", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"&#xE713;\" Style=\"{StaticResource SegoeFluentIconStyle}\" Foreground=\"{StaticResource PrimaryBlueBrush}\" FontSize=\"38\" Width=\"42\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"&#xE701;\" Style=\"{StaticResource SegoeFluentIconStyle}\" Foreground=\"{StaticResource PrimaryBlueBrush}\" FontSize=\"35\" Width=\"40\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_only_the_station_editor_uses_a_nested_scroll_viewer()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("<ScrollViewer VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Disabled\">", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer Grid.Row=\"1\" VerticalScrollBarVisibility=\"Auto\"", markup, StringComparison.Ordinal);
        Assert.Contains("<Border Grid.Row=\"1\" Height=\"300\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"360\"", markup, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"*\" />", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_breadcrumb_starts_on_the_same_content_inset_as_the_sections()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("<Grid Grid.Row=\"0\" Margin=\"16,0,0,18\">", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_keeps_the_station_scroll_enabled_when_editing_is_locked()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("xmlns:local=\"clr-namespace:MineRailMonitor.Pages\"", markup, StringComparison.Ordinal);
        Assert.Contains("StationEditingEnabled", markup, StringComparison.Ordinal);
        Assert.Contains("StationEditorPanel.IsEnabled = true", code, StringComparison.Ordinal);
        Assert.DoesNotContain("StationEditorPanel.IsEnabled = _stationsEditable", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_tracks_unsaved_drafts_and_provides_a_leave_confirmation()
    {
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));
        var dialog = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "UnsavedSettingsChangesDialog.xaml.cs"));

        Assert.Contains("HasUnsavedChanges", code, StringComparison.Ordinal);
        Assert.Contains("TryLeaveAsync", code, StringComparison.Ordinal);
        Assert.Contains("UnsavedSettingsChangesDialog", code, StringComparison.Ordinal);
        Assert.Contains("SaveAndContinue", dialog, StringComparison.Ordinal);
        Assert.Contains("Discard", dialog, StringComparison.Ordinal);
        Assert.Contains("Cancel", dialog, StringComparison.Ordinal);
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
