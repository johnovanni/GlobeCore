using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AstroCore.Bodies;
using AstroCore.Earth;
using GlobeCore.Bodies;
using GlobeCore.Imaging;
using GlobeCore.Wpf.Layers;
using GlobeCore.Wpf.Rendering;
using HelixToolkit.Wpf;

namespace GlobeCore.Tests;

public class LayerHostTests
{
    private static readonly Body TestBody = new("test", "Test", new Ellipsoid(1, 1), BodyRotations.Earth, null,
        LongitudeConvention.EastPositive, new SunLit());

    private static Bgra32Image Pattern(int w, int h)
    {
        var img = Bgra32Image.Create(w, h);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = (byte)(i * 7 + 3);
        return img;
    }

    private static (LayerHost Host, HelixViewport3D Viewport, Canvas Overlay) NewHost(Bgra32Image? baseImage = null, RecordingLog? log = null)
    {
        var viewport = new HelixViewport3D
        {
            Camera = new PerspectiveCamera(new Point3D(3, 0, 0), new Vector3D(-3, 0, 0), new Vector3D(0, 0, 1), 45),
        };
        var overlay = new Canvas();
        var host = new LayerHost(viewport, overlay, TestBody, baseImage ?? Pattern(8, 4), log ?? new RecordingLog());
        return (host, viewport, overlay);
    }

    private sealed class TestImageLayer(string id, IImageLayer? image) : ImageLayer(id, id, 0)
    {
        public IImageLayer? Image { get; set; } = image;
        public override IImageLayer? Current => Image;
        public List<(IImageLayer? Used, bool Composed)> Displayed { get; } = [];
        public void Touch() => Invalidate();
        protected internal override void OnComposeDisplayed(IImageLayer? used, bool composed) => Displayed.Add((used, composed));
    }

    private sealed class TestPickLayer(string id, int zOrder) : SceneLayer(id, id, zOrder), IPickable
    {
        public int Updates { get; private set; }
        public override void Update(SceneContext ctx) => Updates++;
        public PickResult? HitTest(Point screen, Projector projector, bool hover) => new(Id, this, "tip " + Id);
    }

    private sealed class NullPickLayer(string id, int zOrder) : SceneLayer(id, id, zOrder), IPickable
    {
        public override void Update(SceneContext ctx) { }
        public PickResult? HitTest(Point screen, Projector projector, bool hover) => null;
    }

    [Fact]
    public void InvalidateTriggersOneRecompose() => StaAsync.Run(async () =>
    {
        var (host, _, _) = NewHost();
        var layer = new TestImageLayer("lines", null);
        host.Add(layer);
        int composes = 0;
        host.Composed += (_, _) => composes++;

        host.RequestRecompose();   // composes at once and opens the 250 ms throttle window
        layer.Touch(); layer.Touch(); layer.Touch();   // all within the window

        await StaAsync.WaitUntil(() => composes >= 2);
        await Task.Delay(600);   // well past the window: nothing more is owed
        Assert.Equal(2, composes);   // the first, then one for the three invalidations
        Assert.True(host.IsComposeIdle);
        host.Dispose();
    });

    [Fact]
    public void PickPrefersHigherZOrder() => Sta.Run(() =>
    {
        var (host, _, _) = NewHost();
        host.Add(new TestPickLayer("low", 100));
        host.Add(new TestPickLayer("high", 120));
        host.Add(new NullPickLayer("top-but-empty", 130));
        host.Add(new TestPickLayer("lower", 90));

        var hit = host.Pick(new Point(10, 10), hover: true);

        Assert.Equal("high", hit!.LayerId);
        Assert.Equal("tip high", hit.Tooltip);
        host.Dispose();
    });

    [Fact]
    public void HiddenLayerIsNotPicked() => Sta.Run(() =>
    {
        var (host, _, _) = NewHost();
        var low = new TestPickLayer("low", 100);
        var high = new TestPickLayer("high", 120) { IsVisible = false };
        host.Add(low);
        host.Add(high);

        Assert.Equal("low", host.Pick(new Point(10, 10), hover: false)!.LayerId);
        low.IsVisible = false;
        Assert.Null(host.Pick(new Point(10, 10), hover: false));
        high.IsVisible = true;
        Assert.Equal("high", host.Pick(new Point(10, 10), hover: false)!.LayerId);
        host.Dispose();
    });

    // Moved from EarthWatch3D's ComposeFallbackTests (SceneController.ComposeOrPlain).
    [Fact]
    public void ComposeFailureShowsPlainImage()
    {
        var plain = Pattern(8, 4);
        var target = Bgra32Image.Create(8, 4);
        Array.Fill(target.Pixels, (byte)0x55);   // half-written garbage from the failed compose
        var log = new RecordingLog();
        // An Areas layer of the wrong size makes ComposeInto throw.
        IImageLayer[] layers = [new BitmapBlendLayer(Bgra32Image.Create(2, 2), ImagePass.Surface, 20)];

        bool ok = LayerHost.ComposeOrPlain(new GlobeImageComposer(plain), layers,
            new IlluminationState(new GeoPosition(0, 0), 1.0, true), target, plain, log);

        Assert.False(ok);
        Assert.Equal(plain.Pixels, target.Pixels);
        Assert.Contains("compose failed", Assert.Single(log.Errors));
    }

