using GlobeCore.Geo;

namespace GlobeCore.Regions;

/// <summary>
/// One edition of a body's political boundaries with the app's <see cref="BoundaryRules"/> applied: the regions and the
/// boundary lines, which always come from the same edition. Immutable; <see cref="CurrentBoundaries"/> holds the live one.
/// </summary>
/// <param name="Edition">As shown on screen, e.g. "Natural Earth 5.1.2 (2022-05-13)".</param>
/// <param name="Lines">The boundary lines as the source draws them (not split at ±180°; on Earth no land boundary crosses it).</param>
public sealed record BoundarySet(string Edition, DateOnly EditionDate, RegionIndex Regions, IReadOnlyList<GeoLine> Lines)
{
    /// <summary>The source's release tag, e.g. v5.1.2 (empty when the set was not built from a release).</summary>
    public string Tag { get; init; } = "";

    /// <summary>The on-screen text of the rules applied to this set (<see cref="BoundaryRules.Descriptions"/>).</summary>
    public IReadOnlyList<string> RuleDescriptions { get; init; } = [];

    /// <summary>
    /// The edition and the applied rules for a legend or tooltip, e.g. "Natural Earth 5.1.2 (2022-05-13); Crimea shown as
    /// part of Ukraine (UN General Assembly resolution 68/262)". An app adds its own prefix ("Borders: ").
    /// </summary>
    public string Describe() => string.Join("; ", RuleDescriptions.Prepend(Edition));
}
