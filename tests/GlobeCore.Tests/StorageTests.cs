using System.IO;
using GlobeCore.Storage;

namespace GlobeCore.Tests;

/// <summary>AtomicFile: the write-to-temp-then-move used by every store.</summary>
public sealed class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "globecore-storage-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void WritesTextAndBytesAndCreatesTheFolder()
    {
        var file = Path.Combine(_root, "a", "b.txt");
        Assert.True(AtomicFile.TryWriteAllText(file, "hello", out var error));
        Assert.Null(error);
        Assert.Equal("hello", File.ReadAllText(file));
        Assert.True(AtomicFile.TryWriteAllBytes(file, [1, 2, 3], out _));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public void SaveWhileDestinationLockedFailsWithoutThrowingAndLeavesNoTmp()
    {
        var file = Path.Combine(_root, "settings.json");
        Assert.True(AtomicFile.TryWriteAllText(file, "one", out _));

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(AtomicFile.TryWriteAllText(file, "two", out var error));
            Assert.NotNull(error);
            Assert.False(File.Exists(file + ".tmp"));
        }

        Assert.Equal("one", File.ReadAllText(file));
        Assert.True(AtomicFile.TryWriteAllText(file, "two", out _));
        Assert.Equal("two", File.ReadAllText(file));
    }
}
