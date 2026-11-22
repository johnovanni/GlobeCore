using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GlobeCore.Storage;

namespace GlobeCore.Regions;

/// <summary>The files of one stored boundary set, as <see cref="BoundaryStore"/> read them back (sizes and SHA-256 checked).</summary>
public sealed record StoredBoundaryFiles(string Tag, DateOnly Date, IReadOnlyDictionary<string, byte[]> Files);

/// <summary>
/// Keeps validated boundary sets on disk, one folder per release tag (<c>&lt;cacheDir&gt;/&lt;tag&gt;/</c>, e.g.
/// <c>&lt;data&gt;/cache/boundaries/v5.2.0/</c>), each with a <c>manifest.json</c> that records the tag, the release date
/// and every file's size and SHA-256. Files are written atomically and the manifest last, so a set without a manifest,
/// or with a file that does not match it, is never loaded. Stored sets are never deleted here.
/// </summary>
public sealed partial class BoundaryStore(string cacheDir)
{
    public const string ManifestFile = "manifest.json";

    /// <summary>The folder holding one subfolder per stored tag.</summary>
    public string CacheDir { get; } = cacheDir ?? throw new ArgumentNullException(nameof(cacheDir));

    /// <summary>
    /// Stores one set: each file atomically, then the manifest. Any earlier manifest of the same tag is removed first,
    /// so a half-replaced set is never read as whole.
    /// </summary>
    /// <param name="tag">Release tag, used as the folder name: letters, digits, '.', '_' and '-' only.</param>
    /// <param name="files">File name (same characters as a tag) to content.</param>
    /// <param name="error">The I/O failure when this returns false; otherwise null.</param>
    /// <exception cref="ArgumentException">The tag or a file name is not a safe file name, or there are no files.</exception>
    public bool Save(string tag, DateOnly date, IReadOnlyDictionary<string, byte[]> files, out Exception? error)
    {
        CheckName(tag, nameof(tag));
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0) throw new ArgumentException("no files to store", nameof(files));
        foreach (var name in files.Keys)
        {
            CheckName(name, nameof(files));
            if (name.Equals(ManifestFile, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"{ManifestFile} is reserved", nameof(files));
        }

        var dir = Path.Combine(CacheDir, tag);
        error = null;
        try
        {
            Directory.CreateDirectory(dir);
            File.Delete(Path.Combine(dir, ManifestFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex;
            return false;
        }
        var manifest = new Dictionary<string, object>
        {
            ["tag"] = tag,
            ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["files"] = files.ToDictionary(kv => kv.Key, kv => new Dictionary<string, object>
            {
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(kv.Value)),
                ["bytes"] = (long)kv.Value.Length,
            }),
        };
        foreach (var (name, bytes) in files)
            if (!AtomicFile.TryWriteAllBytes(Path.Combine(dir, name), bytes, out error)) return false;
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        return AtomicFile.TryWriteAllText(Path.Combine(dir, ManifestFile), json, out error);
    }

    /// <summary>Like <see cref="Save(string, DateOnly, IReadOnlyDictionary{string, byte[]}, out Exception?)"/>, without the error.</summary>
    public bool Save(string tag, DateOnly date, IReadOnlyDictionary<string, byte[]> files) => Save(tag, date, files, out _);

    /// <summary>The tags with a readable manifest, newest first (by <see cref="CompareTags"/>, then date).</summary>
    public IReadOnlyList<string> StoredTags() => [.. ReadManifests().Select(m => m.Tag)];

    /// <summary>
    /// Loads the newest stored set that is whole (every file present with its recorded size and SHA-256), parses,
    /// takes the rules and passes the validator. A set that fails is skipped (and kept on disk), and the next newest is
    /// tried, so the last good set wins.
    /// </summary>
    /// <param name="parse">The body pack's reader, e.g. <c>NaturalEarthBoundaries.Parse</c>; any exception it throws rejects that set.</param>
    /// <param name="set">The loaded set, or null.</param>
    /// <param name="reason">Why nothing was loaded (each set's problem), or null on success.</param>
    /// <param name="newerThanTag">When given, only sets with a newer tag are considered (e.g. the bundled set's tag).</param>
    public bool TryLoadNewest(Func<StoredBoundaryFiles, RawBoundaryData> parse, BoundaryRules rules, BoundaryValidator validator,
        [NotNullWhen(true)] out BoundarySet? set, [NotNullWhen(false)] out string? reason, string? newerThanTag = null)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(validator);
        set = null;
        var problems = new List<string>();
        var manifests = ReadManifests(problems);
        foreach (var m in manifests)
        {
            if (newerThanTag is not null && CompareTags(m.Tag, newerThanTag) <= 0) continue;
            try
            {
                var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                string? bad = null;
                foreach (var (name, (sha, size)) in m.Files)
                {
                    var path = Path.Combine(CacheDir, m.Tag, name);
                    if (!File.Exists(path)) { bad = $"{name} is missing"; break; }
                    var bytes = File.ReadAllBytes(path);
                    if (bytes.LongLength != size) { bad = $"{name} has {bytes.LongLength} bytes, the manifest says {size}"; break; }
                    if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(sha, StringComparison.OrdinalIgnoreCase)) { bad = $"{name} does not match its SHA-256"; break; }
                    files[name] = bytes;
                }
                if (bad is not null) { problems.Add($"{m.Tag}: {bad}"); continue; }

                var raw = parse(new StoredBoundaryFiles(m.Tag, m.Date, files));
                var result = validator.Validate(raw, rules, out var valid);
                if (valid is null) { problems.Add($"{m.Tag}: {result.Reason}"); continue; }
                set = valid;
                reason = null;
                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                problems.Add($"{m.Tag}: {ex.Message}");
            }
        }
        reason = problems.Count == 0
            ? (newerThanTag is null ? "no stored boundary set" : $"no stored boundary set newer than {newerThanTag}")
            : string.Join("; ", problems);
        return false;
    }

    /// <summary>
    /// Orders release tags as versions ("v5.10.0" after "v5.2.0"; a leading 'v' is ignored). A tag that is not a
    /// version sorts before every version, and two such tags compare as text.
    /// </summary>
    public static int CompareTags(string a, string b)
    {
        var va = ParseTag(a);
        var vb = ParseTag(b);
        if (va is not null && vb is not null) return va.CompareTo(vb);
        if (va is not null) return 1;
        if (vb is not null) return -1;
        return string.CompareOrdinal(a, b);
    }

    private static Version? ParseTag(string tag)
    {
        var t = tag.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        if (!Version.TryParse(t, out var v)) return null;
        // 5.1 and 5.1.0 are the same release.
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    private sealed record Manifest(string Tag, DateOnly Date, IReadOnlyDictionary<string, (string Sha256, long Bytes)> Files);

    private List<Manifest> ReadManifests(List<string>? problems = null)
    {
        var list = new List<Manifest>();
        if (!Directory.Exists(CacheDir)) return list;
        foreach (var dir in Directory.EnumerateDirectories(CacheDir))
        {
            var file = Path.Combine(dir, ManifestFile);
            if (!File.Exists(file)) continue;   // a set still being written, or abandoned
            var folder = Path.GetFileName(dir);
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var tag = root.GetProperty("tag").GetString() ?? "";
                if (tag != folder) throw new InvalidDataException($"the manifest names {tag}");
                var date = DateOnly.ParseExact(root.GetProperty("date").GetString() ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var files = new Dictionary<string, (string, long)>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in root.GetProperty("files").EnumerateObject())
                {
                    if (!SafeName().IsMatch(f.Name)) throw new InvalidDataException($"unsafe file name {f.Name}");
                    files[f.Name] = (f.Value.GetProperty("sha256").GetString() ?? "", f.Value.GetProperty("bytes").GetInt64());
                }
                if (files.Count == 0) throw new InvalidDataException("the manifest lists no files");
                list.Add(new Manifest(tag, date, files));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
            {
                problems?.Add($"{folder}: unreadable manifest ({ex.Message})");
            }
        }
        list.Sort((x, y) => CompareTags(y.Tag, x.Tag) is int c && c != 0 ? c : y.Date.CompareTo(x.Date));
        return list;
    }

    private static void CheckName(string name, string param)
    {
        if (name is null || !SafeName().IsMatch(name)) throw new ArgumentException($"\"{name}\" is not a safe file name", param);
    }

    // One path segment: no separators, and not "." or "..".
    [GeneratedRegex(@"^(?!\.{1,2}$)[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex SafeName();
}
