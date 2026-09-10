using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class CoordinateTransformServiceTests
{
    private static readonly CadBounds Bounds = new(2700, 2800, 1900, 2100);
    private readonly CoordinateTransformService _service = new();

    [Fact]
    public void Converts_cad_upper_left_to_normalized_upper_left()
    {
        var result = _service.ToNormalized(new CadPoint(2700, 2100), Bounds);

        Assert.Equal(0, result.X);
        Assert.Equal(0, result.Y);
    }

    [Fact]
    public void Converts_cad_lower_right_to_normalized_lower_right()
    {
        var result = _service.ToNormalized(new CadPoint(2800, 1900), Bounds);

        Assert.Equal(1, result.X);
        Assert.Equal(1, result.Y);
    }

    [Fact]
    public void Converts_cad_center_to_normalized_center()
    {
        var result = _service.ToNormalized(new CadPoint(2750, 2000), Bounds);

        Assert.Equal(0.5, result.X);
        Assert.Equal(0.5, result.Y);
    }

    [Fact]
    public void Flips_cad_y_axis_before_returning_normalized_y()
    {
        var cadTop = _service.ToNormalized(new CadPoint(2750, 2100), Bounds);
        var cadBottom = _service.ToNormalized(new CadPoint(2750, 1900), Bounds);

        Assert.Equal(0, cadTop.Y);
        Assert.Equal(1, cadBottom.Y);
    }

    [Fact]
    public void Converts_normalized_coordinates_back_to_cad_and_flips_y_axis()
    {
        var result = _service.ToCad(new NormalizedPoint(0.25, 0.75), Bounds);

        Assert.Equal(2725, result.X);
        Assert.Equal(1950, result.Y);
    }

    [Fact]
    public void Forward_then_inverse_conversion_returns_the_original_cad_point()
    {
        var original = new CadPoint(2771.8203, 1916.0352);
        var normalized = _service.ToNormalized(original, Bounds);
        var result = _service.ToCad(normalized, Bounds);

        Assert.InRange(Math.Abs(result.X - original.X), 0, 0.0000001);
        Assert.InRange(Math.Abs(result.Y - original.Y), 0, 0.0000001);
    }

    [Theory]
    [InlineData(2800, 2800, 1900, 2100)]
    [InlineData(2700, 2800, 1900, 1900)]
    public void Rejects_non_positive_cad_bounds(double minX, double maxX, double minY, double maxY)
    {
        var invalidBounds = new CadBounds(minX, maxX, minY, maxY);

        Assert.Throws<ArgumentException>(() =>
            _service.ToNormalized(new CadPoint(2750, 2000), invalidBounds));
    }
}
