using AstroCore.Earth;
using GlobeCore.Bodies;
using GlobeCore.Geo;

namespace GlobeCore.Tests;

public class GeoFormatTests
{
    [Theory] [InlineData(-12.34, -45.0, "12.3°S 45.0°W")] [InlineData(0, 180, "0.0°N 180.0°E")] [InlineData(18.05, 120.0, "18.1°N 120.0°E")]
    [InlineData(-0.04, -0.04, "0.0°N 0.0°E")]
    public void Formats(double lat, double lon, string expected) => Assert.Equal(expected, GeoFormat.LatLon(new(lat, lon)));
    // Final review minor 2: longitudes are stored east-positive; a west-positive body (Mars) shows them as 0–360°W.
    [Theory]
    [InlineData(-12.34, -45.0, "12.3°S 45.0°W")] [InlineData(0, 45, "0.0°N 315.0°W")] [InlineData(0, 180, "0.0°N 180.0°W")]
    [InlineData(0, 0, "0.0°N 0.0°W")] [InlineData(0, -0.04, "0.0°N 0.0°W")] [InlineData(0, 0.04, "0.0°N 0.0°W")]
    [InlineData(18.05, -179.96, "18.1°N 180.0°W")]
    public void FormatsWestPositive(double lat, double lon, string expected) =>
        Assert.Equal(expected, GeoFormat.LatLon(new(lat, lon), LongitudeConvention.WestPositive));

    [Theory] [InlineData(-12.34, -45.0)] [InlineData(0, 180)] [InlineData(18.05, 120.0)] [InlineData(-0.04, -0.04)]
    public void EastPositiveConventionIsTheDefaultFormat(double lat, double lon) =>
        Assert.Equal(GeoFormat.LatLon(new(lat, lon)), GeoFormat.LatLon(new(lat, lon), LongitudeConvention.EastPositive));
}
