using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Controls.Primitives;
using MineRailMonitor.Converters;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Controls;

public sealed class DeviceVisual : Button
{
    private const double RfidMarkerAnchorX = 13;
    private const double RfidMarkerAnchorY = 18;
    private readonly Canvas _marker;
    private readonly Ellipse _stateIndicator;
    private readonly Ellipse _stateRing;
    private readonly Border _labelBorder;
    private readonly TextBlock _labelText;
    private readonly DeviceStateToBrushConverter _brushConverter = new();
    private readonly RfidStationConfig? _stationConfiguration;
    private bool _isAnnotationEditMode;

    public DeviceVisual(DeviceConfig device, string? displayName = null, RfidStationConfig? stationConfiguration = null)
    {
        Device = device;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? device.Name : displayName!;
        _stationConfiguration = stationConfiguration;
        Width = 112;
        Height = 36;
        Background = Brushes.Transparent;
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        Template = CreateTransparentButtonTemplate();
        UpdateToolTip();

        var content = new Grid
        {
            Background = Brushes.Transparent,
            Width = Width,
            Height = Height,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _marker = new Canvas
        {
            Width = 26,
            Height = 26,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            UseLayoutRounding = false,
            SnapsToDevicePixels = false
        };
        _stateRing = new Ellipse
        {
            Width = 24,
            Height = 24,
            Fill = GetBrush("DeviceMarkerBackgroundBrush", Brushes.Transparent),
            StrokeThickness = 2,
            UseLayoutRounding = false,
            SnapsToDevicePixels = false
        };
        _stateIndicator = new Ellipse
        {
            Width = 9,
            Height = 9,
            UseLayoutRounding = false,
            SnapsToDevicePixels = false
        };
        Canvas.SetLeft(_stateRing, (26 - 24) / 2d);
        Canvas.SetTop(_stateRing, (26 - 24) / 2d);
        Canvas.SetLeft(_stateIndicator, (26 - 9) / 2d);
        Canvas.SetTop(_stateIndicator, (26 - 9) / 2d);
        _marker.Children.Add(_stateRing);
        _marker.Children.Add(_stateIndicator);
        content.Children.Add(_marker);

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
        UpdateToolTip();
        _labelText.Text = DisplayName;
    }

    private void UpdateToolTip()
    {
        var hasEndpoint = false;
        var ipText = "未配置";
        var portText = "—";
        if (_stationConfiguration is not null && _stationConfiguration.TryResolveEndpoint(out var resolvedEndpoint))
        {
            hasEndpoint = true;
            ipText = resolvedEndpoint.Address.ToString();
            portText = resolvedEndpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var statusBrush = hasEndpoint && _stationConfiguration is { Enabled: true }
            ? GetBrush("SuccessBrush", Brushes.LimeGreen)
            : GetBrush("WarningBrush", Brushes.Gold);
        var stationId = string.IsNullOrWhiteSpace(Device.RfidStationId)
            ? "未绑定RFID基站"
            : Device.RfidStationId!;
        var stationName = _stationConfiguration?.Name;
        if (string.IsNullOrWhiteSpace(stationName))
        {
            stationName = null;
        }

        var cardBackground = GetBrush("CardBackgroundBrush", new SolidColorBrush(Color.FromRgb(4, 27, 49)));
        var accentBrush = GetBrush("HeaderAccentBrush", Brushes.DeepSkyBlue);
        const double calloutWidth = 360;
        const double calloutHeight = 96;
        const double calloutPointerY = calloutHeight / 2;
        var callout = new Grid
        {
            Width = calloutWidth,
            Height = calloutHeight
        };
        var outline = new Path
        {
            Data = Geometry.Parse(
                "M 36,1 H 350 Q 359,1 359,10 V 86 Q 359,95 350,95 H 36 Q 26,95 26,85 V 57 L 13,48 L 26,39 V 11 Q 26,1 36,1 Z"),
            Fill = cardBackground,
            Stroke = accentBrush,
            StrokeThickness = 1.25,
            Stretch = Stretch.Fill,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 10,
                ShadowDepth = 1,
                Opacity = 0.32
            }
        };
        callout.Children.Add(outline);

        var content = new Grid
        {
            Margin = new Thickness(36, 8, 12, 8)
        };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new TextBlock
        {
            Text = stationName is null ? DisplayName : $"{stationName}  ·  {DisplayName}",
            Foreground = GetBrush("TextPrimaryBrush", Brushes.White),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        content.Children.Add(heading);

        var identityChip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(36, 0, 196, 224)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(74, 0, 196, 224)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        var identityContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        identityContent.Children.Add(new Ellipse
        {
            Width = 5,
            Height = 5,
            Fill = statusBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        var identity = new TextBlock
        {
            Text = stationId,
            Foreground = accentBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center
        };
        identityContent.Children.Add(identity);
        identityChip.Child = identityContent;
        Grid.SetColumn(identityChip, 1);
        content.Children.Add(identityChip);

        var divider = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(75, 64, 171, 199)),
            VerticalAlignment = VerticalAlignment.Center,
            Height = 1
        };
        Grid.SetRow(divider, 1);
        Grid.SetColumnSpan(divider, 2);
        content.Children.Add(divider);

        var address = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        address.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        address.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        address.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        address.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var addressLabelBrush = GetBrush("MutedTextBrush", Brushes.LightSteelBlue);
        var addressValueBrush = hasEndpoint ? accentBrush : GetBrush("WarningBrush", Brushes.Gold);
        var ipLabel = new TextBlock
        {
            Text = hasEndpoint ? "IP 地址" : "通信地址",
            Foreground = addressLabelBrush,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        address.Children.Add(ipLabel);
        if (hasEndpoint)
        {
            var portLabel = new TextBlock
            {
                Text = "端口",
                Foreground = addressLabelBrush,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(portLabel, 1);
            address.Children.Add(portLabel);
        }

        var addressText = new TextBlock
        {
            Text = hasEndpoint ? ipText : "未配置",
            Foreground = addressValueBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(addressText, 1);
        address.Children.Add(addressText);
        if (hasEndpoint)
        {
            var portTextBlock = new TextBlock
            {
                Text = portText,
                Foreground = addressValueBrush,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(portTextBlock, 1);
            Grid.SetRow(portTextBlock, 1);
            address.Children.Add(portTextBlock);
        }

        Grid.SetRow(address, 2);
        Grid.SetColumnSpan(address, 2);
        content.Children.Add(address);
        callout.Children.Add(content);

        var toolTip = new ToolTip
        {
            Content = callout,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HasDropShadow = false,
            Placement = PlacementMode.Right,
            PlacementTarget = this,
            PlacementRectangle = new Rect(RfidMarkerAnchorX, RfidMarkerAnchorY, 0, 0),
            HorizontalOffset = 0,
            VerticalOffset = -calloutPointerY
        };
        ToolTip = toolTip;
        ToolTipService.SetInitialShowDelay(this, 250);
        ToolTipService.SetShowDuration(this, 10000);
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
