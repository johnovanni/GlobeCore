namespace GlobeCore.Bodies;

/// <summary>
/// How a body's longitudes are <em>shown</em>. GlobeCore always stores and computes longitudes planetocentric and
/// east-positive, in (−180, 180]; a west-positive body (such as Mars in the IAU planetographic convention) only
/// displays them as 0–360°W (<c>GeoFormat.LatLon(point, LongitudeConvention.WestPositive)</c>).
/// </summary>
public enum LongitudeConvention { EastPositive, WestPositive }

/// <summary>How a body is lit: by the Sun (with a night shade) or by itself (no night side).</summary>
public abstract record Illumination;

/// <summary>
/// Lit by the Sun; <paramref name="TwilightDeg"/> is the Sun altitude at which the night shade is complete (below 0°,
/// and at least −90°). <see cref="Body.IlluminationAt"/> carries it into the composer's
/// <see cref="Imaging.IlluminationState"/>.
/// </summary>
public sealed record SunLit(double TwilightDeg = -12.0) : Illumination
{
    public double TwilightDeg { get; } = TwilightDeg is < 0 and >= -90
        ? TwilightDeg
        : throw new ArgumentOutOfRangeException(nameof(TwilightDeg), TwilightDeg, "The twilight must be a Sun altitude below 0°, at least −90°.");
}

public sealed record SelfLuminous : Illumination;
