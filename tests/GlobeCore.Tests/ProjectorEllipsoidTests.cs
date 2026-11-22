using System.Windows.Media.Media3D;
using AstroCore.Astronomy;
using AstroCore.Earth;
using GlobeCore.Bodies;
using GlobeCore.Wpf.Rendering;

namespace GlobeCore.Tests;

public class ProjectorEllipsoidTests
{
    [Fact]
    public void HitsFlattenedPole()
    {
        Assert.True(Projector.RayHitsEllipsoid(new Point3D(0, 0, 5), new Vector3D(0, 0, -1), 0.902, out var hit));
        Assert.Equal(0.902, hit.Z, 12);
        Assert.Equal(0, hit.X, 12);
        Assert.Equal(0, hit.Y, 12);
    }

    [Fact]
    public void HitsEquatorAtFullRadius()
    {
        Assert.True(Projector.RayHitsEllipsoid(new Point3D(5, 0, 0), new Vector3D(-1, 0, 0), 0.902, out var hit));
        Assert.Equal(1, hit.X, 12);
    }

    [Fact]
    public void MissesAndBehindRays()
    {
        Assert.False(Projector.RayHitsEllipsoid(new Point3D(0, -5, 0.95), new Vector3D(0, 1, 0), 0.902, out _));   // passes above the flattened pole
        Assert.True(Projector.RayHitsUnitSphere(new Point3D(0, -5, 0.95), new Vector3D(0, 1, 0), out _));          // ...but not the sphere
        Assert.False(Projector.RayHitsEllipsoid(new Point3D(0, -5, 0), new Vector3D(0, -1, 0), 0.902, out _));     // body behind the eye
        Assert.False(Projector.RayHitsEllipsoid(new Point3D(0, -5, 0), new Vector3D(0, 0, 0), 0.902, out _));
    }

    [Fact]
    public void MatchesSphereAtRatioOne()
    {
        var rng = new Random(12345);
        int hits = 0;
        for (int i = 0; i < 1000; i++)
        {
            var o = new Point3D(rng.NextDouble() * 10 - 5, rng.NextDouble() * 10 - 5, rng.NextDouble() * 10 - 5);
            var jitter = new Vector3D(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
            var d = -(Vector3D)o + jitter * 0.6;   // roughly toward the origin, so many hit and some miss
            bool a = Projector.RayHitsUnitSphere(o, d, out var ha);
            bool b = Projector.RayHitsEllipsoid(o, d, 1.0, out var hb);
            Assert.Equal(a, b);
            if (!a) continue;
            hits++;
            Assert.True(Math.Abs(ha.X - hb.X) < 1e-12 && Math.Abs(ha.Y - hb.Y) < 1e-12 && Math.Abs(ha.Z - hb.Z) < 1e-12, $"{ha} vs {hb}");
        }
        Assert.InRange(hits, 100, 1000);
    }

    [Theory]
    [InlineData(0.0, 179.999999, 0.902)]
    [InlineData(0.0, -179.999999, 0.902)]
    [InlineData(10.0, 180.0, 0.902)]
    [InlineData(89.9, 33.0, 0.902)]
    [InlineData(-89.9, -120.0, 0.902)]
    [InlineData(89.9, 180.0, 1.0)]
    [InlineData(-89.9, 179.5, 0.5)]
    public void DatelineAndPoleLongitudes(double lat, double lon, double ratio)
    {
        double la = lat * Math.PI / 180, lo = lon * Math.PI / 180;
        var u = new Vector3D(Math.Cos(la) * Math.Cos(lo), Math.Cos(la) * Math.Sin(lo), Math.Sin(la));
        var origin = new Point3D(u.X * 4, u.Y * 4, u.Z * 4);
        Assert.True(Projector.RayHitsEllipsoid(origin, -u, ratio, out var hit));
        SurfacePoint sp = GeoPosition.FromVector(new Vector3d(hit.X, hit.Y, hit.Z));
        Assert.Equal(lat, sp.LatitudeDeg, 9);
        double dLon = ((sp.LongitudeDeg - lon + 540) % 360) - 180;   // wrap-safe difference
        Assert.True(Math.Abs(dLon) < 1e-9, $"lon {sp.LongitudeDeg} vs {lon}");
    }

    [Fact]
    public void TryHitBodyMatchesTryHitUnitSphereAtRatioOne()
    {
        var cam = new PerspectiveCamera(new Point3D(0, -5, 0), new Vector3D(0, 5, 0), new Vector3D(0, 0, 1), 60);
        var p = new Projector(cam, 800, 600);
        Assert.True(p.TryHitUnitSphere(new System.Windows.Point(420, 280), out var a));
        Assert.True(p.TryHitBody(new System.Windows.Point(420, 280), 1.0, out var b));
        Assert.Equal(a, b);
    }

    [Fact]
    public void TryHitBodyOnFlattenedBodyIsOnItsSurfaceAndProjectsBack()
    {
        var cam = new PerspectiveCamera(new Point3D(0, 0, 5), new Vector3D(0, 0, -5), new Vector3D(0, 1, 0), 60);
        var p = new Projector(cam, 800, 600);
        Assert.True(p.TryHitBody(new System.Windows.Point(400, 300), 0.902, out var hit));
        Assert.Equal(0.902, hit.Z, 12);
        Assert.True(p.TryHitBody(new System.Windows.Point(430, 270), 0.902, out var h2));
        Assert.Equal(1, h2.X * h2.X + h2.Y * h2.Y + h2.Z * h2.Z / (0.902 * 0.902), 9);
        Assert.True(p.TryProject(h2, out var back));
        Assert.Equal(430, back.X, 6); Assert.Equal(270, back.Y, 6);
    }
    // Final review minor 12: the polar ratio must be positive and finite.
    [Theory] [InlineData(0.0)] [InlineData(-0.902)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidPolarRatio(double ratio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Projector.RayHitsEllipsoid(new Point3D(0, 0, 5), new Vector3D(0, 0, -1), ratio, out _));
        var cam = new PerspectiveCamera(new Point3D(0, 0, 5), new Vector3D(0, 0, -5), new Vector3D(0, 1, 0), 60);
        var p = new Projector(cam, 800, 600);
        Assert.Throws<ArgumentOutOfRangeException>(() => p.TryHitBody(new System.Windows.Point(400, 300), ratio, out _));
    }
}
