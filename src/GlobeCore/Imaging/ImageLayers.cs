namespace GlobeCore.Imaging;

/// <summary>A dense straight-alpha BGRA layer drawn over the image.</summary>
public sealed class BitmapBlendLayer(Bgra32Image image, ImagePass pass, int order) : ISizedImageLayer
{
    private readonly Bgra32Image _image = image ?? throw new ArgumentNullException(nameof(image));
    public ImagePass Pass { get; } = pass;
    public int Order { get; } = order;
    public int Width => _image.Width;
    public int Height => _image.Height;

    public void BlendRow(int y, Span<byte> bgraRow)
    {
        var layer = _image.Pixels;
        int row = y * _image.Width * 4;
        for (int p = 0; p < bgraRow.Length; p += 4)
        {
            int lp = row + p;
            int al = layer[lp + 3];
            if (al == 0) continue;
            if (al == 255)
            {
                bgraRow[p] = layer[lp]; bgraRow[p + 1] = layer[lp + 1]; bgraRow[p + 2] = layer[lp + 2];
                continue;
            }
            double a = al / 255.0;
            bgraRow[p] = GlobeImageComposer.Mix(layer[lp], bgraRow[p], a);
            bgraRow[p + 1] = GlobeImageComposer.Mix(layer[lp + 1], bgraRow[p + 1], a);
            bgraRow[p + 2] = GlobeImageComposer.Mix(layer[lp + 2], bgraRow[p + 2], a);
        }
    }
}

/// <summary>A <see cref="SparseOverlay"/> drawn over the image (same result as the dense layer, far less memory).</summary>
public sealed class SparseOverlayLayer(SparseOverlay overlay, ImagePass pass, int order) : ISizedImageLayer
{
    private readonly SparseOverlay _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
    public ImagePass Pass { get; } = pass;
    public int Order { get; } = order;
    public int Width => _overlay.Width;
    public int Height => _overlay.Height;

    public void BlendRow(int y, Span<byte> bgraRow)
    {
        var idx = _overlay.PixelIndices;
        var col = _overlay.Bgra;
        int rowStart = y * _overlay.Width;
        for (int n = _overlay.RowStarts[y], end = _overlay.RowStarts[y + 1]; n < end; n++)
        {
            int p = (idx[n] - rowStart) * 4, c = n * 4;
            int al = col[c + 3];
            if (al == 255)
            {
                bgraRow[p] = col[c]; bgraRow[p + 1] = col[c + 1]; bgraRow[p + 2] = col[c + 2];
                continue;
            }
            double a = al / 255.0;
            bgraRow[p] = GlobeImageComposer.Mix(col[c], bgraRow[p], a);
            bgraRow[p + 1] = GlobeImageComposer.Mix(col[c + 1], bgraRow[p + 1], a);
            bgraRow[p + 2] = GlobeImageComposer.Mix(col[c + 2], bgraRow[p + 2], a);
        }
    }
}
