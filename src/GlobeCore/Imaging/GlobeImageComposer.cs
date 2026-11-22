using GlobeCore.Bodies;

namespace GlobeCore.Imaging;

/// <summary>
/// What the composer needs to light the globe: where the Sun is overhead, how dark the night is, whether to draw the
/// day/night line, and the Sun altitude at which the night is complete (<paramref name="TwilightDeg"/>: below 0°, at
/// least −90°; −12° by default, as EarthWatch3D always drew it). <see cref="Body.IlluminationAt"/> builds one from a body,
/// with the twilight of its <see cref="SunLit"/>.
/// </summary>
public sealed record IlluminationState(SurfacePoint SubSolarPoint, double NightStrength, bool DrawTerminator, double TwilightDeg = -12.0)
{
    public double TwilightDeg { get; } = TwilightDeg is < 0 and >= -90
        ? TwilightDeg
        : throw new ArgumentOutOfRangeException(nameof(TwilightDeg), TwilightDeg, "The twilight must be a Sun altitude below 0°, at least −90°.");
}

/// <summary>Adapter: the fixed layer set EarthWatch3D used before layers became a list.</summary>
public sealed record GlobeLayers(
    SurfacePoint SubSolarPoint,
    double NightStrength,
    Bgra32Image? Clouds = null,
    Bgra32Image? Areas = null,
    Bgra32Image? Lines = null,
    SparseOverlay? SparseLines = null,
    bool DrawTerminator = true);

/// <summary>
/// Composes the globe texture: base, Surface layers (by Order), night shade, day/night line, AboveNight layers (by Order).
/// The night side is darkened (each channel multiplied toward a blue-tinted dark, so it never gets lighter),
/// with strength <c>NightStrength × smoothstep(0°, TwilightDeg, h)</c> where h is the Sun's altitude and TwilightDeg is
/// the <see cref="IlluminationState"/>'s (−12° by default). h is worked out from each pixel's planetocentric latitude
/// and longitude, as on a sphere, not from the normal of the body's ellipsoid; on a flattened body that moves the
/// day/night line a little from where the true horizon puts it (on Mars by up to about 0.34°, near ±45° latitude).
/// The day/night line is a soft gold line along h = 0, about <see cref="TerminatorHalfWidthPx"/> pixels each side.
/// With no <see cref="IlluminationState"/> the night shade and line are skipped (a self-luminous body).
/// Pixel (x, y) is centred at lon = -180 + (x+0.5)*360/W, lat = 90 - (y+0.5)*180/H.
/// </summary>
public sealed class GlobeImageComposer(Bgra32Image baseImage)
{
    /// <summary>The twilight when none is given: the night is complete at a Sun altitude of −12°.</summary>
    public const double DefaultTwilightDeg = -12.0;
    // Full-night channel multipliers: darker in red and green than blue, so night reads as dark blue.
    private const double NightMulB = 0.50, NightMulG = 0.36, NightMulR = 0.32;
    // Day/night line: colour, peak opacity, and half-width in image pixels.
    private const byte LineB = 120, LineG = 200, LineR = 255;
    private const double LineMaxAlpha = 0.7;
    public const double TerminatorHalfWidthPx = 2.5;

    private readonly Bgra32Image _base = baseImage ?? throw new ArgumentNullException(nameof(baseImage));

    /// <summary>
    /// How strongly a point is darkened: <paramref name="strength"/> × smoothstep(0°, <paramref name="twilightDeg"/>,
    /// altitude); the twilight is −12° unless given.
    /// </summary>
    public static double NightFactor(double sunAltitudeDeg, double strength, double twilightDeg = DefaultTwilightDeg)
    {
        double t = Math.Clamp((sunAltitudeDeg - 0.0) / (twilightDeg - 0.0), 0.0, 1.0);
        return strength * (t * t * (3.0 - 2.0 * t));
    }

