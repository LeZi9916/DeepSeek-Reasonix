using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.IO;

namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// Draws the mark the tray and the window show, so the shell ships no binary
/// asset it would have to keep in step with the theme.
/// </summary>
/// <remarks>
/// A small filled tile with three cut bars: enough to be findable in a tray at
/// 16 pixels, and drawn here rather than checked in so a build has one file list
/// on every platform. The icon is created once and reused; a second render would
/// leak the bitmap it replaced.
/// </remarks>
public static class TrayGlyph
{
    /// <summary>
    /// The edge, in pixels, of the bitmap drawn here.
    /// </summary>
    /// <remarks>
    /// Windows scales a tray icon down to 16, and a 32-pixel source stays legible
    /// through that halving; a larger one costs memory for no pixels a tray shows.
    /// </remarks>
    private const int Size = 32;

    /// <summary>
    /// The icon handed out to the tray and the window.
    /// </summary>
    private static WindowIcon? s_icon;

    /// <summary>
    /// Gets the mark, drawing it on first use.
    /// </summary>
    /// <returns>The icon, or null when this process cannot render one.</returns>
    /// <remarks>
    /// Null rather than an exception: a host with no renderer can still run, it
    /// simply shows no tray mark, which is the same outcome as a platform with no
    /// tray at all.
    /// </remarks>
    public static WindowIcon? Icon()
    {
        if (s_icon is not null)
        {
            return s_icon;
        }

        try
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
            using (var context = bitmap.CreateDrawingContext())
            {
                // The tile is a rounded rectangle, which DrawingContext takes
                // through its pen overload; the plain FillRectangle only takes a
                // square Rect.
                context.DrawRectangle(Brushes.Black, null, new RoundedRect(new Rect(2, 2, Size - 4, Size - 4), 7));
                context.FillRectangle(Brushes.White, new Rect(10, 10, 12, 3));
                context.FillRectangle(Brushes.White, new Rect(10, 16, 8, 3));
                context.FillRectangle(Brushes.White, new Rect(10, 22, 10, 3));
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            stream.Position = 0;
            s_icon = new WindowIcon(stream);
            return s_icon;
        }
        catch (Exception)
        {
            // A renderer that cannot draw the mark is not a reason to fail the
            // launch; the window is what the person opened.
            return null;
        }
    }
}
