namespace MineRailMonitor.Core.Services;

public static class MapLabelTransform
{
    public const double MinimumFontSize = 10;
    public const double MaximumFontSize = 18;

    public static double ToWpfRotation(double cadRotation)
    {
        if (double.IsNaN(cadRotation) || double.IsInfinity(cadRotation))
        {
            return 0;
        }

        var angle = -cadRotation % 360;
        while (angle <= -180)
        {
            angle += 360;
        }

        while (angle > 180)
        {
            angle -= 360;
        }

        if (angle > 90)
        {
            angle -= 180;
        }
        else if (angle < -90)
        {
            angle += 180;
        }

        return angle;
    }

    public static double GetFontSize(double textHeight)
    {
        if (double.IsNaN(textHeight) || double.IsInfinity(textHeight) || textHeight <= 0)
        {
            return MinimumFontSize;
        }

        var size = Math.Round(9 + textHeight * 0.9, MidpointRounding.AwayFromZero);
        return Math.Max(MinimumFontSize, Math.Min(MaximumFontSize, size));
    }
}
