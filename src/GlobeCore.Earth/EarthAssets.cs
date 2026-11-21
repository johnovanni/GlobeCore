using System.IO;
using GlobeCore.Imaging;
using GlobeCore.Wpf.Rendering;

namespace GlobeCore.Earth;

/// <summary>
/// The Earth pack's embedded files (resource names <c>GlobeCore.Earth.Assets.&lt;file&gt;</c>); their sources and
/// licences are in Assets/README.md and THIRD-PARTY-NOTICES.txt.
/// </summary>
public static class EarthAssets
{
    public const string BlueMarbleFile = "bluemarble-8192.jpg";
    public const string CoastlineFile = "ne_50m_coastline.geojson";
    public const string LandBoundariesFile = "ne_50m_admin_0_boundary_lines_land.geojson";
    public const string PlateBoundariesFile = "plate_boundaries.geojson";

    private const string Prefix = "GlobeCore.Earth.Assets.";

    /// <summary>Every embedded asset's file name.</summary>
    public static IReadOnlyList<string> Names { get; } = [BlueMarbleFile, CoastlineFile, LandBoundariesFile, PlateBoundariesFile];

    /// <summary>Opens an embedded asset by file name. The caller disposes the stream.</summary>
    public static Stream Open(string fileName) =>
        typeof(EarthAssets).Assembly.GetManifestResourceStream(Prefix + fileName)
        ?? throw new InvalidOperationException($"Missing embedded resource {fileName}.");

    /// <summary>
    /// The Blue Marble (8192 x 4096 asset), scaled to <paramref name="width"/> pixels wide while decoding (height half
    /// that), as straight BGRA. Safe to call off the UI thread.
    /// </summary>
    public static Bgra32Image LoadBlueMarble(int width)
    {
        using var s = Open(BlueMarbleFile);
        return ImageConversions.DecodeToBgra32(s, width);
    }
}
