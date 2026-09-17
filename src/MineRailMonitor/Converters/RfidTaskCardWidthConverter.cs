using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MineRailMonitor.Converters;

public sealed class RfidTaskCardWidthConverter : IMultiValueConverter
{
    private const double DefaultCardWidth = 205;
    private const int FilledCardLimit = 6;
    private const double ScrollViewerHorizontalPadding = 8;
    private const double CardRightMargin = 8;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 ||
            values[0] is not double viewportWidth ||
            values[1] is not int totalCount ||
            totalCount <= 0 ||
            double.IsNaN(viewportWidth) ||
            double.IsInfinity(viewportWidth) ||
            viewportWidth <= ScrollViewerHorizontalPadding)
        {
            return DefaultCardWidth;
        }

        var count = Math.Min(totalCount, FilledCardLimit);
        var width = (viewportWidth - ScrollViewerHorizontalPadding - CardRightMargin * count) / count;
        return width > 0 ? width : DefaultCardWidth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
