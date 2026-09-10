namespace MineRailMonitor.Core.Services;

public struct MapPixelPoint
{
    public MapPixelPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; private set; }

    public double Y { get; private set; }
}

public static class MapCoordinateMapper
{
    public static MapPixelPoint ToMapPixels(
        NormalizedPoint normalizedPoint,
        double mapWidth,
        double mapHeight)
    {
        if (mapWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapWidth), "Map width must be positive.");
        }

        if (mapHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapHeight), "Map height must be positive.");
        }

        return new MapPixelPoint(
            normalizedPoint.X * mapWidth,
            normalizedPoint.Y * mapHeight);
    }

    public static NormalizedPoint ToNormalized(
        MapPixelPoint mapPoint,
        double mapWidth,
        double mapHeight)
    {
        if (mapWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapWidth), "Map width must be positive.");
        }

        if (mapHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapHeight), "Map height must be positive.");
        }

        return new NormalizedPoint(
            mapPoint.X / mapWidth,
            mapPoint.Y / mapHeight);
    }
}
