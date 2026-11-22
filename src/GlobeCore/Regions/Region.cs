using AstroCore.Earth;
using GlobeCore.Geo;

namespace GlobeCore.Regions;

/// <summary>
/// A named area on any body's surface (on Earth, a country or other admin-0 unit), with its label point and polygons
/// (split at ±180° longitude, holes kept).
/// </summary>
/// <param name="Id">The region's code (on Earth an ISO3 code); unique in a well-formed set, matched case-insensitively.</param>
/// <param name="Name">The short name, which <see cref="RegionIndex.Resolve"/> tries first.</param>
/// <param name="OtherNames">Longer or formal names, in priority order.</param>
public sealed record Region(string Id, string Name, IReadOnlyList<string> OtherNames, GeoPosition LabelPoint, IReadOnlyList<GeoPolygon> Polygons);
