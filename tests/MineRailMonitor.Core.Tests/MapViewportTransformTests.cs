using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class MapViewportTransformTests
{
    [Fact]
    public void Map_local_uses_one_scale_and_translation_for_screen_position()
    {
        var local = new MapPixelPoint(500, 300);

        var scaleOne = MapViewportTransform.MapLocalToScreen(local, 1, 0, 0);
        var scaleTwo = MapViewportTransform.MapLocalToScreen(local, 2, 0, 0);
        var scaleTwoWithTranslation = MapViewportTransform.MapLocalToScreen(local, 2, 100, 50);

        Assert.Equal(500, scaleOne.X);
        Assert.Equal(300, scaleOne.Y);
        Assert.Equal(1000, scaleTwo.X);
        Assert.Equal(600, scaleTwo.Y);
        Assert.Equal(1100, scaleTwoWithTranslation.X);
        Assert.Equal(650, scaleTwoWithTranslation.Y);

        var roundTrip = MapViewportTransform.ScreenToMapLocal(
            scaleTwoWithTranslation,
            2,
            100,
            50);
        Assert.Equal(500, roundTrip.X);
        Assert.Equal(300, roundTrip.Y);
    }

    [Fact]
    public void Screen_to_map_local_removes_zoom_and_pan()
    {
        var local = MapViewportTransform.ScreenToMapLocal(
            new MapPixelPoint(850, 650),
            scale: 2,
            offsetX: 50,
            offsetY: 150);

        Assert.Equal(400, local.X);
        Assert.Equal(250, local.Y);
    }

    [Fact]
    public void Screen_round_trip_is_stable_after_zoom_and_pan()
    {
        var local = new MapPixelPoint(412.5, 287.25);
        var screen = MapViewportTransform.MapLocalToScreen(local, 1.75, -96, 132);
        var result = MapViewportTransform.ScreenToMapLocal(screen, 1.75, -96, 132);

        Assert.InRange(Math.Abs(result.X - local.X), 0, 0.0000001);
        Assert.InRange(Math.Abs(result.Y - local.Y), 0, 0.0000001);
    }

    [Fact]
    public void Cad_overlay_anchor_keeps_the_same_map_position_after_fit_zoom_and_pan()
    {
        var cad = new CadPoint(2771.8203, 1916.0352);
        var bounds = new CadBounds(2700, 2800, 1900, 2100);
        var normalized = new CoordinateTransformService().ToNormalized(cad, bounds);
        var mapLocal = MapCoordinateMapper.ToMapPixels(normalized, 1600, 900);

        var views = new[]
        {
            (scale: 0.5, offsetX: 0.0, offsetY: 30.0),
            (scale: 1.25, offsetX: -180.0, offsetY: 90.0),
            (scale: 2.4, offsetX: -840.0, offsetY: -250.0)
        };

        foreach (var view in views)
        {
            var screen = MapViewportTransform.MapLocalToScreen(mapLocal, view.scale, view.offsetX, view.offsetY);
            var result = MapViewportTransform.ScreenToMapLocal(screen, view.scale, view.offsetX, view.offsetY);
            Assert.InRange(Math.Abs(result.X - mapLocal.X), 0, 0.0000001);
            Assert.InRange(Math.Abs(result.Y - mapLocal.Y), 0, 0.0000001);
        }
    }
}
