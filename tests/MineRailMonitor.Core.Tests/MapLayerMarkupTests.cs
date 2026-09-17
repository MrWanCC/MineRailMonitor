namespace MineRailMonitor.Core.Tests;

public sealed class MapLayerMarkupTests
{
    [Fact]
    public void Map_viewport_declares_one_world_transform_for_background_and_overlays()
    {
        var viewport = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapViewport.xaml"));

        var worldIndex = viewport.IndexOf("x:Name=\"MapWorld\"", StringComparison.Ordinal);
        var backgroundIndex = viewport.IndexOf("x:Name=\"BackgroundImage\"", StringComparison.Ordinal);
        var labelIndex = viewport.IndexOf("x:Name=\"MapLabelLayerControl\"", StringComparison.Ordinal);
        var pointIndex = viewport.IndexOf("x:Name=\"MapPointLayerControl\"", StringComparison.Ordinal);
        var rfidIndex = viewport.IndexOf("x:Name=\"DeviceLayerControl\"", StringComparison.Ordinal);

        Assert.True(worldIndex >= 0, "Background and overlays must share a named MapWorld container.");
        Assert.Contains("<Canvas.RenderTransform>", viewport);
        Assert.True(backgroundIndex >= 0, "Background image is not declared.");
        Assert.True(backgroundIndex > worldIndex, "Background image must be inside MapWorld.");
        Assert.True(labelIndex > backgroundIndex, "MapLabelLayer must be above the background.");
        Assert.True(pointIndex > labelIndex, "MapPointLayer must be above the labels.");
        Assert.True(rfidIndex > pointIndex, "The RFID layer must be above ordinary map points.");
    }

