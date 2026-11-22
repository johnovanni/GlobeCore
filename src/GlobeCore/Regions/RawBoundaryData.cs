using AstroCore.Earth;

namespace GlobeCore.Regions;

/// <summary>One polygon as its source file gives it: rings of (longitude, latitude), outer ring first, not yet split at ±180°.</summary>
public sealed record RawPolygon(IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> Rings);

/// <summary>A region as read from the source, before <see cref="BoundaryRules"/> and the date-line split.</summary>
/// <param name="Parts">The region's polygons in file order (a multipolygon's parts).</param>
public sealed record RawRegion(string Id, string Name, IReadOnlyList<string> OtherNames, GeoPosition LabelPoint, IReadOnlyList<RawPolygon> Parts);

/// <summary>
/// A first-level subdivision of a region (on Earth an admin-1 unit such as a province), which a <c>moveAdmin1</c> rule
/// can move to another region.
/// </summary>
/// <param name="Code">Its code (on Earth ISO 3166-2, e.g. UA-43).</param>
/// <param name="ParentId">The id of the region the source puts it in.</param>
public sealed record RawSubdivision(string Code, string ParentId, string Name, IReadOnlyList<RawPolygon> Parts);

/// <summary>
/// One edition of a body's political boundaries, read from its files but with no rules applied:
/// <see cref="BoundaryRules.Apply"/> turns it into a <see cref="BoundarySet"/>. A body's pack (for Earth,
/// <c>GlobeCore.Earth.NaturalEarthBoundaries</c>) maps its source's file format to this.
/// </summary>
/// <param name="Tag">The source's release tag (e.g. v5.1.2); <see cref="BoundaryStore.CompareTags"/> orders tags.</param>
/// <param name="Edition">The edition as shown on screen, e.g. "Natural Earth 5.1.2 (2022-05-13)".</param>
/// <param name="Lines">The boundary lines (on Earth, land boundaries), each a list of points.</param>
/// <param name="Subdivisions">The subdivisions the rules may need; may be only those (the bundled Earth set carries two).</param>
public sealed record RawBoundaryData(
    string Tag,
    string Edition,
    DateOnly EditionDate,
    IReadOnlyList<RawRegion> Regions,
    IReadOnlyList<IReadOnlyList<GeoPosition>> Lines,
    IReadOnlyList<RawSubdivision> Subdivisions)
{
    /// <summary>Names the source does not carry, for region ids (applied before the rules' aliases).</summary>
    public IReadOnlyDictionary<string, string> Aliases { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The source files as downloaded (file name to bytes), so a validated set can be kept by <see cref="BoundaryStore.Save(string, DateOnly, IReadOnlyDictionary{string, byte[]}, out Exception?)"/>.
    /// Empty for a set read from bundled assets.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> Files { get; init; } = new Dictionary<string, byte[]>();
}
