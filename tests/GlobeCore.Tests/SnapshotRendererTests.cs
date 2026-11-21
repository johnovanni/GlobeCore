using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlobeCore.Wpf.Diagnostics;

namespace GlobeCore.Tests;

public class SnapshotRendererTests
{
    private static string TempPng() => Path.Combine(Path.GetTempPath(), "globecore-snap-" + Guid.NewGuid().ToString("N"), "shot.png");

    [Fact]
    public void NothingToRenderReturnsFalse() => Sta.Run(() =>
    {
        var path = TempPng();
        Assert.False(SnapshotRenderer.SavePng(new Window(), path));                       // no content
        Assert.False(SnapshotRenderer.SavePng(new Window { Content = new Border() }, path)); // never laid out: size 0
        Assert.False(File.Exists(path));
    });

    [Fact]
    public void SavesAPngAtTheWindowsDpi() => Sta.Run(() =>
    {
        // The window is never shown: its content is laid out by hand.
        var content = new Border { Width = 40, Height = 20, Background = Brushes.Red };
        var window = new Window { Content = content };
        content.Measure(new Size(40, 20));
        content.Arrange(new Rect(0, 0, 40, 20));
        var path = TempPng();
        try
        {
            Assert.True(SnapshotRenderer.SavePng(window, path));   // creates the folder too
            var dpi = VisualTreeHelper.GetDpi(window);
            using var s = File.OpenRead(path);
            var frame = new PngBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal((int)Math.Ceiling(40 * dpi.DpiScaleX), frame.PixelWidth);
            Assert.Equal((int)Math.Ceiling(20 * dpi.DpiScaleY), frame.PixelHeight);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    });
}
