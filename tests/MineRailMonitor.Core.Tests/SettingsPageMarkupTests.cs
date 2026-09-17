namespace MineRailMonitor.Core.Tests;

public sealed class SettingsPageMarkupTests
{
    [Fact]
    public void Settings_markup_exposes_all_station_endpoint_fields_and_row_controls()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("基站 / 名称", markup);
        Assert.Contains("通信地址（IP:端口）", markup);
        Assert.Contains("所属站场", markup);
        Assert.Contains("状态", markup);
        Assert.Contains("地图绑定", markup);
        Assert.Contains("新增基站", markup);
        Assert.Contains("移除", markup);
        Assert.Contains("Text=\"{Binding DisplayStationId}\"", markup);
        Assert.Contains("Text=\"{Binding DisplayStationId, Mode=OneWay}\"", markup);
        Assert.Contains("ToolTip=\"稳定标识，创建后不可修改\"", markup);
        Assert.Contains("Focusable=\"False\"", markup);
        Assert.Contains("两位十六进制设备协议地址", markup);
        Assert.Contains("Text=\"{Binding ProtocolAddress, UpdateSourceTrigger=PropertyChanged}\"", markup);
        Assert.DoesNotContain("ToolTip=\"按现场协议表固定为03，不可编辑\"", markup);
        Assert.Contains("Content=\"启用\"", markup);
        Assert.Contains("SettingsToggleStyle", markup);
        var toggleStart = markup.IndexOf("<CheckBox Content=\"启用\"", StringComparison.Ordinal);
        Assert.True(toggleStart >= 0);
        var toggleEnd = markup.IndexOf("<CheckBox.Style>", toggleStart, StringComparison.Ordinal);
        Assert.True(toggleEnd > toggleStart);
        var toggleMarkup = markup.Substring(toggleStart, toggleEnd - toggleStart);
        Assert.Contains("IsChecked=\"{Binding Enabled, UpdateSourceTrigger=PropertyChanged}\"", toggleMarkup);
        Assert.DoesNotContain("<TextBlock Text=\"基站编号\" Style=\"{StaticResource SettingsTableCellLabelStyle}\"", markup);
        Assert.DoesNotContain("<TextBlock Text=\"基站名称\" Style=\"{StaticResource SettingsTableCellLabelStyle}\"", markup);
        Assert.DoesNotContain("<TextBlock Text=\"IP地址\" Style=\"{StaticResource SettingsTableCellLabelStyle}\"", markup);
        Assert.DoesNotContain("<TextBlock Text=\"端口\" Style=\"{StaticResource SettingsTableCellLabelStyle}\"", markup);
        Assert.DoesNotContain("<TextBlock Text=\"协议地址\" Style=\"{StaticResource SettingsTableCellLabelStyle}\"", markup);

        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));
        Assert.Contains("isRfidStationBound", code, StringComparison.Ordinal);
        Assert.Contains("请先解除地图绑定后再删除", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Rfid_recognition_parameters_are_enabled_only_in_admin_mode()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("RfidSettingsEditingEnabled", markup, StringComparison.Ordinal);
        Assert.Contains("RfidSettingsEditingEnabled = _adminModeService.IsAdmin", code, StringComparison.Ordinal);
        Assert.Contains("请先进入管理员模式后再修改 RFID 识别参数", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_save_button_is_disabled_outside_admin_mode()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var saveButtonStart = markup.IndexOf("Content=\"保存设置\"", StringComparison.Ordinal);
        var saveButtonEnd = markup.IndexOf("/>", saveButtonStart, StringComparison.Ordinal);
        Assert.True(saveButtonStart >= 0);
        Assert.True(saveButtonEnd > saveButtonStart);
        var saveButtonMarkup = markup.Substring(saveButtonStart, saveButtonEnd - saveButtonStart);

        Assert.Contains("IsEnabled=\"{Binding RfidSettingsEditingEnabled, RelativeSource={RelativeSource AncestorType={x:Type local:SettingsPage}}}\"", saveButtonMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_page_exposes_independent_yard_communication_interfaces()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("x:Name=\"ConfigureYardCommunicationButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"⚙  配置\"", markup, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnConfigureYardCommunicationClick\"", markup, StringComparison.Ordinal);
        Assert.Contains("YardCommunicationConfigDialog", code, StringComparison.Ordinal);
        Assert.Contains("IEnumerable<YardCommunicationConfig>", code, StringComparison.Ordinal);
        Assert.Contains("SaveYardCommunicationsRequested", code, StringComparison.Ordinal);
        Assert.Contains("TryBuildYardCommunications", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_communication_cards_follow_the_selected_yard_filter()
    {
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("_visibleYardCommunicationRows", code, StringComparison.Ordinal);
        Assert.Contains("private void RebuildVisibleYardCommunicationRows()", code, StringComparison.Ordinal);
        Assert.Contains("string.Equals(selectedFilter, RfidStationYardOption.AllId", code, StringComparison.Ordinal);
        Assert.Contains("_visibleYardCommunicationRows.Add(row)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_communication_configuration_is_collapsed_until_requested()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("x:Name=\"ConfigureYardCommunicationButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"⚙  配置\"", markup, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnConfigureYardCommunicationClick\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"YardCommunicationEditorPanel\"", markup, StringComparison.Ordinal);
        Assert.Contains("OnConfigureYardCommunicationClick", code, StringComparison.Ordinal);
        Assert.Contains("new YardCommunicationConfigDialog", code, StringComparison.Ordinal);
        Assert.Contains("dialog.ShowDialog()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Yard_communication_configuration_dialog_has_clear_editing_actions()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "YardCommunicationConfigDialog.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "YardCommunicationConfigDialog.xaml.cs"));

        Assert.Contains("独立通信配置", markup, StringComparison.Ordinal);
        Assert.Contains("每个站场使用独立监听地址和端口", markup, StringComparison.Ordinal);
        Assert.Contains("监听 IP", markup, StringComparison.Ordinal);
        Assert.Contains("监听端口", markup, StringComparison.Ordinal);
        Assert.Contains("保存配置", markup, StringComparison.Ordinal);
        Assert.Contains("取消", markup, StringComparison.Ordinal);
        Assert.Contains("IsEditingEnabled", markup, StringComparison.Ordinal);
        Assert.Contains("EditedRows", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_section_icons_reserve_a_fixed_column_before_the_titles()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("<ColumnDefinition Width=\"52\" />", markup, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"48\" />", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"&#xE713;\" Style=\"{StaticResource SegoeFluentIconStyle}\" Foreground=\"{StaticResource PrimaryBlueBrush}\" FontSize=\"30\" Width=\"34\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"&#xE701;\" Style=\"{StaticResource SegoeFluentIconStyle}\" Foreground=\"{StaticResource PrimaryBlueBrush}\" FontSize=\"35\" Width=\"40\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_only_the_station_editor_uses_a_nested_scroll_viewer()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("<ListBox x:Name=\"StationsItemsControl\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"基站 / 名称\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"基站名称\" Style=\"{StaticResource SettingsTableHeaderStyle}\"", markup, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", markup, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", markup, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.CanContentScroll=\"False\"", markup, StringComparison.Ordinal);
        Assert.Contains("Padding=\"0,0,0,8\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer VerticalScrollBarVisibility=\"Visible\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer Grid.Row=\"1\" VerticalScrollBarVisibility=\"Auto\"", markup, StringComparison.Ordinal);
        Assert.Contains("<Border Grid.Row=\"1\" MinHeight=\"340\" MaxHeight=\"420\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<Border Grid.Row=\"1\" Height=\"300\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"220\"", markup, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"*\" />", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_breadcrumb_starts_on_the_same_content_inset_as_the_sections()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("<Grid Grid.Row=\"0\" Margin=\"16,0,0,12\">", markup, StringComparison.Ordinal);
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

    [Fact]
    public void Settings_exposes_strict_map_binding_actions_and_conflict_diagnostics()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("地图绑定", markup, StringComparison.Ordinal);
        Assert.Contains("绑定", markup, StringComparison.Ordinal);
        Assert.Contains("解绑", markup, StringComparison.Ordinal);
        Assert.Contains("查看", markup, StringComparison.Ordinal);
        Assert.Contains("OnBindStationClick", markup, StringComparison.Ordinal);
        Assert.Contains("OnUnbindStationClick", markup, StringComparison.Ordinal);
        Assert.Contains("OnViewMapPointClick", markup, StringComparison.Ordinal);
        Assert.Contains("RfidStationBindingRules.FindBindings", code, StringComparison.Ordinal);
        Assert.Contains("请先解绑后再绑定", code, StringComparison.Ordinal);
        Assert.Contains("存在历史重复绑定", code, StringComparison.Ordinal);
        Assert.Contains("BindingEditingEnabled = _stationsEditable && _adminModeService.IsAdmin", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_endpoint_actions_use_semantic_button_styles()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));
        var detailsHandlerStart = code.IndexOf("private void OnViewMapPointClick", StringComparison.Ordinal);
        var detailsHandlerEnd = code.IndexOf("private static bool TryGetStationRow", detailsHandlerStart, StringComparison.Ordinal);
        var detailsHandler = code.Substring(detailsHandlerStart, detailsHandlerEnd - detailsHandlerStart);

        Assert.Contains("x:Key=\"SettingsTableBindButtonStyle\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsTableViewButtonStyle\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsTableConflictButtonStyle\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsTableMoreButtonStyle\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsActionContextMenuStyle\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsActionMenuItemStyle\"", markup, StringComparison.Ordinal);
        Assert.Contains("<ItemsPresenter />", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"绑定地图\" Width=\"84\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"定位地图\" Width=\"88\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"处理冲突\" Width=\"88\"", markup, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnLocateMapPointClick\"", markup, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnMoreActionsClick\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"查看绑定详情\"", markup, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnViewMapPointClick\"", markup, StringComparison.Ordinal);
        Assert.Contains("button.ContextMenu.IsOpen = true", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewMapPointRequested", detailsHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"查看\" Style=\"{StaticResource SettingsTableViewButtonStyle}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"…\" Style=\"{StaticResource SettingsTableMoreButtonStyle}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"解绑\"", markup, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnUnbindStationClick\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"移除\"", markup, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnRemoveStationClick\"", markup, StringComparison.Ordinal);
        Assert.Contains("<ContextMenu Style=\"{StaticResource SettingsActionContextMenuStyle}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SettingsActionMenuItemStyle}\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_endpoint_diagnostics_have_a_dedicated_summary_strip()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("Text=\"基站总数\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingDiagnosticSummaryText\"", markup, StringComparison.Ordinal);
        Assert.Contains("Padding=\"10,4\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("系统诊断", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("诊断状态", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_endpoint_header_uses_a_compact_protocol_address_label()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("Text=\"通信地址（IP:端口）\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("设备协议地址（Byte2）", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"协议地址（Hex）\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_endpoint_layout_separates_filter_and_diagnostic_summary()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("Text=\"站场筛选\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingDiagnosticSummaryPanel\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingDiagnosticWarningPanel\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingStationCountText\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"已启用\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"地图绑定\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"待处理项\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingEnabledCountText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingEnabledRateText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingEnabledProgressBar\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingBoundCountText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingBoundRateText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingBoundProgressBar\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingPendingCountText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingPendingDetailsText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingPendingSummaryCard\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingPendingSummaryIcon\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingBoundSummaryIcon\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"&#xE701;\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"&#xE7BA;\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"&#xE70F;\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"正常绑定\"", markup, StringComparison.Ordinal);
        Assert.Contains("BindingDiagnosticWarningPanel.BorderBrush", code, StringComparison.Ordinal);
        Assert.Contains("BindingDiagnosticWarningPanel.Background", code, StringComparison.Ordinal);
        Assert.Contains("var enabledCount = visibleRows.Count(row => row.Enabled);", code, StringComparison.Ordinal);
        Assert.Contains("var boundCount = visibleRows.Count(row => row.HasBinding);", code, StringComparison.Ordinal);
        Assert.Contains("var pendingCount = visibleRows.Count(row =>", code, StringComparison.Ordinal);
        Assert.Contains("!row.HasBinding || row.HasDuplicateBinding || row.HasYardBindingConflict);", code, StringComparison.Ordinal);
        Assert.Contains("BindingPendingDetailsText.Text", code, StringComparison.Ordinal);
        Assert.Contains("BindingPendingSummaryCard.Background", code, StringComparison.Ordinal);
        Assert.Contains("BindingPendingSummaryIcon.Text = \"\\uE7BA\";", code, StringComparison.Ordinal);
        Assert.DoesNotContain("BindingPendingSummaryIcon.Text = \"\\uE7BA;\";", code, StringComparison.Ordinal);
        Assert.Contains("var statusBrush = warningBrush;", code, StringComparison.Ordinal);
        Assert.Contains("BindingDiagnosticWarningPanel.Visibility = hasPending ? Visibility.Visible : Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("当前配置无待处理项", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_endpoint_table_groups_endpoint_fields_and_has_a_read_only_view()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("Text=\"通信地址（IP:端口）\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"状态\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"只读模式\"", markup, StringComparison.Ordinal);
        Assert.Contains("YardDisplayName", markup, StringComparison.Ordinal);
        Assert.Contains("YardDisplayName", code, StringComparison.Ordinal);
        Assert.Contains("StationEditingEnabled", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_endpoint_section_keeps_help_text_compact_and_fills_the_lower_area()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.DoesNotContain("管理员模式可编辑网络与归属；修改后请保存。", markup, StringComparison.Ordinal);
        Assert.Contains("修改后请保存；仅启用基站参与采集。", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("地图绑定严格一对一：重复绑定只提示，不自动覆盖或删除；绑定、解绑和删除仅管理员可用。", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"配置提示\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"参数说明\"", markup, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"*\" />", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_page_uses_the_compact_visual_configuration_layout()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.DoesNotContain("x:Key=\"SettingsParameterStepButtonStyle\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"⟳  恢复默认\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"ms\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"节\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"秒\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Click=\"OnParameterStepClick\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"⟳  刷新\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"#\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectAllStationsCheckBox", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"配置提示\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("系统诊断", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("诊断状态", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("批量校验", markup, StringComparison.Ordinal);
        Assert.Contains("OnRefreshStationsClick", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OnParameterStepClick", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_page_prioritizes_the_station_list_with_compact_parameter_and_footer_regions()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("Text=\"轮询、标准节数、车辆超时。\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"当前配置可用\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"保存后参数立即生效。\"", markup, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"24\" />", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"轮询间隔，建议 100-1000ms。\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"提示：包含车头，标准节数计算。\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"相邻车辆超时，建议 10-60秒。\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock x:Name=\"ValidationText\" Grid.Row=\"3\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"修改后请保存；仅启用基站参与采集。\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_page_gives_the_station_editor_remaining_height_and_modest_parameter_inputs()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("x:Name=\"StationEditorPanel\" Grid.Row=\"1\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"StationEditorPanel\" Grid.Row=\"1\" Style=\"{StaticResource SettingsPanelStyle}\" Padding=\"12\" Margin=\"0,0,0,10\" VerticalAlignment=\"Top\"", markup, StringComparison.Ordinal);
        Assert.Contains("TextBox x:Name=\"PollIntervalTextBox\" Style=\"{StaticResource SettingsTextBoxStyle}\" Height=\"34\" Width=\"280\"", markup, StringComparison.Ordinal);
        Assert.Contains("TextBox x:Name=\"ExpectedVehicleCountTextBox\" Style=\"{StaticResource SettingsTextBoxStyle}\" Height=\"34\" Width=\"280\"", markup, StringComparison.Ordinal);
        Assert.Contains("TextBox x:Name=\"InterVehicleTimeoutTextBox\" Style=\"{StaticResource SettingsTextBoxStyle}\" Height=\"34\" Width=\"280\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PollIntervalTextBox\" Style=\"{StaticResource SettingsTextBoxStyle}\" Height=\"34\" Width=\"280\" HorizontalAlignment=\"Left\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExpectedVehicleCountTextBox\" Style=\"{StaticResource SettingsTextBoxStyle}\" Height=\"34\" Width=\"280\" HorizontalAlignment=\"Left\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InterVehicleTimeoutTextBox\" Style=\"{StaticResource SettingsTextBoxStyle}\" Height=\"34\" Width=\"280\" HorizontalAlignment=\"Left\"", markup, StringComparison.Ordinal);
        Assert.Equal(3, markup.Split(new[] { "<ColumnDefinition Width=\"280\" />" }, StringSplitOptions.None).Length - 1);
        Assert.Contains("<ColumnDefinition Width=\"360\" />", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_station_overview_uses_four_status_cards_and_a_compact_table_header()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("Text=\"基站总数\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"已启用\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"地图绑定\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"待处理项\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"StationSummaryIconGeometry\"", markup, StringComparison.Ordinal);
        Assert.Contains("<Ellipse Width=\"12\" Height=\"12\"", markup, StringComparison.Ordinal);
        Assert.Contains("BindingDiagnosticSummaryPanel\" Grid.Row=\"3\"", markup, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"64\"", markup, StringComparison.Ordinal);
        Assert.Contains("Width=\"140\"", markup, StringComparison.Ordinal);
        Assert.Contains("Width=\"38\" Height=\"38\" CornerRadius=\"19\"", markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"22\" FontWeight=\"SemiBold\"", markup, StringComparison.Ordinal);
        Assert.Contains("<Grid Height=\"32\">", markup, StringComparison.Ordinal);
        Assert.Contains("<Grid MinHeight=\"48\">", markup, StringComparison.Ordinal);
        Assert.Contains("Padding=\"0,3\"", markup, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"13\" />", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"站场筛选\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"#\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectAllStationsCheckBox", markup, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"5\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_station_table_keeps_action_column_separate_and_uses_binding_aware_actions()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));
        var stationPanelStart = markup.IndexOf("x:Name=\"StationEditorPanel\"", StringComparison.Ordinal);
        var headerStart = markup.IndexOf("<Border Grid.Row=\"0\"", stationPanelStart, StringComparison.Ordinal);
        Assert.True(stationPanelStart >= 0);
        var headerDefinitionsStart = markup.IndexOf("<Grid.ColumnDefinitions>", headerStart, StringComparison.Ordinal);
        var headerDefinitionsEnd = markup.IndexOf("</Grid.ColumnDefinitions>", headerDefinitionsStart, StringComparison.Ordinal);
        var headerDefinitions = markup.Substring(headerDefinitionsStart, headerDefinitionsEnd - headerDefinitionsStart);

        Assert.Equal(8, headerDefinitions.Split(new[] { "<ColumnDefinition" }, StringSplitOptions.None).Length - 1);
        Assert.Contains("Grid.Column=\"6\" Text=\"最后在线时间\"", markup, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"7\" Text=\"操作\"", markup, StringComparison.Ordinal);
        Assert.Contains("LastOnlineDisplay", markup, StringComparison.Ordinal);
        Assert.Contains("public string LastOnlineDisplay", code, StringComparison.Ordinal);
        Assert.Contains("Content=\"定位地图\"", markup, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding HasBinding}\" Value=\"True\"", markup, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Visibility\" Value=\"Collapsed\" />", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_exposes_yard_selection_for_new_and_existing_rfid_stations()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("站场筛选", markup, StringComparison.Ordinal);
        Assert.Contains("ViewRangeComboBox", markup, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnViewRangeChanged\"", markup, StringComparison.Ordinal);
        Assert.Contains("CanAddStation", markup, StringComparison.Ordinal);
        Assert.Contains("SetSelectedYard", code, StringComparison.Ordinal);
        Assert.DoesNotContain("新增归属站场", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("NewStationYardComboBox", markup, StringComparison.Ordinal);
        Assert.Contains("所属站场", markup, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding YardOptions}\"", markup, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding YardId, UpdateSourceTrigger=PropertyChanged}\"", markup, StringComparison.Ordinal);
        Assert.Contains("DropDownClosed=\"OnStationYardChanged\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged=\"OnStationYardChanged\"", markup, StringComparison.Ordinal);
        Assert.Contains("YardId", code, StringComparison.Ordinal);
        Assert.Contains("ApplyStationFilter", code, StringComparison.Ordinal);
        Assert.Contains("请选择具体站场后再新增基站", code, StringComparison.Ordinal);
        Assert.Contains("RfidStationYardOption.AllId", code, StringComparison.Ordinal);
        Assert.Contains("RfidStationYardOption.UnboundId", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_new_station_uses_a_yard_local_number_and_a_yard_scoped_key()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("DisplayStationId", markup, StringComparison.Ordinal);
        Assert.Contains("GetNextStationId(selectedYardId)", code, StringComparison.Ordinal);
        Assert.Contains("RfidStationIdentity.CreateScopedId", code, StringComparison.Ordinal);
        Assert.Contains("GetNextLocalNumber(yardId)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetNextGlobalNumber()", code, StringComparison.Ordinal);
        Assert.Contains("TryGetStationNumber", code, StringComparison.Ordinal);
        Assert.Contains("GetNextDisplayIndex(selectedYardId)", code, StringComparison.Ordinal);
        Assert.Contains("row.DisplayIndex = string.Equals(selectedFilter, RfidStationYardOption.AllId", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_binding_overview_separates_conflict_status_from_details_and_summary()
    {
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("BindingDetailText", markup, StringComparison.Ordinal);
        Assert.Contains("BindingStatusText", markup, StringComparison.Ordinal);
        Assert.Contains("HasYardBindingConflict", markup, StringComparison.Ordinal);
        Assert.Contains("归属冲突", code, StringComparison.Ordinal);
        Assert.Contains("当前存在", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_filter_refresh_rebuilds_the_visible_rows_idempotently()
    {
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));
        var filterStart = code.IndexOf("private void ApplyStationFilter()", StringComparison.Ordinal);
        var filterEnd = code.IndexOf("private string GetSelectedViewFilter()", filterStart, StringComparison.Ordinal);

        Assert.True(filterStart >= 0);
        Assert.True(filterEnd > filterStart);
        var filterCode = code.Substring(filterStart, filterEnd - filterStart);
        Assert.Contains("_visibleStationRows.Clear()", filterCode, StringComparison.Ordinal);
        Assert.Contains("foreach (var row in _stationRows)", filterCode, StringComparison.Ordinal);
        Assert.Contains("_visibleStationRows.Add(row)", filterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_stationRows.Add(row)", filterCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_station_list_refresh_is_guarded_against_reentrant_row_events()
    {
        var code = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs"));

        Assert.Contains("_isStationListRefreshInProgress", code, StringComparison.Ordinal);
        Assert.Contains("if (_isStationListRefreshInProgress)", code, StringComparison.Ordinal);
        Assert.Contains("try", code, StringComparison.Ordinal);
        Assert.Contains("finally", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_map_annotations_refreshes_station_binding_overview()
    {
        var monitorCode = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));
        var mainWindowCode = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("MapAnnotationsSaved", monitorCode, StringComparison.Ordinal);
        Assert.Contains("_monitorPage.MapAnnotationsSaved +=", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("_settingsPage?.RefreshBindingOverview()", mainWindowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Station_id_is_read_only_in_the_model_and_editor()
    {
        var model = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor.Core", "Models", "RfidStationConfig.cs"));
        var markup = File.ReadAllText(LocateSourceFile("src", "MineRailMonitor", "Pages", "SettingsPage.xaml"));

        Assert.Contains("public string StationId { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"稳定标识，创建后不可修改\"", markup, StringComparison.Ordinal);
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
