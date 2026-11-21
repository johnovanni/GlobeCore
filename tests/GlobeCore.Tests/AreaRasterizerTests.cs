using System.Windows.Media;
using AstroCore.Earth;
using GlobeCore.Wpf.Rendering;
using GlobeCore.Geo;
using GlobeCore.Imaging;

namespace GlobeCore.Tests;

public class AreaRasterizerTests
{
    private static GeoPolygon Box(double lat0, double lon0, double lat1, double lon1) =>
        new([[new(lat0, lon0), new(lat0, lon1), new(lat1, lon1), new(lat1, lon0), new(lat0, lon0)]]);

    private static (byte B, byte G, byte R, byte A) Px(Bgra32Image img, int x, int y)
    {
        int i = (y * img.Width + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    private static Bgra32Image Raster(IEnumerable<(GeoPolygon, Color)> areas, int w, int h)
    {
        Bgra32Image? r = null;
        Sta.Run(() => r = AreaRasterizer.Rasterize(areas, w, h));
        return r!;
    }

    [Fact]
    public void FillsInteriorAtThirtyFivePercent()
    {   // lon 0..10 -> x 180..190, lat 0..10 -> y 80..90 on 360x180
        var img = Raster([(Box(0, 0, 10, 10), Color.FromRgb(255, 0, 0))], 360, 180);
        var c = Px(img, 185, 85);
        Assert.InRange(c.A, 88, 90);
        Assert.Equal(255, c.R); Assert.Equal(0, c.G); Assert.Equal(0, c.B);
        Assert.Equal(0, Px(img, 50, 20).A);
    }

    [Fact]
    public void EdgeIsOpaque()
    {
        var img = Raster([(Box(0, 0, 10.5, 10), Color.FromRgb(255, 0, 0))], 360, 180);   // top edge through the centre of row 79
        Assert.Equal(255, Px(img, 185, 79).A);
    }

    [Fact]
    public void AreaCrossingDateLineFillsBothEdgesOnly()
    {
        var split = DateLine.SplitPolygon([[(175, 5), (185, 5), (185, 15), (175, 15)]]);
        Assert.Equal(2, split.Count);
        var img = Raster(split.Select(p => (p, Color.FromRgb(255, 255, 255))), 360, 180);
        Assert.True(Px(img, 0, 85).A > 0);
        Assert.True(Px(img, 359, 85).A > 0);
        Assert.Equal(0, Px(img, 180, 85).A);
    }

    [Fact]
    public void DateLineEdgesAreNotStroked()
    {
        var split = DateLine.SplitPolygon([[(170, 0), (190, 0), (190, 10), (170, 10)]]);
        var img = Raster(split.Select(p => (p, Color.FromRgb(255, 0, 0))), 360, 180);
        Assert.InRange(Px(img, 359, 85).A, 88, 90);
        Assert.InRange(Px(img, 0, 85).A, 88, 90);
    }

    [Fact]
    public void ComposedOverWhiteShowsTheColour()
    {
        var white = Bgra32Image.Create(360, 180);
        for (int i = 0; i < white.Pixels.Length; i++) white.Pixels[i] = 255;
        var areas = Raster([(Box(0, 0, 10, 10), Color.FromRgb(255, 0, 0)),
                            (Box(20, 40, 30, 60), Color.FromRgb(255, 255, 255))], 360, 180);
        var target = Bgra32Image.Create(360, 180);
        new GlobeImageComposer(white).ComposeInto(new GlobeLayers(new GeoPosition(0, 0), 0.0, Areas: areas, DrawTerminator: false), target);
        var red = Px(target, 185, 85);   // 255 -> 255*0.65 on G and B
        Assert.Equal(255, red.R);
        Assert.InRange(red.G, 165, 168);
        Assert.InRange(red.B, 165, 168);
        Assert.Equal((255, 255, 255, 255), (Px(target, 230, 65).B, Px(target, 230, 65).G, Px(target, 230, 65).R, Px(target, 230, 65).A));
    }

    [Fact]
    public void ProjectionPixelCentreConvention()
    {   // lon -10..10 -> x 170..190, lat 20..30 -> y 60..70 (north up: row 0 is lat 90)
        var img = Raster([(Box(20, -10, 30, 10), Color.FromRgb(255, 0, 0))], 360, 180);
        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
        for (int y = 0; y < 180; y++)
            for (int x = 0; x < 360; x++)
                if (Px(img, x, y).A != 0 && !(x < 168 || x > 191 || y < 58 || y > 71))
                { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        // Pixels 170..189 x 60..69 have their centres inside; the 1 px edge straddles the boundary, so it also
        // touches the outer neighbours (169, 190, 59, 70) but nothing further out.
        Assert.Equal(169, minX); Assert.Equal(190, maxX);
        Assert.Equal(59, minY); Assert.Equal(70, maxY);
        Assert.InRange(Px(img, 171, 61).A, 88, 90);
        Assert.InRange(Px(img, 188, 68).A, 88, 90);
        Assert.InRange(Px(img, 180, 65).A, 88, 90);
        Assert.Equal(0, Px(img, 180, 20).A);    // north of the box (lat ~70) is empty
        Assert.Equal(0, Px(img, 180, 110).A);   // the mirrored southern latitude is empty
        Assert.Equal(0, Px(img, 168, 65).A);    // >1 px outside the west edge
        Assert.Equal(0, Px(img, 191, 65).A);    // >1 px outside the east edge
    }

}
