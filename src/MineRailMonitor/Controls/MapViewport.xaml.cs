using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Controls;

public partial class MapViewport : UserControl
{
    private const double DefaultMapWidth = 1600;
    private const double DefaultMapHeight = 900;
    private const double MinimumScale = 0.08;
    private const double MaximumScale = 8;

    private double _mapWidth = DefaultMapWidth;
    private double _mapHeight = DefaultMapHeight;
    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;
    private bool _hasView;
    private bool _isPanning;
    private bool _isCalibrationMode;
    private bool _isAnnotationEditMode;
    private bool _calibrationMouseDown;
    private bool _calibrationMoved;
    private bool _annotationPointerDown;
    private bool _annotationPointerMoved;
    private bool _canvasClickCandidate;
    private Point _calibrationStart;
    private Point _annotationStart;
    private Point _annotationGrabOffset;
    private MapPixelPoint _annotationOriginalAnchor;
    private Point _panStart;
    private double _panStartOffsetX;
    private double _panStartOffsetY;
    private StationConfig? _station;
    private FrameworkElement? _calibrationPreview;
    private readonly CoordinateTransformService _coordinateTransformService = new();
    private MapAnnotationVisualMetadata? _activeAnnotation;
    private MapAnnotationTool _annotationTool = MapAnnotationTool.Select;

    public MapViewport()
    {
        InitializeComponent();
        DeviceLayerControl.DeviceSelected += OnDeviceSelected;
    }

    public event EventHandler<DeviceSelectedEventArgs>? DeviceSelected;

    public event EventHandler<MapCalibrationPointEventArgs>? CalibrationPointClicked;

    public event EventHandler<MapAnnotationHitEventArgs>? AnnotationClicked;

    public event EventHandler<MapCanvasPointEventArgs>? CanvasClicked;

    public event EventHandler<MapAnnotationDragEventArgs>? AnnotationDragged;

    public bool IsCalibrationMode => _isCalibrationMode;

    public void SetAnnotationEditMode(bool enabled, MapAnnotationTool tool)
    {
        CancelPointerOperation();
        _isAnnotationEditMode = enabled;
        _annotationTool = enabled ? tool : MapAnnotationTool.Select;
        MapPointLayerControl.SetAnnotationEditMode(enabled);
        MapLabelLayerControl.SetAnnotationEditMode(enabled);
        DeviceLayerControl.SetAnnotationEditMode(enabled);
    }

    public void SetSelectedAnnotation(MapAnnotationKind? kind, string? annotationId)
    {
        MapPointLayerControl.SetSelected(kind == MapAnnotationKind.MapPoint ? annotationId : null);
        MapLabelLayerControl.SetSelected(kind == MapAnnotationKind.MapLabel ? annotationId : null);
        DeviceLayerControl.SetSelected(kind == MapAnnotationKind.RfidStation ? annotationId : null);
    }

    public void SetStation(StationConfig station, ImageSource? imageSource)
    {
        _station = station;
        BackgroundImage.Source = imageSource;
        EmptyImageNotice.Visibility = imageSource is null ? Visibility.Visible : Visibility.Collapsed;

        if (imageSource is BitmapSource bitmapSource && bitmapSource.PixelWidth > 0 && bitmapSource.PixelHeight > 0)
        {
            _mapWidth = bitmapSource.PixelWidth;
            _mapHeight = bitmapSource.PixelHeight;
        }
        else
        {
            _mapWidth = imageSource?.Width > 0 ? imageSource.Width : DefaultMapWidth;
            _mapHeight = imageSource?.Height > 0 ? imageSource.Height : DefaultMapHeight;
        }

        MapWorld.Width = _mapWidth;
        MapWorld.Height = _mapHeight;
        BackgroundImage.Width = _mapWidth;
        BackgroundImage.Height = _mapHeight;
        MapLabelLayerControl.SetStation(station, _mapWidth, _mapHeight);
        MapPointLayerControl.SetStation(station, _mapWidth, _mapHeight);
        DeviceLayerControl.SetStation(station, _mapWidth, _mapHeight);
        ClearCalibrationPreview();
        _hasView = false;
        ResetView();
    }

    public void SetRfidStations(IEnumerable<RfidStationConfig> stations) =>
        DeviceLayerControl.SetRfidStations(stations);

