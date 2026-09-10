namespace MineRailMonitor.Core.Services;

public struct CadPoint
{
    public CadPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; private set; }

    public double Y { get; private set; }
}

public struct CadBounds
{
    public CadBounds(double minX, double maxX, double minY, double maxY)
    {
        MinX = minX;
        MaxX = maxX;
        MinY = minY;
        MaxY = maxY;
    }

    public double MinX { get; private set; }

    public double MaxX { get; private set; }

    public double MinY { get; private set; }

    public double MaxY { get; private set; }
}

public struct NormalizedPoint
{
    public NormalizedPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; private set; }

    public double Y { get; private set; }
}
