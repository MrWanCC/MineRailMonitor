namespace MineRailMonitor.Core.Services;

public interface ICoordinateTransformService
{
    NormalizedPoint ToNormalized(CadPoint point, CadBounds bounds);

    CadPoint ToCad(NormalizedPoint point, CadBounds bounds);
}