    public bool SetDeviceStatus(string deviceId, DeviceStatus status) =>
        DeviceLayerControl.SetStatus(deviceId, status);

    public void SetRfidRuntimeStates(IEnumerable<StationRuntimeState> states) =>
        DeviceLayerControl.SetRuntimeStates(states);

    public void ResetView()
    {
        if (ViewportRoot.ActualWidth <= 0 || ViewportRoot.ActualHeight <= 0 || _mapWidth <= 0 || _mapHeight <= 0)
        {
            return;
        }

        _scale = Math.Min(ViewportRoot.ActualWidth / _mapWidth, ViewportRoot.ActualHeight / _mapHeight);
        _scale = Clamp(_scale, MinimumScale, MaximumScale);
        _offsetX = (ViewportRoot.ActualWidth - (_mapWidth * _scale)) / 2;
        _offsetY = (ViewportRoot.ActualHeight - (_mapHeight * _scale)) / 2;
        ConstrainOffset();
        _hasView = true;
        ApplyTransform();
    }

    public void SetCalibrationMode(bool enabled)
    {
        _isCalibrationMode = enabled;
        StopPanning();
        CalibrationModeBanner.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        CalibrationCoordinateText.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled)
        {
            CalibrationCoordinateText.Text = string.Empty;
            ClearCalibrationPreview();
        }
    }

    public void ShowCalibrationPreview(CadPoint point)
    {
        if (_station is null || _mapWidth <= 0 || _mapHeight <= 0)
        {
            return;
        }

        var bounds = new CadBounds(_station.CadMinX, _station.CadMaxX, _station.CadMinY, _station.CadMaxY);
        var normalized = _coordinateTransformService.ToNormalized(point, bounds);
        var mapPoint = MapCoordinateMapper.ToMapPixels(normalized, _mapWidth, _mapHeight);
        if (_calibrationPreview is null)
        {
            _calibrationPreview = new TextBlock
            {
                Text = "◎",
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Width = 34,
                Height = 34,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            CalibrationLayer.Children.Add(_calibrationPreview);
        }

        Canvas.SetLeft(_calibrationPreview, mapPoint.X - 17);
        Canvas.SetTop(_calibrationPreview, mapPoint.Y - 17);
        _calibrationPreview.RenderTransform = new ScaleTransform(1 / _scale, 1 / _scale);
    }

    public void ClearCalibrationPreview()
    {
        if (_calibrationPreview is null)
        {
            return;
        }

        CalibrationLayer.Children.Remove(_calibrationPreview);
        _calibrationPreview = null;
    }

    public void ZoomIn() => ZoomAt(new Point(ViewportRoot.ActualWidth / 2, ViewportRoot.ActualHeight / 2), 1.2);

    public void ZoomOut() => ZoomAt(new Point(ViewportRoot.ActualWidth / 2, ViewportRoot.ActualHeight / 2), 1 / 1.2);

    private void OnViewportLoaded(object sender, RoutedEventArgs e)
    {
        ResetView();
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_hasView || e.PreviousSize.Width <= 0 || e.PreviousSize.Height <= 0)
        {
            ResetView();
            return;
        }

        var previousFitScale = Clamp(
            Math.Min(e.PreviousSize.Width / _mapWidth, e.PreviousSize.Height / _mapHeight),
            MinimumScale,
            MaximumScale);
        if (Math.Abs(_scale - previousFitScale) < 0.0001)
        {
            ResetView();
            return;
        }

        var previousCenter = new Point(
            (e.PreviousSize.Width / 2 - _offsetX) / _scale,
            (e.PreviousSize.Height / 2 - _offsetY) / _scale);
        _offsetX = e.NewSize.Width / 2 - previousCenter.X * _scale;
        _offsetY = e.NewSize.Height / 2 - previousCenter.Y * _scale;
        ConstrainOffset();
        ApplyTransform();
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pointer = e.GetPosition(ViewportRoot);
        ZoomAt(pointer, e.Delta > 0 ? 1.1 : 1 / 1.1);
        e.Handled = true;
    }

    private void OnViewportMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewportRoot.Focus();
        var isAddingAnnotation = _isAnnotationEditMode && IsAddTool(_annotationTool);
        if (TryGetAnnotation(e.OriginalSource, out var annotation) && annotation is not null && !isAddingAnnotation)
        {
            if (_isAnnotationEditMode)
            {
                _activeAnnotation = annotation;
                _annotationPointerDown = true;
                _annotationPointerMoved = false;
                _annotationStart = e.GetPosition(ViewportRoot);
                _annotationGrabOffset = new Point();
                if (TryScreenToMapLocal(_annotationStart, out var mapLocalPoint))
                {
                    var visualLeft = Canvas.GetLeft(annotation.Visual);
                    var visualTop = Canvas.GetTop(annotation.Visual);
                    if (double.IsNaN(visualLeft))
                    {
                        visualLeft = 0;
                    }

                    if (double.IsNaN(visualTop))
                    {
                        visualTop = 0;
                    }

                    var anchorPoint = new Point(
                        visualLeft + annotation.AnchorOffset.X,
                        visualTop + annotation.AnchorOffset.Y);
                    _annotationOriginalAnchor = new MapPixelPoint(anchorPoint.X, anchorPoint.Y);
                    _annotationGrabOffset = new Point(
                        anchorPoint.X - mapLocalPoint.X,
                        anchorPoint.Y - mapLocalPoint.Y);
                }

                ViewportRoot.CaptureMouse();
            }
            else
            {
                if (annotation.Kind == MapAnnotationKind.RfidStation)
                {
                    return;
                }

                PublishAnnotationClicked(annotation, e.GetPosition(ViewportRoot));
            }

            e.Handled = true;
            return;
        }

        if (IsDeviceVisualSource(e.OriginalSource) && !isAddingAnnotation)
        {
            return;
        }

        if (_isCalibrationMode && !_isAnnotationEditMode)
        {
            _calibrationMouseDown = true;
            _calibrationMoved = false;
            _calibrationStart = e.GetPosition(ViewportRoot);
            _panStart = _calibrationStart;
            _panStartOffsetX = _offsetX;
            _panStartOffsetY = _offsetY;
            ViewportRoot.CaptureMouse();
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(ViewportRoot);
        _panStartOffsetX = _offsetX;
        _panStartOffsetY = _offsetY;
        _canvasClickCandidate = _isAnnotationEditMode && IsAddTool(_annotationTool);
        ViewportRoot.CaptureMouse();
        Cursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private static bool IsDeviceVisualSource(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is DeviceVisual)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void OnViewportMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_annotationPointerDown && _activeAnnotation is not null)
        {
            var releasePoint = e.GetPosition(ViewportRoot);
            var annotation = _activeAnnotation;
            var wasDragged = _annotationPointerMoved;
            var dragAnchor = default(MapPixelPoint);
            var shouldCommitDrag = wasDragged && _annotationTool == MapAnnotationTool.Select &&
                                   TryGetAnnotationDragAnchor(releasePoint, out dragAnchor);
            CancelPointerOperation(!shouldCommitDrag);
            if (shouldCommitDrag)
            {
                PublishAnnotationDragged(annotation, dragAnchor);
            }
            else if (!wasDragged || _annotationTool != MapAnnotationTool.Select)
            {
                PublishAnnotationClicked(annotation, releasePoint);
            }

            e.Handled = true;
            return;
        }

        if (_isCalibrationMode && _calibrationMouseDown)
        {
            var clickPoint = e.GetPosition(ViewportRoot);
            var wasPanning = _isPanning;
            _calibrationMouseDown = false;
            StopPanning();
            if (!wasPanning && !_calibrationMoved)
            {
                PublishCalibrationPoint(clickPoint);
            }

            e.Handled = true;
            return;
        }

        var canvasPoint = e.GetPosition(ViewportRoot);
        var publishCanvasClick = _canvasClickCandidate;
        _canvasClickCandidate = false;
        StopPanning();
        if (publishCanvasClick)
        {
            PublishCanvasClicked(canvasPoint);
            e.Handled = true;
        }
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(ViewportRoot);
        if (_annotationPointerDown)
        {
            if (!_annotationPointerMoved && HasPointerMoved(_annotationStart, current))
            {
                _annotationPointerMoved = true;
                Cursor = Cursors.SizeAll;
            }

            if (_annotationPointerMoved)
            {
                UpdateAnnotationPreview(current);
            }

            return;
        }

        if (_isCalibrationMode)
        {
            UpdateCalibrationCoordinates(current);
            if (_calibrationMouseDown && !_calibrationMoved &&
                (Math.Abs(current.X - _calibrationStart.X) > 4 || Math.Abs(current.Y - _calibrationStart.Y) > 4))
            {
                _calibrationMoved = true;
                _isPanning = true;
                Cursor = Cursors.ScrollAll;
            }
        }

        if (!_isPanning)
        {
            return;
        }

        if (_canvasClickCandidate && HasPointerMoved(_panStart, current))
        {
            _canvasClickCandidate = false;
        }

        _offsetX = _panStartOffsetX + current.X - _panStart.X;
        _offsetY = _panStartOffsetY + current.Y - _panStart.Y;
        ApplyTransform();
    }

    private void ZoomAt(Point viewportPoint, double factor)
    {
        if (!_hasView || factor <= 0)
        {
            return;
        }

        var mapPoint = new Point(
            (viewportPoint.X - _offsetX) / _scale,
            (viewportPoint.Y - _offsetY) / _scale);
        _scale = Clamp(_scale * factor, MinimumScale, MaximumScale);
        _offsetX = viewportPoint.X - mapPoint.X * _scale;
        _offsetY = viewportPoint.Y - mapPoint.Y * _scale;
        ConstrainOffset();
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        // MapWorld owns geographic position. Layer inverse scales below are visual-size
        // compensation only; they must never be used to calculate map-local coordinates.
        MapScaleTransform.ScaleX = _scale;
        MapScaleTransform.ScaleY = _scale;
        MapTranslateTransform.X = _offsetX;
        MapTranslateTransform.Y = _offsetY;
        MapLabelLayerControl.SetViewportScale(_scale);
        MapPointLayerControl.SetViewportScale(_scale);
        DeviceLayerControl.SetViewportScale(_scale);
        if (_calibrationPreview is not null)
        {
            _calibrationPreview.RenderTransform = new ScaleTransform(1 / _scale, 1 / _scale);
        }
    }

    private void ConstrainOffset()
    {
        if (ViewportRoot.ActualWidth <= 0 || ViewportRoot.ActualHeight <= 0 || _mapWidth <= 0 || _mapHeight <= 0)
        {
            return;
        }

        var scaledWidth = _mapWidth * _scale;
        var scaledHeight = _mapHeight * _scale;

        if (scaledWidth <= ViewportRoot.ActualWidth)
        {
            _offsetX = (ViewportRoot.ActualWidth - scaledWidth) / 2;
        }
        else
        {
            _offsetX = Clamp(_offsetX, ViewportRoot.ActualWidth - scaledWidth, 0);
        }

        if (scaledHeight <= ViewportRoot.ActualHeight)
        {
            _offsetY = (ViewportRoot.ActualHeight - scaledHeight) / 2;
        }
        else
        {
            _offsetY = Clamp(_offsetY, ViewportRoot.ActualHeight - scaledHeight, 0);
        }
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private void StopPanning()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        ViewportRoot.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private void OnViewportPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        CancelPointerOperation();
        if (_isAnnotationEditMode)
        {
            SetAnnotationEditMode(true, MapAnnotationTool.Select);
        }

        e.Handled = true;
    }

    private void CancelPointerOperation(bool restoreAnnotationPreview = true)
    {
        if (restoreAnnotationPreview && _annotationPointerDown && _annotationPointerMoved && _activeAnnotation is not null)
        {
            ApplyAnnotationPreview(_activeAnnotation, _annotationOriginalAnchor);
        }

        _calibrationMouseDown = false;
        _calibrationMoved = false;
        _annotationPointerDown = false;
        _annotationPointerMoved = false;
        _activeAnnotation = null;
        _annotationGrabOffset = new Point();
        _canvasClickCandidate = false;
        StopPanning();
        if (ViewportRoot.IsMouseCaptured)
        {
            ViewportRoot.ReleaseMouseCapture();
        }

        Cursor = Cursors.Arrow;
    }

    private void UpdateCalibrationCoordinates(Point screenPoint)
    {
        if (!_isCalibrationMode || _station is null || !_hasView)
        {
            return;
        }

        var cadPoint = ScreenToCad(screenPoint);
        CalibrationCoordinateText.Text = $"CAD: X={cadPoint.X:0.000}  Y={cadPoint.Y:0.000}";
    }

    private void PublishCalibrationPoint(Point screenPoint)
    {
        if (_station is null || !_hasView)
        {
            return;
        }

        var mapLocalPoint = MapViewportTransform.ScreenToMapLocal(
            new MapPixelPoint(screenPoint.X, screenPoint.Y),
            _scale,
            _offsetX,
            _offsetY);
        if (mapLocalPoint.X < 0 || mapLocalPoint.Y < 0 || mapLocalPoint.X > _mapWidth || mapLocalPoint.Y > _mapHeight)
        {
            return;
        }

        var cadPoint = ScreenToCad(screenPoint);
        ShowCalibrationPreview(cadPoint);
        CalibrationPointClicked?.Invoke(this, new MapCalibrationPointEventArgs(cadPoint));
    }

    private CadPoint ScreenToCad(Point screenPoint)
    {
        var mapLocalPoint = MapViewportTransform.ScreenToMapLocal(
            new MapPixelPoint(screenPoint.X, screenPoint.Y),
            _scale,
            _offsetX,
            _offsetY);
        var normalized = MapCoordinateMapper.ToNormalized(mapLocalPoint, _mapWidth, _mapHeight);
        var bounds = new CadBounds(_station!.CadMinX, _station.CadMaxX, _station.CadMinY, _station.CadMaxY);
        return _coordinateTransformService.ToCad(normalized, bounds);
    }

    private void PublishAnnotationClicked(MapAnnotationVisualMetadata annotation, Point screenPoint)
    {
        if (TryScreenToCad(screenPoint, out var cadPoint))
        {
            AnnotationClicked?.Invoke(this, new MapAnnotationHitEventArgs(annotation.Kind, annotation.Id, cadPoint));
        }
    }

    private void PublishAnnotationDragged(MapAnnotationVisualMetadata annotation, MapPixelPoint mapLocalPoint)
    {
        if (TryMapLocalToCad(mapLocalPoint, out var cadPoint))
        {
            AnnotationDragged?.Invoke(this, new MapAnnotationDragEventArgs(annotation.Kind, annotation.Id, cadPoint));
        }
    }

    private void UpdateAnnotationPreview(Point screenPoint)
    {
        if (_activeAnnotation is null || !TryGetAnnotationDragAnchor(screenPoint, out var mapLocalPoint))
        {
            return;
        }

        ApplyAnnotationPreview(_activeAnnotation, mapLocalPoint);
    }

    private bool TryGetAnnotationDragAnchor(Point screenPoint, out MapPixelPoint mapLocalPoint)
    {
        mapLocalPoint = default;
        if (!TryScreenToMapLocal(screenPoint, out var pointerMapPoint))
        {
            return false;
        }

        var anchorX = pointerMapPoint.X + _annotationGrabOffset.X;
        var anchorY = pointerMapPoint.Y + _annotationGrabOffset.Y;
        mapLocalPoint = new MapPixelPoint(
            Math.Max(0, Math.Min(_mapWidth, anchorX)),
            Math.Max(0, Math.Min(_mapHeight, anchorY)));
        return true;
    }

    private void ApplyAnnotationPreview(MapAnnotationVisualMetadata annotation, MapPixelPoint mapPoint)
    {
        switch (annotation.Kind)
        {
            case MapAnnotationKind.MapPoint:
                MapPointLayerControl.SetPreviewPosition(annotation.Id, mapPoint);
                break;
            case MapAnnotationKind.RfidStation:
                DeviceLayerControl.SetPreviewPosition(annotation.Id, mapPoint);
                break;
            case MapAnnotationKind.MapLabel:
                MapLabelLayerControl.SetPreviewPosition(annotation.Id, mapPoint);
                break;
        }
    }

    private void PublishCanvasClicked(Point screenPoint)
    {
        if (TryScreenToCad(screenPoint, out var cadPoint))
        {
            CanvasClicked?.Invoke(this, new MapCanvasPointEventArgs(cadPoint));
        }
    }

    private bool TryScreenToCad(Point screenPoint, out CadPoint cadPoint)
    {
        cadPoint = default;
        if (!TryScreenToMapLocal(screenPoint, out var mapLocalPoint))
        {
            return false;
        }

        return TryMapLocalToCad(mapLocalPoint, out cadPoint);
    }

    private bool TryMapLocalToCad(MapPixelPoint mapLocalPoint, out CadPoint cadPoint)
    {
        cadPoint = default;
        if (_station is null || !_hasView ||
            mapLocalPoint.X < 0 || mapLocalPoint.Y < 0 ||
            mapLocalPoint.X > _mapWidth || mapLocalPoint.Y > _mapHeight)
        {
            return false;
        }

        var normalized = MapCoordinateMapper.ToNormalized(mapLocalPoint, _mapWidth, _mapHeight);
        var bounds = new CadBounds(_station!.CadMinX, _station.CadMaxX, _station.CadMinY, _station.CadMaxY);
        cadPoint = _coordinateTransformService.ToCad(normalized, bounds);
        return true;
    }

    private bool TryScreenToMapLocal(Point screenPoint, out MapPixelPoint mapLocalPoint)
    {
        mapLocalPoint = default;
        if (_station is null || !_hasView)
        {
            return false;
        }

        mapLocalPoint = MapViewportTransform.ScreenToMapLocal(
            new MapPixelPoint(screenPoint.X, screenPoint.Y),
            _scale,
            _offsetX,
            _offsetY);
        if (mapLocalPoint.X < 0 || mapLocalPoint.Y < 0 || mapLocalPoint.X > _mapWidth || mapLocalPoint.Y > _mapHeight)
        {
            mapLocalPoint = default;
            return false;
        }

        return true;
    }

    private static bool IsAddTool(MapAnnotationTool tool) =>
        tool is MapAnnotationTool.MapPoint or MapAnnotationTool.RfidStation or MapAnnotationTool.MapLabel;

    private static bool HasPointerMoved(Point start, Point current) =>
        Math.Abs(current.X - start.X) > 4 || Math.Abs(current.Y - start.Y) > 4;

    private static bool TryGetAnnotation(object source, out MapAnnotationVisualMetadata? annotation)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is DeviceVisual deviceVisual)
            {
                annotation = new MapAnnotationVisualMetadata(
                    MapAnnotationKind.RfidStation,
                    deviceVisual.Device.Id,
                    deviceVisual,
                    deviceVisual.MapAnchor);
                return true;
            }

            if (current is FrameworkElement element && element.DataContext is MapAnnotationVisualMetadata metadata)
            {
                annotation = metadata;
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        annotation = null;
        return false;
    }

    private void OnDeviceSelected(object? sender, DeviceSelectedEventArgs e)
    {
        DeviceSelected?.Invoke(this, e);
    }
}

