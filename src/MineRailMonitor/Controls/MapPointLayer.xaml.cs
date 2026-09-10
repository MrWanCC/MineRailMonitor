using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Controls;

public partial class MapPointLayer : UserControl
{
    private const double MarkerAnchorX = 7;
    private const double MarkerAnchorY = 15;
    private readonly ICoordinateTransformService _coordinateTransformService = new CoordinateTransformService();
    private readonly Dictionary<string, FrameworkElement> _visuals = new(StringComparer.OrdinalIgnoreCase);
    private StationConfig? _station;
    private double _mapWidth;
    private double _mapHeight;
    private double _viewportScale = 1;
    private bool _isAnnotationEditMode;

    public MapPointLayer()
    {
        InitializeComponent();
    }

    public void SetStation(StationConfig station, double mapWidth, double mapHeight)
    {
        _station = station;
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        Width = mapWidth;
        Height = mapHeight;
        RootCanvas.Width = mapWidth;
        RootCanvas.Height = mapHeight;
        RootCanvas.Children.Clear();
        _visuals.Clear();

        foreach (var point in station.Points.Where(point => point.Enabled))
        {
            var visual = CreateVisual(point);
            _visuals[point.Id] = visual;
            RootCanvas.Children.Add(visual);
        }

        ArrangePoints();
        SetViewportScale(_viewportScale);
        SetAnnotationEditMode(_isAnnotationEditMode);
    }

    public void SetAnnotationEditMode(bool enabled)
    {
        _isAnnotationEditMode = enabled;
        IsHitTestVisible = true;
        RootCanvas.IsHitTestVisible = true;
        foreach (var visual in _visuals.Values)
        {
            visual.IsHitTestVisible = true;
            visual.Cursor = enabled ? Cursors.SizeAll : Cursors.Hand;
        }
    }

    public void SetSelected(string? annotationId)
    {
        foreach (var item in _visuals)
        {
            item.Value.Opacity = annotationId is null || string.Equals(item.Key, annotationId, StringComparison.OrdinalIgnoreCase) ? 1 : 0.72;
        }
    }

    public void SetPreviewPosition(string annotationId, MapPixelPoint mapPoint)
    {
        if (!_visuals.TryGetValue(annotationId, out var visual))
        {
            return;
        }

        Canvas.SetLeft(visual, mapPoint.X - MarkerAnchorX);
        Canvas.SetTop(visual, mapPoint.Y - MarkerAnchorY);
    }

    public void SetViewportScale(double scale)
    {
        if (scale <= 0)
        {
            return;
        }

        _viewportScale = scale;
        foreach (var visual in _visuals.Values)
        {
            visual.RenderTransform = new ScaleTransform(1 / scale, 1 / scale);
        }
    }

    private static FrameworkElement CreateVisual(MapPoint point)
    {
        const double anchorX = MarkerAnchorX;
        const double anchorY = MarkerAnchorY;
        const double width = 88;
        const double height = 22;

        var visual = new Grid
        {
            Width = width,
            Height = height,
            RenderTransformOrigin = new Point(anchorX / width, anchorY / height),
            ToolTip = $"{point.Name}  CAD ({point.CadX:0.####}, {point.CadY:0.####})"
        };
        visual.DataContext = new MapAnnotationVisualMetadata(
            MapAnnotationKind.MapPoint,
            point.Id,
            visual,
            new Point(anchorX, anchorY));
        var marker = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = GetBrush("MapPointMarkerBrush", Brushes.SlateGray),
            Stroke = GetBrush("MapPointMarkerStrokeBrush", Brushes.LightSteelBlue),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = point.Name,
            Foreground = GetBrush("MapPointLabelBrush", Brushes.LightGray),
            Background = GetBrush("MapPointLabelBackgroundBrush", Brushes.Transparent),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 10,
            FontWeight = FontWeights.Normal,
            Padding = new Thickness(3, 1, 3, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, -1, 0, 0)
        };
        visual.Children.Add(marker);
        visual.Children.Add(label);
        return visual;
    }

    private static Brush GetBrush(string key, Brush fallback) =>
        Application.Current?.Resources[key] as Brush ?? fallback;

    private void ArrangePoints()
    {
        if (_station is null || _mapWidth <= 0 || _mapHeight <= 0)
        {
            return;
        }

        var bounds = new CadBounds(_station.CadMinX, _station.CadMaxX, _station.CadMinY, _station.CadMaxY);
        foreach (var point in _station.Points.Where(point => point.Enabled))
        {
            if (!_visuals.TryGetValue(point.Id, out var visual))
            {
                continue;
            }

            var normalized = _coordinateTransformService.ToNormalized(new CadPoint(point.CadX, point.CadY), bounds);
            var mapPoint = MapCoordinateMapper.ToMapPixels(normalized, _mapWidth, _mapHeight);
            Canvas.SetLeft(visual, mapPoint.X - MarkerAnchorX);
            Canvas.SetTop(visual, mapPoint.Y - MarkerAnchorY);
        }
    }
}
