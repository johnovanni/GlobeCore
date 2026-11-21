namespace GlobeCore.Storage;

/// <summary>Writes files so a reader never sees a half-written one, and a failed write never throws for I/O reasons.</summary>
public static class AtomicFile
{
    private const int Attempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Writes to <c>file.tmp</c>, then moves it over <paramref name="file"/>, retrying the move (e.g. while an
    /// antivirus scan holds the destination). Returns false, leaving no .tmp behind, if it still fails.
    /// </summary>
    /// <param name="file">Destination; its folder is created if missing.</param>
    /// <param name="text">Written as UTF-8.</param>
    /// <param name="error">The I/O failure when this returns false; otherwise null.</param>
    /// <returns>True if the file was replaced.</returns>
    public static bool TryWriteAllText(string file, string text, out Exception? error)
        => TryWrite(file, tmp => File.WriteAllText(tmp, text), out error);

    /// <summary>Like <see cref="TryWriteAllText"/>, for bytes.</summary>
    /// <param name="file">Destination; its folder is created if missing.</param>
    /// <param name="bytes">The new content.</param>
    /// <param name="error">The I/O failure when this returns false; otherwise null.</param>
    /// <returns>True if the file was replaced.</returns>
    public static bool TryWriteAllBytes(string file, byte[] bytes, out Exception? error)
        => TryWrite(file, tmp => File.WriteAllBytes(tmp, bytes), out error);

    private static bool TryWrite(string file, Action<string> writeTemp, out Exception? error)
    {
        error = null;
        var tmp = file + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            writeTemp(tmp);
            for (var i = 1; ; i++)
            {
                try
                {
                    File.Move(tmp, file, overwrite: true);
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (i >= Attempts) throw;
                    Thread.Sleep(RetryDelay);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex;
            try { File.Delete(tmp); } catch (Exception) { }
            return false;
        }
    }
}
