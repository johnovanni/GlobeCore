using AstroCore.Astronomy;
using AstroCore.Bodies;
using GlobeCore.Imaging;

namespace GlobeCore.Bodies;

/// <summary>
/// A globe body. <paramref name="HeliocentricPosition"/> maps jdTT to heliocentric J2000 ecliptic AU; null means the Earth.
/// </summary>
public sealed record Body(
    string Id,
    string Name,
    Ellipsoid Shape,
    IBodyRotation Rotation,
    Func<double, Vector3d>? HeliocentricPosition,
    LongitudeConvention LongitudeConvention,
    Illumination Illumination)
{
    /// <summary>
    /// The point on the body directly beneath the Sun at <paramref name="utc"/>. Geometric: the Sun and the body are both
    /// taken at <paramref name="utc"/>, with no light time (<see cref="BodySubPoints.SubSolarPoint"/>); that is the body's
    /// own day and night at that instant. To show another body as it is seen from the Earth at <paramref name="utc"/>,
    /// use the light that left it one one-way light time earlier: <c>SubSolarPoint(utc − lightTime)</c>, with lightTime
    /// the Earth–body distance divided by c. For Mars that is 3 to 22 minutes, and Mars turns about 0.24° a minute, so
    /// leaving it out puts the day/night line up to about 5° of longitude out.
    /// </summary>
    public SurfacePoint SubSolarPoint(DateTime utc) => BodySubPoints.SubSolarPoint(HeliocentricPosition, Rotation, utc);

    /// <summary>
    /// The light to compose at <paramref name="utc"/>: <see cref="SubSolarPoint"/> and the twilight of the body's
    /// <see cref="SunLit"/> illumination; null for a <see cref="SelfLuminous"/> body (no night shade). Made for
    /// <c>LayerHost.IlluminationAt</c>: <c>host.IlluminationAt = utc => body.IlluminationAt(utc, 0.85, true)</c>.
    /// </summary>
    public IlluminationState? IlluminationAt(DateTime utc, double nightStrength, bool drawTerminator) => Illumination switch
    {
        SunLit sun => new IlluminationState(SubSolarPoint(utc), nightStrength, drawTerminator, sun.TwilightDeg),
        _ => null,
    };
}
