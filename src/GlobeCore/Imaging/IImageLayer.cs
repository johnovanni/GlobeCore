namespace GlobeCore.Imaging;

/// <summary>Surface layers are drawn before the night shade (and so are shaded); AboveNight layers after it.</summary>
public enum ImagePass { Surface, AboveNight }

public interface IImageLayer
{
    ImagePass Pass { get; }
    /// <summary>Layers in one pass are drawn in ascending Order.</summary>
    int Order { get; }
    /// <summary>
    /// Blend this layer into row <paramref name="y"/> of the image (straight BGRA, Width*4 bytes). The composer calls it
    /// for different rows at the same time (from a <see cref="Parallel.For(int, int, Action{int})"/> over the rows), so
    /// it must be thread-safe: read shared state only, and write only <paramref name="bgraRow"/>.
    /// </summary>
    void BlendRow(int y, Span<byte> bgraRow);
}

/// <summary>A layer with a fixed pixel size, which the composer checks against the base image.</summary>
internal interface ISizedImageLayer : IImageLayer
{
    int Width { get; }
    int Height { get; }
}