public enum MapAnnotationTool { Select, MapPoint, RfidStation, MapLabel, Delete }

public sealed class MapAnnotationHitEventArgs : EventArgs
{
    public MapAnnotationHitEventArgs(MapAnnotationKind kind, string annotationId, CadPoint cadPoint)
    {
        Kind = kind;
        AnnotationId = annotationId;
        CadPoint = cadPoint;
    }

    public MapAnnotationKind Kind { get; }

    public MapAnnotationKind AnnotationKind => Kind;

    public string AnnotationId { get; }

    public string Id => AnnotationId;

    public CadPoint CadPoint { get; }
}

public sealed class MapCanvasPointEventArgs : EventArgs
{
    public MapCanvasPointEventArgs(CadPoint cadPoint)
    {
        CadPoint = cadPoint;
    }

    public CadPoint CadPoint { get; }
}

public sealed class MapAnnotationDragEventArgs : EventArgs
{
    public MapAnnotationDragEventArgs(MapAnnotationKind kind, string annotationId, CadPoint cadPoint)
    {
        Kind = kind;
        AnnotationId = annotationId;
        CadPoint = cadPoint;
    }

    public MapAnnotationKind Kind { get; }

    public MapAnnotationKind AnnotationKind => Kind;

    public string AnnotationId { get; }

    public string Id => AnnotationId;

    public CadPoint CadPoint { get; }
}

internal sealed class MapAnnotationVisualMetadata
{
    public MapAnnotationVisualMetadata(MapAnnotationKind kind, string id, FrameworkElement visual, Point anchorOffset)
    {
        Kind = kind;
        Id = id;
        Visual = visual;
        AnchorOffset = anchorOffset;
    }

    public MapAnnotationKind Kind { get; }

    public string Id { get; }

    public FrameworkElement Visual { get; }

    public Point AnchorOffset { get; }
}

public sealed class MapCalibrationPointEventArgs : EventArgs
{
    public MapCalibrationPointEventArgs(CadPoint cadPoint)
    {
        CadPoint = cadPoint;
    }

    public CadPoint CadPoint { get; }
}
