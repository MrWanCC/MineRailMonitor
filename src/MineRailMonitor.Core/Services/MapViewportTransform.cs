namespace MineRailMonitor.Core.Services;

public static class MapViewportTransform
{
    public static MapPixelPoint ScreenToMapLocal(
        MapPixelPoint screenPoint,
        double scale,
        double offsetX,
        double offsetY)
    {
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Viewport scale must be positive.");
        }

        return new MapPixelPoint(
            (screenPoint.X - offsetX) / scale,
            (screenPoint.Y - offsetY) / scale);
    }

    public static MapPixelPoint MapLocalToScreen(
        MapPixelPoint mapPoint,
        double scale,
        double offsetX,
        double offsetY)
    {
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Viewport scale must be positive.");
        }

        return new MapPixelPoint(
            mapPoint.X * scale + offsetX,
            mapPoint.Y * scale + offsetY);
    }
}
