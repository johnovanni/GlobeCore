using AstroCore.Bodies;
using AstroCore.Orbits;
using GlobeCore.Bodies;

namespace GlobeCore.Tests;

public class BodyTests
{
    [Fact]
    public void MeanRadius()
    {
        var (a, b) = BodyConstants.Radii("mars");
        Assert.Equal(3389.53, new Ellipsoid(a, b).MeanRadiusKm, 2);
        Assert.Equal(b / a, new Ellipsoid(a, b).PolarRatio, 12);
    }

    [Fact]
    public void EarthLikeBodyMatchesAstroCoreSubSolarPoint()
    {
        var earth = new Body("earth", "Earth", new Ellipsoid(6371, 6371), BodyRotations.Earth, null,
            LongitudeConvention.EastPositive, new SunLit());
        var t = new DateTime(2026, 3, 20, 12, 34, 56, DateTimeKind.Utc);
        var expected = AstroCore.Earth.SubPoints.SubSolarPoint(t);
        var got = earth.SubSolarPoint(t);
        Assert.Equal(expected.LatitudeDeg, got.LatitudeDeg, 9);
        Assert.Equal(expected.LongitudeDeg, got.LongitudeDeg, 9);
    }

    [Fact]
    public void MarsBodyUsesItsOrbit()
    {
        var mars = new Body("mars", "Mars", new Ellipsoid(3396.19, 3376.20), BodyRotations.Mars,
            PlanetOrbits.Mars.PositionAt, LongitudeConvention.EastPositive, new SunLit());
        var p = mars.SubSolarPoint(new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc));
        Assert.InRange(p.LatitudeDeg, -30, 30);
        Assert.InRange(p.LongitudeDeg, -180, 180);
    }

    [Fact]
    public void DistanceOneDegreeAtEquator() =>
        Assert.Equal(111.19, GlobeCore.Geo.GeoMath.DistanceKm(new(0, 0), new(0, 1), 6371.0), 1);
}