    /// <summary>Compose the fixed Earth-style layer set: clouds and areas (Surface 10, 20), lines and sparse lines (AboveNight 10, 20).</summary>
    public void ComposeInto(GlobeLayers layers, Bgra32Image target)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var list = new List<IImageLayer>(4);
        if (layers.Clouds != null) list.Add(new BitmapBlendLayer(layers.Clouds, ImagePass.Surface, 10));
        if (layers.Areas != null) list.Add(new BitmapBlendLayer(layers.Areas, ImagePass.Surface, 20));
        if (layers.Lines != null) list.Add(new BitmapBlendLayer(layers.Lines, ImagePass.AboveNight, 10));
        if (layers.SparseLines != null) list.Add(new SparseOverlayLayer(layers.SparseLines, ImagePass.AboveNight, 20));
        ComposeInto(list, new IlluminationState(layers.SubSolarPoint, layers.NightStrength, layers.DrawTerminator), target);
    }

    public void ComposeInto(IReadOnlyList<IImageLayer> layers, IlluminationState? light, Bgra32Image target)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(target);
        int w = _base.Width, h = _base.Height;
        Check(target.Width, target.Height, nameof(target));
        foreach (var l in layers)
            if (l is ISizedImageLayer s) Check(s.Width, s.Height, l.GetType().Name);

        var surface = layers.Where(l => l.Pass == ImagePass.Surface).OrderBy(l => l.Order).ToArray();
        var above = layers.Where(l => l.Pass == ImagePass.AboveNight).OrderBy(l => l.Order).ToArray();

        double max = Math.Clamp(light?.NightStrength ?? 0.0, 0.0, 1.0);
        double twilight = light?.TwilightDeg ?? DefaultTwilightDeg;
        double sinTwilight = Math.Sin(twilight * Math.PI / 180.0);
        double deg = Math.PI / 180.0;
        double sinDec = 0, cosDec = 1, sunLon = 0;
        if (light != null)
        {
            sinDec = Math.Sin(light.SubSolarPoint.LatitudeDeg * deg);
            cosDec = Math.Cos(light.SubSolarPoint.LatitudeDeg * deg);
            sunLon = light.SubSolarPoint.LongitudeDeg;
        }

        var cosDl = new double[w];
        for (int x = 0; x < w; x++)
            cosDl[x] = Math.Cos((-180.0 + (x + 0.5) * 360.0 / w - sunLon) * deg);

        // Full-night result per source value, per channel.
        var fullB = new byte[256]; var fullG = new byte[256]; var fullR = new byte[256];
        for (int v = 0; v < 256; v++)
        {
            fullB[v] = Darken(v, NightMulB, max);
            fullG[v] = Darken(v, NightMulG, max);
            fullR[v] = Darken(v, NightMulR, max);
        }

        double lineHalfDeg = TerminatorHalfWidthPx * 180.0 / h;
        double sinLineHalf = Math.Sin(lineHalfDeg * deg);
        bool line = light?.DrawTerminator ?? false;

        var src = _base.Pixels;
        var dst = target.Pixels;
        bool shade = light != null && max > 0.0;

        Parallel.For(0, h, y =>
        {
            int row = y * w * 4;
            Buffer.BlockCopy(src, row, dst, row, w * 4);
            foreach (var l in surface) l.BlendRow(y, new Span<byte>(dst, row, w * 4));

            if (shade || line)
            {
                double lat = 90.0 - (y + 0.5) * 180.0 / h;
                double sinLat = Math.Sin(lat * deg), cosLat = Math.Cos(lat * deg);
                double a = sinLat * sinDec, b = cosLat * cosDec;
                int p = row;
                for (int x = 0; x < w; x++, p += 4)
                {
                    double s = a + b * cosDl[x];
                    if (shade && s < 0.0)
                    {
                        if (s <= sinTwilight)
                        {
                            dst[p] = fullB[dst[p]];
                            dst[p + 1] = fullG[dst[p + 1]];
                            dst[p + 2] = fullR[dst[p + 2]];
                        }
                        else
                        {
                            double f = NightFactor(Math.Asin(s) / deg, max, twilight);
                            dst[p] = Darken(dst[p], NightMulB, f);
                            dst[p + 1] = Darken(dst[p + 1], NightMulG, f);
                            dst[p + 2] = Darken(dst[p + 2], NightMulR, f);
                        }
                    }
                    if (line && Math.Abs(s) < sinLineHalf)
                    {
                        double la = LineMaxAlpha * (1.0 - Math.Abs(Math.Asin(s) / deg) / lineHalfDeg);
                        if (la > 0.0)
                        {
                            dst[p] = Mix(LineB, dst[p], la);
                            dst[p + 1] = Mix(LineG, dst[p + 1], la);
                            dst[p + 2] = Mix(LineR, dst[p + 2], la);
                        }
                    }
                }
            }

            foreach (var l in above) l.BlendRow(y, new Span<byte>(dst, row, w * 4));

            for (int p = row + 3, end = row + w * 4; p < end; p += 4) dst[p] = 255;
        });
    }

    /// <summary>v × (1 − f·(1 − m)): f = 0 leaves v unchanged, f = 1 multiplies it by m. Never brightens.</summary>
    private static byte Darken(int v, double m, double f) =>
        (byte)Math.Round(v * (1.0 - f * (1.0 - m)), MidpointRounding.AwayFromZero);

    internal static byte Mix(int srcValue, int dstValue, double a) =>
        (byte)Math.Round(srcValue * a + dstValue * (1.0 - a), MidpointRounding.AwayFromZero);

    private void Check(int width, int height, string name)
    {
        if (width != _base.Width || height != _base.Height)
            throw new ArgumentException($"{name} must be {_base.Width}x{_base.Height}, got {width}x{height}.", name);
    }
}
