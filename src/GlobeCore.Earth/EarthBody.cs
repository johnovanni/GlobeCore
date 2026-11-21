using AstroCore.Bodies;
using GlobeCore.Bodies;

namespace GlobeCore.Earth;

/// <summary>
/// The Earth as GlobeCore draws it: a sphere of radius 6371 km (the mean radius; the ellipsoid is not used, so every
/// Earth result stays as before), turned by <see cref="BodyRotations.Earth"/>, lit by the Sun with the night complete at
/// a solar altitude of −12°. Its heliocentric position is null: the Earth's own orbit is used.
/// </summary>
public static class EarthBody
{
    public const double RadiusKm = 6371.0;

    public static Body Instance { get; } = new(
        "earth",
        "Earth",
        new Ellipsoid(RadiusKm, RadiusKm),
        BodyRotations.Earth,
        null,
        LongitudeConvention.EastPositive,
        new SunLit(-12));
}
