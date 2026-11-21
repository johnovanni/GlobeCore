using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlobeCore.Wpf.Diagnostics;

/// <summary>
/// Saves a window as a PNG by having WPF render it (<see cref="RenderTargetBitmap"/>), for an app's own snapshot
/// diagnostics. No screen capture: what is saved is the window's visual tree, whatever is on the screen.
/// </summary>
public static class SnapshotRenderer
{
    /// <summary>
    /// Renders the window's client area at the window's DPI and saves it as a PNG at <paramref name="path"/> (its folder
    /// is created). Returns false, saving nothing, when there is nothing to render: no content element, or one not laid
    /// out yet (zero size). Rendering and file errors are thrown, for the caller to report.
    /// </summary>
    public static bool SavePng(Window window, string path)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (window.Content is not FrameworkElement content) return false;
        var dpi = VisualTreeHelper.GetDpi(window);
        int pw = (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX);
        int ph = (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY);
        if (pw <= 0 || ph <= 0) return false;
        var rtb = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var stream = File.Create(full);
        encoder.Save(stream);
        return true;
    }
}