    [Fact]
    public void Rfid_visual_uses_the_ring_center_as_the_map_anchor()
    {
        var deviceVisual = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "DeviceVisual.cs"));
        var deviceLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "DeviceLayer.xaml.cs"));
        var viewport = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapViewport.xaml.cs"));

        Assert.Contains("public Point MapAnchor", deviceVisual);
        Assert.Contains("RfidMarkerAnchorX / Width", deviceVisual);
        Assert.Contains("mapPoint.X - visual.MapAnchor.X", deviceLayer);
        Assert.Contains("deviceVisual.MapAnchor", viewport);
    }

    [Fact]
    public void Applying_viewport_transform_does_not_recalculate_annotation_coordinates()
    {
        var viewport = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapViewport.xaml.cs"));
        var start = viewport.IndexOf("private void ApplyTransform()", StringComparison.Ordinal);
        var end = viewport.IndexOf("private void ConstrainOffset()", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "MapViewport transform methods are missing.");
        var applyTransform = viewport.Substring(start, end - start);
        Assert.Contains("MapScaleTransform.ScaleX = _scale", applyTransform);
        Assert.Contains("MapTranslateTransform.X = _offsetX", applyTransform);
        Assert.DoesNotContain("ToMapPixels", applyTransform);
        Assert.DoesNotContain("ArrangePoints", applyTransform);
        Assert.DoesNotContain("ArrangeLabels", applyTransform);
        Assert.DoesNotContain("ArrangeDevices", applyTransform);
    }

    [Fact]
    public void Map_point_and_label_layers_have_separate_controls()
    {
        Assert.True(File.Exists(Locate("src", "MineRailMonitor", "Controls", "MapPointLayer.xaml")));
        Assert.True(File.Exists(Locate("src", "MineRailMonitor", "Controls", "MapPointLayer.xaml.cs")));
        Assert.True(File.Exists(Locate("src", "MineRailMonitor", "Controls", "MapLabelLayer.xaml")));
        Assert.True(File.Exists(Locate("src", "MineRailMonitor", "Controls", "MapLabelLayer.xaml.cs")));
    }

    [Fact]
    public void Map_viewport_exposes_annotation_edit_contract_and_preserves_calibration_contract()
    {
        var viewport = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapViewport.xaml.cs"));

        Assert.Contains("public enum MapAnnotationTool { Select, MapPoint, RfidStation, MapLabel, Delete }", viewport);
        Assert.Contains("public void SetAnnotationEditMode(bool enabled, MapAnnotationTool tool)", viewport);
        Assert.Contains("event EventHandler<MapAnnotationHitEventArgs>? AnnotationClicked", viewport);
        Assert.Contains("event EventHandler<MapCanvasPointEventArgs>? CanvasClicked", viewport);
        Assert.Contains("event EventHandler<MapAnnotationDragEventArgs>? AnnotationDragged", viewport);
        Assert.Contains("MapAnnotationKind", viewport);
        Assert.Contains("MapViewportTransform.ScreenToMapLocal", viewport);
        Assert.Contains("MapCoordinateMapper.ToNormalized", viewport);
        Assert.Contains("_coordinateTransformService.ToCad", viewport);
        Assert.Contains("public void SetCalibrationMode(bool enabled)", viewport);
        Assert.Contains("CalibrationPointClicked", viewport);
        Assert.Contains("public void ShowCalibrationPreview(CadPoint point)", viewport);
        Assert.Contains("public void ClearCalibrationPreview()", viewport);
    }

    [Fact]
    public void Overlay_layers_expose_annotation_metadata_for_readonly_and_editing_modes()
    {
        var pointMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapPointLayer.xaml"));
        var labelMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapLabelLayer.xaml"));
        var pointLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapPointLayer.xaml.cs"));
        var labelLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapLabelLayer.xaml.cs"));
        var deviceLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "DeviceLayer.xaml.cs"));
        var viewport = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapViewport.xaml.cs"));

        Assert.Contains("IsHitTestVisible=\"False\"", pointMarkup);
        Assert.Contains("IsHitTestVisible=\"False\"", labelMarkup);
        Assert.Contains("SetAnnotationEditMode(bool enabled)", pointLayer);
        Assert.Contains("SetAnnotationEditMode(bool enabled)", labelLayer);
        Assert.Contains("IsHitTestVisible = true", pointLayer);
        Assert.Contains("IsHitTestVisible = true", labelLayer);
        Assert.Contains("MapAnnotationVisualMetadata", pointLayer);
        Assert.Contains("MapAnnotationVisualMetadata", labelLayer);
        Assert.Contains("SetAnnotationEditMode(bool enabled)", deviceLayer);
        Assert.Contains("MapPointLayerControl.SetAnnotationEditMode(enabled)", viewport);
        Assert.Contains("MapLabelLayerControl.SetAnnotationEditMode(enabled)", viewport);
        Assert.Contains("DeviceLayerControl.SetAnnotationEditMode(enabled)", viewport);
        Assert.Contains("_calibrationMouseDown = false", viewport);
        Assert.Contains("_calibrationMoved = false", viewport);
    }

    [Fact]
    public void Overlay_layers_do_not_capture_empty_map_area_and_editor_toolbar_keeps_labels_readable()
    {
        var pointMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapPointLayer.xaml"));
        var labelMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapLabelLayer.xaml"));
        var deviceMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "DeviceLayer.xaml"));
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));

        Assert.DoesNotContain("Background=\"Transparent\"", pointMarkup);
        Assert.DoesNotContain("Background=\"Transparent\"", labelMarkup);
        Assert.DoesNotContain("Background=\"Transparent\"", deviceMarkup);
        Assert.Contains("MapAnnotationToolButtonStyle", monitorMarkup);
        Assert.Contains("Property=\"MinWidth\" Value=\"54\"", monitorMarkup);
        Assert.Contains("Text=\"选择：点击已有标注可编辑，按住拖动可移动；新增工具请点击空白底图。\"", monitorMarkup);
    }

    [Fact]
    public void Rfid_station_visual_uses_configured_name_for_display()
    {
        var deviceVisual = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "DeviceVisual.cs"));

        Assert.Contains("Text = DisplayName", deviceVisual);
        Assert.Contains("public string DisplayName", deviceVisual);
    }

    [Fact]
    public void Map_station_visual_preserves_explicit_legacy_device_name()
    {
        var deviceLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "DeviceLayer.xaml.cs"));

        Assert.Contains(
            "string.IsNullOrWhiteSpace(device.Name) ? binding.EffectiveName : device.Name",
            deviceLayer);
    }

    [Fact]
    public void Rfid_station_choice_exposes_a_wpf_display_fallback()
    {
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("public sealed class RfidStationChoice", monitorCode);
        Assert.Contains("public override string ToString() => DisplayName;", monitorCode);
    }

    [Fact]
    public void Rfid_map_editor_preserves_explicit_device_name()
    {
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains(
            "string.IsNullOrWhiteSpace(device.Name) ? binding.EffectiveName : device.Name",
            monitorCode);
    }

    [Fact]
    public void Rfid_map_editor_saves_the_entered_device_name()
    {
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains(
            "MapAnnotationKind.RfidStation => _annotationEditor!.TryUpdateRfidStation(\n                id,\n                name,\n                cadX,\n                cadY,\n                rfidStationId,\n                enabled),",
            monitorCode);
    }

    [Fact]
    public void Rfid_map_editor_selects_station_identity_instead_of_editing_protocol_address()
    {
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));
        var deviceLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "DeviceLayer.xaml.cs"));

        Assert.Contains("x:Name=\"AnnotationRfidStationComboBox\"", monitorMarkup);
        Assert.DoesNotContain("AnnotationProtocolAddressBox", monitorMarkup);
        Assert.Contains("RfidStationId", monitorCode);
        Assert.DoesNotContain("TryReadProtocolAddress", monitorCode);
        Assert.Contains("RfidMapBindingResolver.Resolve", deviceLayer);
        Assert.Contains("binding.Configuration.StationId", deviceLayer);
    }

    [Fact]
    public void Annotation_editor_hides_readonly_info_and_uses_clear_action_copy()
    {
        var monitorMarkup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var monitorCode = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("x:Name=\"SelectedDeviceInfo\"", monitorMarkup);
        Assert.Contains("SetAnnotationEditorVisibility", monitorCode);
        Assert.Contains("SelectedDeviceInfo.Visibility", monitorCode);
        Assert.Contains("Content=\"应用修改\"", monitorMarkup);
        Assert.Contains("Content=\"取消修改\"", monitorMarkup);
        Assert.Contains("Content=\"启用显示\"", monitorMarkup);
        Assert.Contains("后才写入站场配置", monitorMarkup);
    }

    [Fact]
    public void Annotation_drag_updates_only_the_active_visual_until_release()
    {
        var viewport = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapViewport.xaml.cs"));
        var pointLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapPointLayer.xaml.cs"));
        var labelLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapLabelLayer.xaml.cs"));
        var deviceLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "DeviceLayer.xaml.cs"));
        var monitor = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("UpdateAnnotationPreview(current)", viewport);
        Assert.Contains("SetPreviewPosition", pointLayer);
        Assert.Contains("SetPreviewPosition", labelLayer);
        Assert.Contains("SetPreviewPosition", deviceLayer);
        Assert.Contains("FrameworkElement Visual", viewport);
        Assert.DoesNotContain("RefreshAnnotationMap();\r\n        SelectAnnotation(e.Kind, e.AnnotationId);", monitor);
    }

    [Fact]
    public void Annotation_drag_commits_the_visual_anchor_instead_of_raw_pointer_position()
    {
        var viewport = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapViewport.xaml.cs"));

        Assert.Contains("TryGetAnnotationDragAnchor", viewport);
        Assert.Contains("PublishAnnotationDragged(annotation, dragAnchor)", viewport);
        Assert.DoesNotContain("PublishAnnotationDragged(annotation, releasePoint)", viewport);
    }

    [Fact]
    public void Map_labels_remain_opaque_when_another_annotation_is_selected()
    {
        var labelLayer = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapLabelLayer.xaml.cs"));

        Assert.Contains("item.Value.Opacity = 1;", labelLayer);
        Assert.DoesNotContain("0.72", labelLayer);
        Assert.Contains("BorderThickness = selected ? new Thickness(1.5) : new Thickness(0.5)", labelLayer);
        Assert.Contains("AccentBrush", labelLayer);
    }

    [Fact]
    public void Annotation_add_tools_route_overlay_clicks_to_the_canvas()
    {
        var viewport = File.ReadAllText(Locate("src", "MineRailMonitor", "Controls", "MapViewport.xaml.cs"));

        Assert.Contains("var isAddingAnnotation = _isAnnotationEditMode && IsAddTool(_annotationTool);", viewport);
        Assert.Contains("if (TryGetAnnotation(e.OriginalSource, out var annotation) && annotation is not null && !isAddingAnnotation)", viewport);
        Assert.Contains("if (IsDeviceVisualSource(e.OriginalSource) && !isAddingAnnotation)", viewport);
    }

    [Fact]
    public void Applying_or_selecting_an_annotation_returns_editor_to_select_tool()
    {
        var monitor = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("private void SetAnnotationTool(MapAnnotationTool tool)", monitor);
        Assert.Contains("SetAnnotationTool(MapAnnotationTool.Select);", monitor);
        Assert.Contains("SetAnnotationTool(tool);", monitor);
    }

    [Fact]
    public void Monitor_page_exposes_the_admin_map_annotation_editor_instead_of_legacy_calibration()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("地图标注", markup);
        Assert.Contains("普通点位", markup);
        Assert.Contains("RFID基站", markup);
        Assert.Contains("文字标注", markup);
        Assert.Contains("退出编辑", markup);
        Assert.Contains("保存", markup);
        Assert.DoesNotContain("点位标定", markup);
        Assert.Contains("MapAnnotationEditor", code);
        Assert.Contains("TryLeaveMapEditingAsync", code);
        Assert.Contains("TryUndo", code);
        Assert.Contains("e.Key == Key.Delete", code);
        Assert.Contains("e.Key == Key.Escape", code);
    }

    [Fact]
    public void Map_edit_mode_replaces_the_runtime_right_panel_until_editing_ends()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "Pages", "MonitorPage.xaml.cs"));

        Assert.Contains("RfidDetailHeaderText", markup);
        Assert.Contains("AnnotationEditEmptyState", markup);
        Assert.Contains("RfidOverviewCard.Visibility = Visibility.Collapsed", code);
        Assert.Contains("RfidAlarmCard.Visibility = Visibility.Collapsed", code);
        Assert.Contains("Grid.SetRowSpan(RfidDetailView, 2)", code);
        Assert.Contains("地图标注编辑", code);
    }

    [Fact]
    public void Main_window_declares_closing_protection_hook_for_unsaved_map_annotations()
    {
        var markup = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml"));
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("Closing=\"OnWindowClosing\"", markup);
        Assert.Contains("OnWindowClosing", code);
        Assert.Contains("TryLeaveMapEditingAsync", code);
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
