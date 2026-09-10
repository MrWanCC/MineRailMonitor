using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class MapCoordinateMapperTests
{
    [Fact]
    public void Maps_normalized_coordinates_to_the_same_map_pixel_space_used_by_overlays()
    {
        var result = MapCoordinateMapper.ToMapPixels(new NormalizedPoint(0.25, 0.75), 1200, 800);

        Assert.Equal(300, result.X);
        Assert.Equal(600, result.Y);
    }

    [Fact]
    public void Rejects_non_positive_map_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MapCoordinateMapper.ToMapPixels(new NormalizedPoint(0.5, 0.5), 0, 800));
    }

    [Fact]
    public void Converts_map_pixels_back_to_normalized_coordinates()
    {
        var result = MapCoordinateMapper.ToNormalized(new MapPixelPoint(300, 600), 1200, 800);

        Assert.Equal(0.25, result.X);
        Assert.Equal(0.75, result.Y);
    }

    [Theory]
    [InlineData(1200, 800, 300, 600, 0.25, 0.75)]
    [InlineData(1600, 900, 800, 225, 0.5, 0.25)]
    public void Uses_the_actual_map_viewport_dimensions(
        double mapWidth,
        double mapHeight,
        double pixelX,
        double pixelY,
        double expectedX,
        double expectedY)
    {
        var result = MapCoordinateMapper.ToNormalized(
            new MapPixelPoint(pixelX, pixelY),
            mapWidth,
            mapHeight);

        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
    }
}
