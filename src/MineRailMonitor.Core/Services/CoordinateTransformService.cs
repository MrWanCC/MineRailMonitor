namespace MineRailMonitor.Core.Services;

public sealed class CoordinateTransformService : ICoordinateTransformService
{
    public NormalizedPoint ToNormalized(CadPoint point, CadBounds bounds)
    {
        ValidateBounds(bounds);

        var normalizedX = (point.X - bounds.MinX) / (bounds.MaxX - bounds.MinX);
        var normalizedY = (bounds.MaxY - point.Y) / (bounds.MaxY - bounds.MinY);

        return new NormalizedPoint(normalizedX, normalizedY);
    }

    public CadPoint ToCad(NormalizedPoint point, CadBounds bounds)
    {
        ValidateBounds(bounds);

        var cadX = bounds.MinX + point.X * (bounds.MaxX - bounds.MinX);
        var cadY = bounds.MaxY - point.Y * (bounds.MaxY - bounds.MinY);
        return new CadPoint(cadX, cadY);
    }

    private static void ValidateBounds(CadBounds bounds)
    {
        if (!IsFinite(bounds.MinX) ||
            !IsFinite(bounds.MaxX) ||
            !IsFinite(bounds.MinY) ||
            !IsFinite(bounds.MaxY) ||
            bounds.MaxX <= bounds.MinX ||
            bounds.MaxY <= bounds.MinY)
        {
            throw new ArgumentException("CAD bounds must be finite and have a positive width and height.", nameof(bounds));
        }
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
