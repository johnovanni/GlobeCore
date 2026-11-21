namespace GlobeCore.Storage;

/// <summary>Folder layout under an app's data root.</summary>
public sealed record AppDataRoot(string Root)
{
    public string CacheDir => Path.Combine(Root, "cache");
    public string LogsDir => Path.Combine(Root, "logs");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string DownloadMeterFile => Path.Combine(Root, "download-meter.json");

    /// <summary>
    /// The data root for a user. Packaged (MSIX) apps get Windows' redirected folder named directly, so the
    /// paths the app shows are the real ones; unpackaged apps use <c>%LOCALAPPDATA%\&lt;appName&gt;</c>.
    /// </summary>
    public static AppDataRoot ForUser(string appName, string localAppData, string? packageFamilyName) => new(
        packageFamilyName is null
            ? Path.Combine(localAppData, appName)
            : Path.Combine(localAppData, "Packages", packageFamilyName, "LocalCache", "Local", appName));
}
