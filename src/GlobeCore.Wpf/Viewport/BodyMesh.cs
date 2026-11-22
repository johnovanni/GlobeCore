using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using GlobeCore.Bodies;

namespace GlobeCore.Wpf.Viewport;

public static class BodyMesh
{
    /// <summary>
    /// Unit sphere (equatorial radius 1) flattened along Z by <paramref name="polarRatio"/> (polar / equatorial radius; 1 = sphere),
    /// Z up, +X at (0N, 0E). Texture u = (lon+180)/360, v = (90-lat)/180, with lat planetocentric: each vertex is
    /// <see cref="Ellipsoid.ModelPoint(double, double, double)"/> of its row's latitude and column's longitude, on the
    /// radial line of that latitude, so the texture agrees with picking (<c>Projector.TryHitBody</c> and
    /// <c>GeoPosition.FromVector</c>) and with markers. At a ratio of 1 it is the unit sphere EarthWatch3D always drew,
    /// to the bit. The seam column is duplicated (u = 0 and u = 1) so the texture never wraps inside a triangle.
    /// </summary>
    public static MeshGeometry3D Create(double polarRatio = 1.0, int lonSegments = 360, int latSegments = 180)
    {
        if (!(polarRatio > 0 && polarRatio <= 1)) throw new ArgumentOutOfRangeException(nameof(polarRatio), polarRatio, "Polar ratio must be in (0, 1].");
        ArgumentOutOfRangeException.ThrowIfLessThan(lonSegments, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(latSegments, 2);
        var positions = new Point3DCollection((lonSegments + 1) * (latSegments + 1));
        var tex = new PointCollection((lonSegments + 1) * (latSegments + 1));
        for (int i = 0; i <= latSegments; i++)
        {
            double v = (double)i / latSegments;
            double latDeg = 90 - 180 * v;
            for (int j = 0; j <= lonSegments; j++)
            {
                double u = (double)j / lonSegments;
                var p = Ellipsoid.ModelPoint(latDeg, 360 * u - 180, polarRatio);
                positions.Add(new Point3D(p.X, p.Y, p.Z));
                tex.Add(new Point(u, v));
            }
        }
        var idx = new Int32Collection(lonSegments * latSegments * 6);
        int stride = lonSegments + 1;
        for (int i = 0; i < latSegments; i++)
            for (int j = 0; j < lonSegments; j++)
            {
                int a = i * stride + j, b = a + stride, c = a + 1, d = b + 1;
                idx.Add(a); idx.Add(b); idx.Add(c);   // counter-clockwise seen from outside
                idx.Add(c); idx.Add(b); idx.Add(d);
            }
        return new MeshGeometry3D { Positions = positions, TextureCoordinates = tex, TriangleIndices = idx };
    }
}
