using System.Drawing;
using System.Drawing.Imaging;

namespace Astronomy.Diagnostics;

/// <summary>
/// Win32 screen-pixel capture to PNG (<see cref="Graphics.CopyFromScreen(int, int, int, int, Size)"/>): grabs the
/// literal rendered output of a window regardless of its UI framework (WinForms, WinUI/XAML, Skia) — unlike a
/// framework's own draw-to-bitmap API, which returns blank for some compositors. Windows-only. Pairs with
/// <see cref="Log.NewObservationScreenshotPath"/> for the Ctrl+N observation flow: the caller supplies the
/// owner window's physical-pixel bounds; this owns the grab + encode.
/// </summary>
public static class ScreenCapture
{
    /// <summary>Capture the screen rectangle at physical-pixel (<paramref name="x"/>, <paramref name="y"/>) of the
    /// given size and save a PNG to <paramref name="path"/> (creating its folder). Returns <paramref name="path"/>
    /// on success or <c>null</c> on any failure — best-effort, never throwing into the caller; a non-positive size
    /// returns <c>null</c>.</summary>
    public static string? ToPng(int x, int y, int width, int height, string path)
    {
        if (width <= 0 || height <= 0) return null;
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using Bitmap bmp = new(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new Size(width, height));
            }
            bmp.Save(path, ImageFormat.Png);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warn("Screen capture failed", ex);
            return null;
        }
    }
}
