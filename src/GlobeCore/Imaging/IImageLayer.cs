namespace GlobeCore.Imaging;

/// <summary>Surface layers are drawn before the night shade (and so are shaded); AboveNight layers after it.</summary>
public enum ImagePass { Surface, AboveNight }

public interface IImageLayer
{
    ImagePass Pass { get; }
    /// <summary>Layers in one pass are drawn in ascending Order.</summary>
    int Order { get; }
    /// <summary>Blend this layer into row <paramref name="y"/> of the image (straight BGRA, Width*4 bytes).</summary>
    void BlendRow(int y, Span<byte> bgraRow);
}

/// <summary>A layer with a fixed pixel size, which the composer checks against the base image.</summary>
internal interface ISizedImageLayer : IImageLayer
{
    int Width { get; }
    int Height { get; }
}
