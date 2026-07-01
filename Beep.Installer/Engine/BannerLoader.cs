using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Beep.Installer.Engine;

/// <summary>
/// Loads banner images for the installer UI.
/// Resizes the source to fit a target width while preserving aspect ratio.
/// </summary>
public static class BannerLoader
{
    /// <summary>Load a banner image, optionally resizing to fit <paramref name="targetWidth"/>. Returns null on failure.</summary>
    public static Image? Load(string? path, int targetWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var src = Image.FromFile(path);
            if (targetWidth <= 0 || src.Width == targetWidth) return CloneImage(src);

            var ratio = (double)targetWidth / src.Width;
            var newHeight = Math.Max(1, (int)(src.Height * ratio));
            var dst = new Bitmap(targetWidth, newHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, targetWidth, newHeight);
            }
            return dst;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Try to load a banner next to the running exe, then fall back to the absolute path.</summary>
    public static Image? LoadNextToExe(string fileName, int targetWidth = 0)
    {
        var next = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(next)) return Load(next, targetWidth);
        return null;
    }

    private static Image CloneImage(Image src)
    {
        var clone = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(clone);
        g.DrawImage(src, 0, 0);
        return clone;
    }
}
