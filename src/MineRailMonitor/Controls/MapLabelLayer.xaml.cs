using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Controls;

public partial class MapLabelLayer : UserControl
{
    private readonly ICoordinateTransformService _coordinateTransformService = new CoordinateTransformService();
    private readonly Dictionary<string, FrameworkElement> _visuals = new(StringComparer.OrdinalIgnoreCase);
    private StationConfig? _station;
    private double _mapWidth;
    private double _mapHeight;
    private double _viewportScale = 1;
    private bool _isAnnotationEditMode;

    public MapLabelLayer()
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

        foreach (var label in station.Labels.Where(label => label.Enabled))
        {
            var visual = CreateVisual(label);
            _visuals[label.Id] = visual;
            RootCanvas.Children.Add(visual);
        }

        ArrangeLabels();
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
            var selected = annotationId is not null &&
                           string.Equals(item.Key, annotationId, StringComparison.OrdinalIgnoreCase);
            item.Value.Opacity = 1;
            if (item.Value is Border border)
            {
                border.BorderBrush = selected
                    ? Application.Current?.Resources["AccentBrush"] as Brush ?? Brushes.White
                    : new SolidColorBrush(Color.FromArgb(125, 0, 0, 0));
                border.BorderThickness = selected ? new Thickness(1.5) : new Thickness(0.5);
            }
        }
    }

    public void SetPreviewPosition(string annotationId, MapPixelPoint mapPoint)
    {
        if (!_visuals.TryGetValue(annotationId, out var visual))
        {
            return;
        }

        Canvas.SetLeft(visual, mapPoint.X);
        Canvas.SetTop(visual, mapPoint.Y);
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
            if (visual.Tag is MapLabelVisualTag tag)
            {
                visual.RenderTransform = CreateTransform(tag.Rotation, scale);
            }
        }
    }

    private static FrameworkElement CreateVisual(MapLabel label)
    {
        var visual = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(170, 8, 17, 28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(125, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(2, 1, 2, 1),
            RenderTransformOrigin = new Point(0, 0),
            Tag = new MapLabelVisualTag(label.Rotation),
            ToolTip = $"{label.Text}  CAD ({label.CadX:0.####}, {label.CadY:0.####})",
            Child = new TextBlock
            {
                Text = label.Text,
                Foreground = new SolidColorBrush(Color.FromRgb(218, 201, 66)),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = Math.Max(9, MapLabelTransform.GetFontSize(label.TextHeight) - 2),
                FontWeight = FontWeights.Normal,
                Margin = new Thickness(1, -2, 1, 0)
            }
        };
        visual.DataContext = new MapAnnotationVisualMetadata(
            MapAnnotationKind.MapLabel,
            label.Id,
            visual,
            new Point(0, 0));
        return visual;
    }

    private void ArrangeLabels()
    {
        if (_station is null || _mapWidth <= 0 || _mapHeight <= 0)
        {
            return;
        }

        var bounds = new CadBounds(_station.CadMinX, _station.CadMaxX, _station.CadMinY, _station.CadMaxY);
        foreach (var label in _station.Labels.Where(label => label.Enabled))
        {
            if (!_visuals.TryGetValue(label.Id, out var visual))
            {
                continue;
            }

            var normalized = _coordinateTransformService.ToNormalized(new CadPoint(label.CadX, label.CadY), bounds);
            var mapPoint = MapCoordinateMapper.ToMapPixels(normalized, _mapWidth, _mapHeight);
            Canvas.SetLeft(visual, mapPoint.X);
            Canvas.SetTop(visual, mapPoint.Y);
        }
    }

    private static Transform CreateTransform(double cadRotation, double scale)
    {
        var transform = new TransformGroup();
        transform.Children.Add(new RotateTransform(MapLabelTransform.ToWpfRotation(cadRotation)));
        transform.Children.Add(new ScaleTransform(1 / scale, 1 / scale));
        return transform;
    }

    private sealed class MapLabelVisualTag
    {
        public MapLabelVisualTag(double rotation)
        {
            Rotation = rotation;
        }

        public double Rotation { get; }
    }
}
