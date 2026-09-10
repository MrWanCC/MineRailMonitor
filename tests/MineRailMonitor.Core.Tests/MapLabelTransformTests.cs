using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;

namespace MineRailMonitor.Core.Tests;

public sealed class MapLabelTransformTests
{
    [Fact]
    public void Converts_cad_rotation_to_readable_wpf_rotation()
    {
        Assert.Equal(77.019503, MapLabelTransform.ToWpfRotation(282.980497), 6);
        Assert.Equal(58.980177, MapLabelTransform.ToWpfRotation(301.019823), 6);
        Assert.Equal(-64.536893, MapLabelTransform.ToWpfRotation(64.536893), 6);
    }

    [Theory]
    [InlineData(10.0, 18.0)]
    [InlineData(5.0, 14.0)]
    [InlineData(3.0, 12.0)]
    [InlineData(2.0, 11.0)]
    [InlineData(1.5, 10.0)]
    public void Maps_text_height_to_a_readable_clamped_font_size(double textHeight, double expected)
    {
        Assert.Equal(expected, MapLabelTransform.GetFontSize(textHeight));
    }

    [Fact]
    public void Map_point_contains_only_location_data_and_no_protocol_address()
    {
        var point = new MapPoint
        {
            Id = "Y6-1",
            Name = "Y6-1",
            CadX = 2890.541288,
            CadY = 1350.068207,
            Enabled = true
        };

        Assert.Equal("Y6-1", point.Id);
        Assert.Equal(2890.541288, point.CadX);
        Assert.DoesNotContain(typeof(MapPoint).GetProperties(), property => property.Name == "ProtocolAddress");
    }
}
