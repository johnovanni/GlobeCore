using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AstroCore.Earth;
using GlobeCore.Wpf.Viewport;

namespace GlobeCore.Tests;

public class BodyMeshTests
{
    /// <summary>The EarthWatch3D GlobeMesh.Create of before the move, pasted verbatim as the reference.</summary>
    private static MeshGeometry3D OldGlobeMesh(int lonSegments = 360, int latSegments = 180)
    {
        var positions = new Point3DCollection((lonSegments + 1) * (latSegments + 1));
        var tex = new PointCollection((lonSegments + 1) * (latSegments + 1));
        for (int i = 0; i <= latSegments; i++)
        {
            double v = (double)i / latSegments;
            double lat = (90 - 180 * v) * Math.PI / 180;
            double cl = Math.Cos(lat), sl = Math.Sin(lat);
            for (int j = 0; j <= lonSegments; j++)
            {
                double u = (double)j / lonSegments;
                double lon = (360 * u - 180) * Math.PI / 180;
                positions.Add(new Point3D(cl * Math.Cos(lon), cl * Math.Sin(lon), sl));
                tex.Add(new Point(u, v));
            }
        }
        var idx = new Int32Collection(lonSegments * latSegments * 6);
        int stride = lonSegments + 1;
        for (int i = 0; i < latSegments; i++)
            for (int j = 0; j < lonSegments; j++)
            {
                int a = i * stride + j, b = a + stride, c = a + 1, d = b + 1;
                idx.Add(a); idx.Add(b); idx.Add(c);
                idx.Add(c); idx.Add(b); idx.Add(d);
            }
        return new MeshGeometry3D { Positions = positions, TextureCoordinates = tex, TriangleIndices = idx };
    }

    [Fact]
    public void PolarRatioOneEqualsOldMesh()
    {
        var old = OldGlobeMesh();
        var m = BodyMesh.Create();
        Assert.Equal(old.Positions.Count, m.Positions.Count);
        for (int i = 0; i < old.Positions.Count; i++) Assert.Equal(old.Positions[i], m.Positions[i]);
        Assert.Equal(old.TextureCoordinates.Count, m.TextureCoordinates.Count);
        for (int i = 0; i < old.TextureCoordinates.Count; i++) Assert.Equal(old.TextureCoordinates[i], m.TextureCoordinates[i]);
        Assert.Equal(old.TriangleIndices.ToArray(), m.TriangleIndices.ToArray());
    }

    [Fact]
    public void FlattenedPoleAtPolarRatio()
    {
        var m = BodyMesh.Create(0.902, 36, 18);
        Assert.Equal(0.902, m.Positions[0].Z, 12);                       // north pole vertex (first row)
        Assert.Equal(-0.902, m.Positions[^1].Z, 12);                     // south pole vertex (last row)
        Assert.All(m.Positions, p => Assert.Equal(1, Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z / (0.902 * 0.902)), 9));
    }

    [Fact]
    public void TextureAndIndicesIgnorePolarRatio()
    {
        var a = BodyMesh.Create(1.0, 36, 18);
        var b = BodyMesh.Create(0.902, 36, 18);
        Assert.Equal(a.TextureCoordinates.ToArray(), b.TextureCoordinates.ToArray());
        Assert.Equal(a.TriangleIndices.ToArray(), b.TriangleIndices.ToArray());
    }

    [Fact]
    public void VerticesOnUnitSphereAndTextureMatchesLatLon()
    {
        var m = BodyMesh.Create(1.0, 36, 18);
        Assert.All(m.Positions, p => Assert.Equal(1, ((Vector3D)p).Length, 9));
        for (int i = 0; i < m.Positions.Count; i++)
        {
            var g = GeoPosition.FromVector(new(m.Positions[i].X, m.Positions[i].Y, m.Positions[i].Z));
            if (Math.Abs(g.LatitudeDeg) > 89.9) continue;
            Assert.Equal((90 - g.LatitudeDeg) / 180, m.TextureCoordinates[i].Y, 6);
            double du = (m.TextureCoordinates[i].X - (g.LongitudeDeg + 180) / 360) % 1;
            Assert.True(Math.Abs(du) < 1e-6 || Math.Abs(Math.Abs(du) - 1) < 1e-6);
        }
    }

    [Theory] [InlineData(0.0)] [InlineData(-1.0)] [InlineData(1.5)] [InlineData(double.NaN)]
    public void RejectsInvalidPolarRatio(double ratio) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BodyMesh.Create(ratio, 36, 18));
}
