using System.IO;

namespace GlobeCore.Earth;

/// <summary>
/// The Earth pack's embedded files (resource names <c>GlobeCore.Earth.Assets.&lt;file&gt;</c>); their sources and
/// licences are in Assets/README.md and THIRD-PARTY-NOTICES.txt. The pack has no UI, so it does not decode the Blue
/// Marble (an 8192 x 4096 JPEG): a WPF app decodes it, scaling while it decodes, with GlobeCore.Wpf's
/// <c>ImageConversions.DecodeToBgra32(EarthAssets.Open(EarthAssets.BlueMarbleFile), width)</c> (safe off the UI thread).
/// </summary>
public static class EarthAssets
{
    public const string BlueMarbleFile = "bluemarble-8192.jpg";
    public const string CoastlineFile = "ne_50m_coastline.geojson";
    public const string LandBoundariesFile = "ne_50m_admin_0_boundary_lines_land.geojson";
    public const string CountriesFile = "ne_50m_admin_0_countries.geojson";
    public const string PlateBoundariesFile = "plate_boundaries.geojson";
    /// <summary>Natural Earth's admin-1 polygons of Crimea and Sevastopol, used to show them as part of Ukraine (UN practice).</summary>
    public const string CrimeaRegionsFile = CrimeaFixup.RegionsFile;

    private const string Prefix = "GlobeCore.Earth.Assets.";

    /// <summary>Every embedded asset's file name.</summary>
    public static IReadOnlyList<string> Names { get; } = [BlueMarbleFile, CoastlineFile, LandBoundariesFile, PlateBoundariesFile, CountriesFile, CrimeaRegionsFile];

    /// <summary>Opens an embedded asset by file name. The caller disposes the stream.</summary>
    public static Stream Open(string fileName) =>
        typeof(EarthAssets).Assembly.GetManifestResourceStream(Prefix + fileName)
        ?? throw new InvalidOperationException($"Missing embedded resource {fileName}.");
}