    // Moved from EarthWatch3D's ComposeFallbackTests.
    [Fact]
    public void SuccessfulComposeIsUntouched()
    {
        var plain = Pattern(8, 4);
        var target = Bgra32Image.Create(8, 4);
        var log = new RecordingLog();

        Assert.True(LayerHost.ComposeOrPlain(new GlobeImageComposer(plain), [],
            new IlluminationState(new GeoPosition(0, 0), 1.0, true), target, plain, log));
        Assert.Empty(log.Errors);
        Assert.NotEqual(plain.Pixels, target.Pixels);   // half the image is night-shaded
    }

    [Fact]
    public void ComposedTextureIsBasePlusVisibleLayersPlusIllumination() => StaAsync.Run(async () =>
    {
        var baseImage = Pattern(16, 8);
        var (host, _, _) = NewHost(baseImage);
        var cloud = Bgra32Image.Create(16, 8);
        for (int i = 0; i < cloud.Pixels.Length; i += 4) { cloud.Pixels[i] = 250; cloud.Pixels[i + 3] = 128; }
        var lines = Bgra32Image.Create(16, 8);
        for (int i = 0; i < lines.Pixels.Length; i += 12) { lines.Pixels[i + 2] = 255; lines.Pixels[i + 3] = 255; }
        var clouds = new TestImageLayer("clouds", new BitmapBlendLayer(cloud, ImagePass.Surface, 10));
        var hidden = new TestImageLayer("hidden", new BitmapBlendLayer(Bgra32Image.Create(16, 8), ImagePass.Surface, 5)) { IsVisible = false };
        var linesLayer = new TestImageLayer("lines", new BitmapBlendLayer(lines, ImagePass.AboveNight, 10));
        host.Add(linesLayer);
        host.Add(hidden);
        host.Add(clouds);
        var t = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        var light = new IlluminationState(new GeoPosition(10, 20), 0.85, true);
        host.IlluminationAt = utc => { Assert.Equal(t, utc); return light; };
        int first = 0;
        host.FirstComposeDisplayed += (_, _) => first++;

        host.DisplayTimeUtc = t;   // never composed, so due: composes
        await host.WhenFirstComposeDisplayed;
        await StaAsync.WaitUntil(() => host.IsComposeIdle);

        var expected = Bgra32Image.Create(16, 8);
        new GlobeImageComposer(baseImage).ComposeInto(
            [clouds.Image!, linesLayer.Image!], light, expected);
        Assert.Equal(expected.Pixels, host.ComposedImage.Pixels);
        Assert.Equal(light, host.Illumination);
        Assert.Equal(t, host.ComposedUtc);
        Assert.Equal(1, first);
        Assert.True(host.WhenFirstComposeDisplayed.IsCompletedSuccessfully);
        Assert.Equal(new[] { (clouds.Image, true) }, clouds.Displayed);
        Assert.Equal(new[] { ((IImageLayer?)null, true) }, hidden.Displayed);
        Assert.True(host.IsComposeIdle);

        await host.RecomposeNowAsync();
        Assert.Equal(1, first);   // raised once only
        host.Dispose();
    });

    [Fact]
    public void DisplayTimeComposesOnlyOnceTheIntervalHasPassed() => StaAsync.Run(async () =>
    {
        var (host, _, _) = NewHost();
        int composes = 0;
        host.Composed += (_, _) => composes++;
        var t = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

        host.DisplayTimeUtc = t;
        await StaAsync.WaitUntil(() => composes == 1 && host.IsComposeIdle);
        host.DisplayTimeUtc = t.AddSeconds(59);   // not due
        await Task.Delay(400);
        Assert.Equal(1, composes);
        Assert.True(host.IsComposeIdle);
        host.DisplayTimeUtc = t.AddSeconds(61);   // due (a trailing compose if still inside the throttle window)
        await StaAsync.WaitUntil(() => composes == 2 && host.IsComposeIdle);
        Assert.Equal(t.AddSeconds(61), host.ComposedUtc);
        host.DisplayTimeUtc = t.AddSeconds(30);   // the clock went back: due
        await StaAsync.WaitUntil(() => composes == 3 && host.IsComposeIdle);
        host.Dispose();
    });

    [Fact]
    public void GlobeAndSceneRootsFollowZOrderAndVisibility() => StaAsync.Run(async () =>
    {
        var (host, viewport, _) = NewHost();
        var a = new TestPickLayer("a", 110);
        var b = new TestPickLayer("b", 100);
        var c = new TestPickLayer("c", 120) { IsVisible = false };
        host.Add(a);
        host.Add(b);
        host.Add(c);
        await host.RecomposeNowAsync();

        var globe = viewport.Children.OfType<ModelVisual3D>().First(v => v.Content is GeometryModel3D);
        int g = viewport.Children.IndexOf(globe);
        Assert.Equal(g + 1, viewport.Children.IndexOf(b.Root));
        Assert.Equal(g + 2, viewport.Children.IndexOf(a.Root));
        Assert.DoesNotContain(c.Root, viewport.Children);
        var mesh = (MeshGeometry3D)((GeometryModel3D)globe.Content).Geometry;
        Assert.Equal(361 * 181, mesh.Positions.Count);

        c.IsVisible = true;
        Assert.Equal(g + 3, viewport.Children.IndexOf(c.Root));
        b.IsVisible = false;
        Assert.DoesNotContain(b.Root, viewport.Children);
        b.IsVisible = true;
        Assert.Equal(g + 1, viewport.Children.IndexOf(b.Root));
        host.Dispose();
    });

    [Fact]
    public void DisposeCancelsTheFirstComposeWait() => Sta.Run(() =>
    {
        var (host, _, _) = NewHost();
        host.Dispose();
        Assert.True(host.WhenFirstComposeDisplayed.IsCanceled);
    });
}
