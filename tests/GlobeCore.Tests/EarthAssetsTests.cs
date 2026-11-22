using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AstroCore.Bodies;
using GlobeCore.Bodies;
using GlobeCore.Earth;
using GlobeCore.Geo;
using GlobeCore.Imaging;

namespace GlobeCore.Tests;

public class EarthAssetsTests
{
    [Fact]
    public void EveryNamedAssetExists()
    {
        Assert.Equal(["bluemarble-8192.jpg", "ne_50m_coastline.geojson", "ne_50m_admin_0_boundary_lines_land.geojson",
                      "plate_boundaries.geojson", "ne_50m_admin_0_countries.geojson"], EarthAssets.Names);
        foreach (var name in EarthAssets.Names)
        {
            using var s = EarthAssets.Open(name);
            Assert.True(s.ReadByte() >= 0, name + " is empty");
        }
    }

    [Fact]
    public void ResourceNamesAreUnderGlobeCoreEarthAssets()
    {
        var names = typeof(EarthAssets).Assembly.GetManifestResourceNames();
        foreach (var file in EarthAssets.Names) Assert.Contains("GlobeCore.Earth.Assets." + file, names);
    }

    [Fact]
    public void MissingAssetThrows() =>
        Assert.Throws<InvalidOperationException>(() => EarthAssets.Open("nope.jpg"));

    /// <summary>
    /// The old SceneController path, pasted as the reference: <c>ImageConversions.DecodeToBgra32(asset, 4096)</c>, whose
    /// scaled branch lets WIC scale while decoding (BitmapImage.DecodePixelWidth), then converts to BGRA32.
    /// </summary>
    private static Bgra32Image OldDecode(Stream jpegOrPng, int wanted)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = jpegOrPng;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.DecodePixelWidth = wanted;
        image.EndInit();
        BitmapSource frame = image;
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth, h = converted.PixelHeight;
        var pixels = new byte[checked(w * h * 4)];
        converted.CopyPixels(pixels, w * 4, 0);
        return new Bgra32Image(w, h, pixels);
    }

    /// <summary>The Blue Marble as an app loads it (the Earth pack has no UI, so the WPF side decodes it).</summary>
    private static Bgra32Image LoadBlueMarble(int width)
    {
        using var s = EarthAssets.Open(EarthAssets.BlueMarbleFile);
        return GlobeCore.Wpf.Rendering.ImageConversions.DecodeToBgra32(s, width);
    }

    [Fact]
    public void BlueMarbleDownscaleMatchesOld()
    {
        var loaded = LoadBlueMarble(4096);
        using var s = typeof(EarthAssets).Assembly.GetManifestResourceStream("GlobeCore.Earth.Assets.bluemarble-8192.jpg")!;
        var old = OldDecode(s, 4096);
        Assert.Equal((4096, 2048), (loaded.Width, loaded.Height));
        Assert.Equal(old.Pixels, loaded.Pixels);
    }

    // Moved from GlobeCore's GeoJsonLinesTests in EarthWatch3D (it needs the embedded coastline).
    [Fact]
    public void EmbeddedCoastlineLoads()
    {
        using var s = EarthAssets.Open("ne_50m_coastline.geojson");
        Assert.True(GeoJsonLines.Read(s).Count > 1000);
    }

    [Fact]
    public void NaturalEarthLinesReadTheEmbeddedFiles()
    {
        var coast = NaturalEarthLines.Coastlines();
        var borders = NaturalEarthLines.LandBoundaries();
        Assert.True(coast.Count > 1000);
        Assert.True(borders.Count > 100);
        using var s = EarthAssets.Open("ne_50m_admin_0_boundary_lines_land.geojson");
        Assert.Equal(GeoJsonLines.Read(s).Count, borders.Count);
    }

    [Fact]
    public void EarthBodyIsTheSphereOfRadius6371()
    {
        var e = EarthBody.Instance;
        Assert.Equal(6371.0, EarthBody.RadiusKm);
        Assert.Equal("earth", e.Id);
        Assert.Equal(new Ellipsoid(6371, 6371), e.Shape);
        Assert.Equal(1.0, e.Shape.PolarRatio);
        Assert.Same(BodyRotations.Earth, e.Rotation);
        Assert.Null(e.HeliocentricPosition);
        Assert.Equal(LongitudeConvention.EastPositive, e.LongitudeConvention);
        Assert.Equal(new SunLit(-12), e.Illumination);
    }
}
