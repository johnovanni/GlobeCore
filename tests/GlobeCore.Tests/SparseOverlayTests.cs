using GlobeCore.Imaging;

namespace GlobeCore.Tests;

public class SparseOverlayTests
{
    private static void Put(Bgra32Image img, int x, int y, byte b, byte g, byte r, byte a)
    { int i = (y * img.Width + x) * 4; img.Pixels[i] = b; img.Pixels[i + 1] = g; img.Pixels[i + 2] = r; img.Pixels[i + 3] = a; }

    [Fact]
    public void KeepsOnlyNonTransparentPixelsInOrder()
    {
        var img = Bgra32Image.Create(4, 3);
        Put(img, 2, 0, 1, 2, 3, 255);
        Put(img, 1, 2, 10, 20, 30, 128);
        Put(img, 3, 2, 9, 9, 9, 0);   // colour but alpha 0: dropped
        var s = SparseOverlay.FromImage(img);
        Assert.Equal((4, 3), (s.Width, s.Height));
        Assert.Equal(2, s.Count);
        Assert.Equal([2, 9], s.PixelIndices);
        Assert.Equal([1, 2, 3, 255, 10, 20, 30, 128], s.Bgra);
        Assert.Equal([0, 1, 1, 2], s.RowStarts);   // row y spans entries [RowStarts[y], RowStarts[y+1])
    }
}
