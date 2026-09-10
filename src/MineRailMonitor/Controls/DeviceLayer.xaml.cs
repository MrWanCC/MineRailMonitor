using System.Windows;
using System.Windows.Controls;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Controls;

public partial class DeviceLayer : UserControl
{
    private readonly ICoordinateTransformService _coordinateTransformService = new CoordinateTransformService();
    private readonly Dictionary<string, DeviceVisual> _visuals = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, RfidStationConfig> _rfidStations =
        new Dictionary<string, RfidStationConfig>(StringComparer.OrdinalIgnoreCase);
    private StationConfig? _station;
    private double _mapWidth;
    private double _mapHeight;
    private bool _isAnnotationEditMode;

    public DeviceLayer()
    {
        InitializeComponent();
    }

    public event EventHandler<DeviceSelectedEventArgs>? DeviceSelected;

    public void SetRfidStations(IEnumerable<RfidStationConfig> stations)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));

        _rfidStations = stations
            .Where(station => station is not null && !string.IsNullOrWhiteSpace(station.StationId))
            .GroupBy(station => station.StationId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (_station is not null)
        {
            SetStation(_station, _mapWidth, _mapHeight);
        }
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

        foreach (var device in station.Devices.Where(device => device.Enabled && device.Type == DeviceType.RfidStation))
        {
            var binding = RfidMapBindingResolver.Resolve(device, _rfidStations.Values);
            var visual = new DeviceVisual(device, binding.EffectiveName);
            visual.Click += OnDeviceClicked;
            visual.SetAnnotationEditMode(_isAnnotationEditMode);
            _visuals[device.Id] = visual;
            RootCanvas.Children.Add(visual);
        }

        ArrangeDevices();
    }

    public bool SetStatus(string deviceId, DeviceStatus status)
    {
        if (!_visuals.TryGetValue(deviceId, out var visual))
        {
            return false;
        }

        visual.SetStatus(status);
        return true;
    }

    public void SetRuntimeStates(IEnumerable<StationRuntimeState> states)
    {
        if (states is null) throw new ArgumentNullException(nameof(states));

        var snapshot = states.ToArray();
        foreach (var visual in _visuals.Values)
        {
            var binding = RfidMapBindingResolver.Resolve(visual.Device, _rfidStations.Values);
            if (binding.State != RfidMapBindingState.Offline || binding.Configuration is null)
            {
                visual.SetBindingState(binding.State);
                continue;
            }

            var state = snapshot.FirstOrDefault(item =>
                string.Equals(item.StationId, binding.Configuration.StationId, StringComparison.OrdinalIgnoreCase));
            if (state is null)
            {
                visual.SetBindingState(RfidMapBindingState.Offline);
                continue;
            }

            var progress = state.DetectedVehicleCount > 0
                ? $"{state.DetectedVehicleCount}/{state.ExpectedVehicleCount}"
                : null;
            visual.SetRuntimeState(state.VisualState, progress);
        }
    }

    public void SetAnnotationEditMode(bool enabled)
    {
        _isAnnotationEditMode = enabled;
        foreach (var visual in _visuals.Values)
        {
            visual.SetAnnotationEditMode(enabled);
        }
    }

    public void SetSelected(string? annotationId)
    {
        foreach (var item in _visuals)
        {
            item.Value.SetSelected(annotationId is null || string.Equals(item.Key, annotationId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void SetPreviewPosition(string annotationId, MapPixelPoint mapPoint)
    {
        if (!_visuals.TryGetValue(annotationId, out var visual))
        {
            return;
        }

        Canvas.SetLeft(visual, mapPoint.X - visual.MapAnchor.X);
        Canvas.SetTop(visual, mapPoint.Y - visual.MapAnchor.Y);
    }

    public void SetViewportScale(double scale)
    {
        if (scale <= 0)
        {
            return;
        }

        foreach (var visual in _visuals.Values)
        {
            visual.SetViewportScale(scale);
        }
    }

    private void OnDeviceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is DeviceVisual visual)
        {
            DeviceSelected?.Invoke(this, new DeviceSelectedEventArgs(visual.Device, visual.Status));
            e.Handled = true;
        }
    }

    private void OnRootCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ArrangeDevices();
    }

    private void ArrangeDevices()
    {
        if (_station is null || _mapWidth <= 0 || _mapHeight <= 0)
        {
            return;
        }

        var bounds = new CadBounds(_station.CadMinX, _station.CadMaxX, _station.CadMinY, _station.CadMaxY);
        foreach (var visual in _visuals.Values)
        {
            var normalized = _coordinateTransformService.ToNormalized(
                new CadPoint(visual.Device.CadX, visual.Device.CadY),
                bounds);
            var mapPoint = MapCoordinateMapper.ToMapPixels(normalized, _mapWidth, _mapHeight);
            Canvas.SetLeft(visual, mapPoint.X - visual.MapAnchor.X);
            Canvas.SetTop(visual, mapPoint.Y - visual.MapAnchor.Y);
        }
    }

}

public sealed class DeviceSelectedEventArgs : EventArgs
{
    public DeviceSelectedEventArgs(DeviceConfig device, DeviceStatus status)
    {
        Device = device;
        Status = status;
    }

    public DeviceConfig Device { get; }

    public DeviceStatus Status { get; }
}
