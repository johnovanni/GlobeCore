using AstroCore.Astronomy;
using AstroCore.Bodies;
using AstroCore.Earth;
using AstroCore.Orbits;
using GlobeCore.Bodies;
using GlobeCore.Imaging;

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

    // Final review minor 12: a body needs positive, finite radii.
    [Theory]
    [InlineData(0.0, 1.0)] [InlineData(1.0, 0.0)] [InlineData(-6371.0, 6371.0)] [InlineData(6371.0, -1.0)]
    [InlineData(double.NaN, 1.0)] [InlineData(1.0, double.PositiveInfinity)]
    public void EllipsoidRejectsNonPositiveRadii(double eq, double polar) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Ellipsoid(eq, polar));

    // Final review F1: one helper for surface point <-> model point (equatorial radius 1).
    [Fact]
    public void ModelPointAtRatioOneIsTheUnitVector()
    {
        var rng = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            double lat = rng.NextDouble() * 180 - 90, lon = rng.NextDouble() * 360 - 180;
            var u = new GeoPosition(lat, lon).ToUnitVector();
            var m = Ellipsoid.ModelPoint(lat, lon, 1.0);
            Assert.True(Math.Abs(u.X - m.X) < 1e-15 && Math.Abs(u.Y - m.Y) < 1e-15 && Math.Abs(u.Z - m.Z) < 1e-15, $"{u} vs {m}");
            // ...and is exactly the old globe mesh's arithmetic
            double la = lat * Math.PI / 180, lo = lon * Math.PI / 180;
            Assert.Equal(new Vector3d(Math.Cos(la) * Math.Cos(lo), Math.Cos(la) * Math.Sin(lo), Math.Sin(la)), m);
        }
    }

    [Theory] [InlineData(0.902)] [InlineData(0.99411)] [InlineData(1.0)]
    public void ModelPointRoundTripsAndLiesOnTheEllipsoid(double ratio)
    {
        var shape = new Ellipsoid(1000, 1000 * ratio);
        var rng = new Random(11);
        for (int i = 0; i < 500; i++)
        {
            var sp = new SurfacePoint(rng.NextDouble() * 179.8 - 89.9, rng.NextDouble() * 359.8 - 179.9);
            Vector3d v = shape.ModelPoint(sp);
            Assert.Equal(1, Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z / (shape.PolarRatio * shape.PolarRatio)), 12);
            var back = Ellipsoid.SurfacePointOf(v);
            Assert.Equal(sp.LatitudeDeg, back.LatitudeDeg, 9);
            Assert.Equal(sp.LongitudeDeg, back.LongitudeDeg, 9);
        }
        Assert.Equal(ratio, Ellipsoid.ModelPoint(90, 0, ratio).Z, 12);
        Assert.Equal(1.0, Ellipsoid.ModelPoint(0, 0, ratio).X, 12);
    }

    [Theory] [InlineData(0.0)] [InlineData(-0.5)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void ModelPointRejectsInvalidPolarRatio(double ratio) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Ellipsoid.ModelPoint(10, 20, ratio));

    // Final review F2: the twilight comes from the body.
    [Fact]
    public void IlluminationAtUsesTheBodysTwilightAndSubSolarPoint()
    {
        var t = new DateTime(2026, 3, 20, 12, 34, 56, DateTimeKind.Utc);
        var body = new Body("earth", "Earth", new Ellipsoid(6371, 6371), BodyRotations.Earth, null,
            LongitudeConvention.EastPositive, new SunLit(-6));
        var light = body.IlluminationAt(t, 0.7, false);
        Assert.NotNull(light);
        Assert.Equal(new IlluminationState(body.SubSolarPoint(t), 0.7, false, -6), light);
        Assert.Equal(-12, (body with { Illumination = new SunLit() }).IlluminationAt(t, 0.7, false)!.TwilightDeg);
        Assert.Null((body with { Illumination = new SelfLuminous() }).IlluminationAt(t, 0.7, false));
    }

    [Theory] [InlineData(0.0)] [InlineData(3.0)] [InlineData(-91.0)] [InlineData(double.NaN)]
    public void TwilightMustBeBelowTheHorizon(double twilight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SunLit(twilight));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IlluminationState(new(0, 0), 0.85, true, twilight));
    }

    [Fact]
    public void DistanceOneDegreeAtEquator() =>
        Assert.Equal(111.19, GlobeCore.Geo.GeoMath.DistanceKm(new(0, 0), new(0, 1), 6371.0), 1);
}
