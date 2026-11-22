using AstroCore.Earth;
using GlobeCore.Geo;

namespace GlobeCore.Earth;

/// <summary>A country (or other admin-0 unit) with its label point and polygons (split at the date line, holes kept).</summary>
public sealed record Country(string Iso3, string Name, GeoPosition LabelPoint, IReadOnlyList<GeoPolygon> Polygons);
