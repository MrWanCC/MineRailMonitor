using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MineRailMonitor.Core.Models;

namespace MineRailMonitor.Converters;

public sealed class DeviceStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value is DeviceStatus status
            ? status switch
            {
                DeviceStatus.Normal => Color.FromRgb(44, 196, 132),
                DeviceStatus.Identifying => Color.FromRgb(46, 153, 255),
                DeviceStatus.Alarm => Color.FromRgb(226, 76, 76),
                DeviceStatus.Offline => Color.FromRgb(119, 133, 149),
                _ => Color.FromRgb(119, 133, 149)
            }
            : Color.FromRgb(119, 133, 149);

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
