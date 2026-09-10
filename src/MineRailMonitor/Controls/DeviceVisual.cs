using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MineRailMonitor.Converters;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Controls;

public sealed class DeviceVisual : Button
{
    private const double RfidMarkerAnchorX = 13;
    private const double RfidMarkerAnchorY = 18;
    private readonly Ellipse _stateIndicator;
    private readonly Ellipse _stateRing;
    private readonly Border _labelBorder;
    private readonly TextBlock _labelText;
    private readonly DeviceStateToBrushConverter _brushConverter = new();
    private bool _isAnnotationEditMode;

    public DeviceVisual(DeviceConfig device, string? displayName = null)
    {
        Device = device;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? device.Name : displayName!;
        Width = 112;
        Height = 36;
        Background = Brushes.Transparent;
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        Template = CreateTransparentButtonTemplate();
        ToolTip = $"{DisplayName}  CAD ({device.CadX:0.####}, {device.CadY:0.####})";

        var content = new Grid
        {
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var marker = new Grid
        {
            Width = 26,
            Height = 26,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        _stateRing = new Ellipse
        {
            Width = 24,
            Height = 24,
            Fill = GetBrush("DeviceMarkerBackgroundBrush", Brushes.Transparent),
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _stateIndicator = new Ellipse
        {
            Width = 9,
            Height = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        marker.Children.Add(_stateRing);
        marker.Children.Add(_stateIndicator);
        content.Children.Add(marker);

        _labelBorder = new Border
        {
            Background = GetBrush("DeviceMarkerBackgroundBrush", Brushes.Transparent),
            BorderBrush = GetBrush("DeviceMarkerBorderBrush", Brushes.DarkSlateGray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        _labelText = new TextBlock
        {
            Text = DisplayName,
            Foreground = GetBrush("DeviceMarkerTextBrush", Brushes.White),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _labelBorder.Child = _labelText;
        Grid.SetColumn(_labelBorder, 1);
        content.Children.Add(_labelBorder);
        Content = content;

        SetViewportScale(1);
        SetStatus(DeviceStatus.Normal);
    }

    public DeviceConfig Device { get; }

    public string DisplayName { get; private set; }

    // The map coordinate is the center of the RFID ring, not the center of the wider nameplate.
    public Point MapAnchor => new(RfidMarkerAnchorX, RfidMarkerAnchorY);

    public DeviceStatus Status { get; private set; }

    public RfidStationVisualState RuntimeState { get; private set; } = RfidStationVisualState.Offline;

    public RfidMapBindingState BindingState { get; private set; } = RfidMapBindingState.Offline;

    public void SetDisplayName(string displayName)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Device.Name : displayName;
        ToolTip = $"{DisplayName}  CAD ({Device.CadX:0.####}, {Device.CadY:0.####})";
        _labelText.Text = DisplayName;
    }

    public void SetStatus(DeviceStatus status)
    {
        StopPulse();
        BindingState = RfidMapBindingState.Runtime;
        Status = status;
        RuntimeState = status switch
        {
            DeviceStatus.Offline => RfidStationVisualState.Offline,
            DeviceStatus.Identifying => RfidStationVisualState.Recognizing,
            DeviceStatus.Alarm => RfidStationVisualState.Alarm,
            _ => RfidStationVisualState.Idle
        };
        var brush = (Brush)_brushConverter.Convert(
            status,
            typeof(Brush),
            string.Empty,
            System.Globalization.CultureInfo.InvariantCulture);
        _stateIndicator.Fill = brush;
        _stateRing.Stroke = brush;
        _labelBorder.BorderBrush = brush;
        AutomationProperties.SetHelpText(this, $"{DisplayName} {status}");
    }

    public void SetRuntimeState(RfidStationVisualState state, string? progressText = null)
    {
        var stateChanged = RuntimeState != state;
        BindingState = RfidMapBindingState.Runtime;
        RuntimeState = state;
        Status = state switch
        {
            RfidStationVisualState.Offline => DeviceStatus.Offline,
            RfidStationVisualState.Recognizing => DeviceStatus.Identifying,
            RfidStationVisualState.Alarm => DeviceStatus.Alarm,
            _ => DeviceStatus.Normal
        };

        var brush = GetRuntimeBrush(state);
        _stateIndicator.Fill = brush;
        _stateRing.Stroke = brush;
        _labelBorder.BorderBrush = brush;
        _labelText.Text = string.IsNullOrWhiteSpace(progressText)
            ? DisplayName
            : $"{DisplayName} {progressText}";
        AutomationProperties.SetHelpText(this, $"{DisplayName} {state} {progressText}".Trim());

        if (!stateChanged)
        {
            return;
        }

        StopPulse();
        if (state is RfidStationVisualState.Recognizing or RfidStationVisualState.Warning or RfidStationVisualState.Alarm or RfidStationVisualState.Clearing)
        {
            var pulse = new DoubleAnimation
            {
                From = state == RfidStationVisualState.Alarm ? 0.35 : 0.62,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(state == RfidStationVisualState.Alarm ? 520 : 900)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            _stateRing.BeginAnimation(UIElement.OpacityProperty, pulse);
        }
        else
        {
            _stateRing.Opacity = 1;
        }
    }

    public void SetBindingState(RfidMapBindingState state)
    {
        if (state == RfidMapBindingState.Runtime)
        {
            return;
        }

        StopPulse();
        BindingState = state;
        RuntimeState = RfidStationVisualState.Offline;
        Status = DeviceStatus.Offline;
        var brush = GetBindingBrush(state);
        _stateIndicator.Fill = brush;
        _stateRing.Stroke = brush;
        _labelBorder.BorderBrush = brush;
        var statusText = state switch
        {
            RfidMapBindingState.Unbound => "未绑定基站",
            RfidMapBindingState.MissingConfiguration => "基站配置不存在",
            RfidMapBindingState.Disabled => "基站已禁用",
            _ => "离线"
        };
        _labelText.Text = $"{DisplayName}（{statusText}）";
        AutomationProperties.SetHelpText(this, $"{DisplayName} {statusText}");
    }

    public void SetViewportScale(double scale)
    {
        if (scale <= 0)
        {
            return;
        }

        RenderTransformOrigin = new Point(RfidMarkerAnchorX / Width, RfidMarkerAnchorY / Height);
        RenderTransform = new ScaleTransform(1 / scale, 1 / scale);
    }

    public void SetAnnotationEditMode(bool enabled)
    {
        _isAnnotationEditMode = enabled;
        Cursor = _isAnnotationEditMode ? Cursors.SizeAll : Cursors.Hand;
    }

    public void SetSelected(bool selected)
    {
        Opacity = selected ? 1 : 0.72;
    }

    private static Brush GetBrush(string key, Brush fallback) =>
        Application.Current?.Resources[key] as Brush ?? fallback;

    private static Brush GetRuntimeBrush(RfidStationVisualState state) => state switch
    {
        RfidStationVisualState.Offline => GetBrush("OfflineBrush", Brushes.Gray),
        RfidStationVisualState.Warning => GetBrush("WarningBrush", Brushes.Gold),
        RfidStationVisualState.Alarm => GetBrush("DangerBrush", Brushes.Red),
        RfidStationVisualState.Clearing => GetBrush("PrimaryBlueBrush", Brushes.DeepSkyBlue),
        _ => GetBrush("SuccessBrush", Brushes.LimeGreen)
    };

    private static Brush GetBindingBrush(RfidMapBindingState state) => state switch
    {
        RfidMapBindingState.MissingConfiguration => GetBrush("WarningBrush", Brushes.Gold),
        RfidMapBindingState.Disabled => GetBrush("OfflineBrush", Brushes.Gray),
        _ => GetBrush("MutedTextBrush", Brushes.DarkGray)
    };

    private void StopPulse()
    {
        _stateRing.BeginAnimation(UIElement.OpacityProperty, null);
        _stateRing.Opacity = 1;
    }

    private static ControlTemplate CreateTransparentButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(Content))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }
}
