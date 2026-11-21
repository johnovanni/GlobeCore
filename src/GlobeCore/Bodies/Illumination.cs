namespace GlobeCore.Bodies;

public enum LongitudeConvention { EastPositive, WestPositive }

/// <summary>How a body is lit: by the Sun (with a night shade) or by itself (no night side).</summary>
public abstract record Illumination;

/// <summary>Lit by the Sun; <paramref name="TwilightDeg"/> is the Sun altitude at which the night shade is complete.</summary>
public sealed record SunLit(double TwilightDeg = -12.0) : Illumination;

public sealed record SelfLuminous : Illumination;
