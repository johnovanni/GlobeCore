using GlobeCore.Bodies;
using GlobeCore.Imaging;

namespace GlobeCore.Tests;

public class ComposerLayerListTests
{
    private static void Put(Bgra32Image img, int x, int y, byte b, byte g, byte r, byte a)
    { int i = (y * img.Width + x) * 4; img.Pixels[i] = b; img.Pixels[i + 1] = g; img.Pixels[i + 2] = r; img.Pixels[i + 3] = a; }

    private static Bgra32Image Gradient(int w, int h)
    {
        var img = Bgra32Image.Create(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Put(img, x, y, (byte)(x * 7), (byte)(y * 5), (byte)((x + y) * 3), 255);
        return img;
    }

    private static Bgra32Image Scatter(int w, int h, int seed, int n)
    {
        var img = Bgra32Image.Create(w, h);
        var rnd = new Random(seed);
        for (int k = 0; k < n; k++)
            Put(img, rnd.Next(w), rnd.Next(h), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256),
                (byte)(k % 3 == 0 ? 255 : rnd.Next(1, 255)));
        return img;
    }

    private sealed class SolidLayer(ImagePass pass, int order, byte v) : IImageLayer
    {
        public ImagePass Pass => pass;
        public int Order => order;
        public void BlendRow(int y, Span<byte> row)
        { for (int p = 0; p < row.Length; p += 4) { row[p] = row[p + 1] = row[p + 2] = v; row[p + 3] = 255; } }
    }

    [Fact]
    public void AdapterMatchesLayerList()
    {
        int w = 64, h = 32;
        var baseImg = Gradient(w, h);
        var clouds = Scatter(w, h, 1, 600);
        var areas = Scatter(w, h, 2, 600);
        var dense = Scatter(w, h, 3, 300);
        var sparse = SparseOverlay.FromImage(Scatter(w, h, 4, 300));
        var sun = new SurfacePoint(-15, 60);
        var composer = new GlobeImageComposer(baseImg);

        var viaAdapter = Bgra32Image.Create(w, h);
        composer.ComposeInto(new GlobeLayers(sun, 0.55, clouds, areas, dense, sparse), viaAdapter);

        var viaList = Bgra32Image.Create(w, h);
        IImageLayer[] list =
        [
            new SparseOverlayLayer(sparse, ImagePass.AboveNight, 20),
            new BitmapBlendLayer(dense, ImagePass.AboveNight, 10),
            new BitmapBlendLayer(areas, ImagePass.Surface, 20),
            new BitmapBlendLayer(clouds, ImagePass.Surface, 10),
        ];
        composer.ComposeInto(list, new IlluminationState(sun, 0.55, true), viaList);

        Assert.Equal(viaAdapter.Pixels, viaList.Pixels);
    }

    [Fact]
    public void OrderIsRespectedWithinAPass()
    {
        var composer = new GlobeImageComposer(Gradient(16, 8));
        var target = Bgra32Image.Create(16, 8);
        composer.ComposeInto([new SolidLayer(ImagePass.Surface, 5, 200), new SolidLayer(ImagePass.Surface, 1, 50)], null, target);
        Assert.Equal(200, target.Pixels[0]);
        composer.ComposeInto([new SolidLayer(ImagePass.Surface, 1, 200), new SolidLayer(ImagePass.Surface, 5, 50)], null, target);
        Assert.Equal(50, target.Pixels[0]);
    }

    [Fact]
    public void SelfLuminousSkipsNight()
    {
        var baseImg = Gradient(16, 8);
        var target = Bgra32Image.Create(16, 8);
        new GlobeImageComposer(baseImg).ComposeInto(Array.Empty<IImageLayer>(), null, target);
        Assert.Equal(baseImg.Pixels, target.Pixels);
    }

    [Fact]
    public void AboveNightLayersAreNotShaded()
    {
        var composer = new GlobeImageComposer(Gradient(16, 8));
        var target = Bgra32Image.Create(16, 8);
        composer.ComposeInto([new SolidLayer(ImagePass.Surface, 0, 200)], new IlluminationState(new(0, 0), 1.0, false), target);
        int dayPx = (4 * 16 + 8) * 4;   // lon +6: day, unchanged
        Assert.Equal(200, target.Pixels[dayPx]);
        int deep = (4 * 16 + 0) * 4;   // lon -174: night
        Assert.True(target.Pixels[deep] < 200);
        composer.ComposeInto([new SolidLayer(ImagePass.AboveNight, 0, 200)], new IlluminationState(new(0, 0), 1.0, false), target);
        Assert.Equal(200, target.Pixels[deep]);
    }
}
