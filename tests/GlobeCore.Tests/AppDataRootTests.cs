using GlobeCore.Storage;

namespace GlobeCore.Tests;

public sealed class AppDataRootTests
{
    [Fact]
    public void PathsMatchEarthWatch3DBefore()
    {
        // Literal strings: what the old EarthWatch3D AppPaths.ForUser gave before the move.
        var plain = AppDataRoot.ForUser("EarthWatch3D", @"C:\L", null);
        Assert.Equal(@"C:\L\EarthWatch3D", plain.Root);
        Assert.Equal(@"C:\L\EarthWatch3D\logs", plain.LogsDir);
        Assert.Equal(@"C:\L\EarthWatch3D\cache", plain.CacheDir);
        Assert.Equal(@"C:\L\EarthWatch3D\settings.json", plain.SettingsFile);
        Assert.Equal(@"C:\L\EarthWatch3D\download-meter.json", plain.DownloadMeterFile);

        var packaged = AppDataRoot.ForUser("EarthWatch3D", @"C:\L", "johnovanni.EarthWatch3D_abc");
        const string root = @"C:\L\Packages\johnovanni.EarthWatch3D_abc\LocalCache\Local\EarthWatch3D";
        Assert.Equal(root, packaged.Root);
        Assert.Equal(root + @"\logs", packaged.LogsDir);
        Assert.Equal(root + @"\cache", packaged.CacheDir);
        Assert.Equal(root + @"\settings.json", packaged.SettingsFile);
        Assert.Equal(root + @"\download-meter.json", packaged.DownloadMeterFile);
    }

    [Fact]
    public void AppNameIsUsedForTheFolder()
        => Assert.Equal(@"C:\L\Other", AppDataRoot.ForUser("Other", @"C:\L", null).Root);
}
