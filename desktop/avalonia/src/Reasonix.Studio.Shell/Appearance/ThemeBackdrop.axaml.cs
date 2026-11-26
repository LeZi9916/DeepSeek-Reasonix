using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Reasonix.Studio.Kernel.Client;
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Appearance;

/// <summary>Draws the active installed theme's cropped image, overlay and optional sky.</summary>
public sealed partial class ThemeBackdrop : UserControl
{
    /// <summary>The background configuration of the selected pack.</summary>
    private JsonObject? _background;

    /// <summary>The image owned by this view.</summary>
    private Bitmap? _bitmap;

    /// <summary>Invalidates a download when a different theme is selected.</summary>
    private int _generation;

    /// <summary>Whether the current conversation is running.</summary>
    private bool _busy;

    /// <summary>Initializes a new instance of the <see cref="ThemeBackdrop"/> class.</summary>
    public ThemeBackdrop()
    {
        InitializeComponent();
        Frame.SizeChanged += (_, _) => Crop();
        ActualThemeVariantChanged += (_, _) => Paint();
        DetachedFromVisualTree += (_, _) =>
        {
            _generation++;
            ClearPicture();
        };
    }

    /// <summary>Sets the theme's activity-dependent opacity.</summary>
    /// <param name="busy">Whether a turn is active.</param>
    internal void SetBusy(bool busy)
    {
        if (busy != _busy)
        {
            _busy = busy;
            Paint();
        }
    }

    /// <summary>Applies the effective platform and user motion policy.</summary>
    /// <param name="enabled">Whether cloud drift may run.</param>
    internal void SetMotion(bool enabled) => Sky.Move(enabled);

    /// <summary>Replaces the current artwork with an image from the active kernel.</summary>
    /// <param name="pack">Active pack, or null for the default Studio theme.</param>
    /// <param name="api">Authenticated kernel client.</param>
    /// <param name="token">Window lifetime.</param>
    /// <param name="current">Whether the palette read still owns publication.</param>
    /// <param name="publish">The resource change that accompanies the artwork.</param>
    /// <returns>The image load.</returns>
    internal async Task ApplyAsync(JsonObject? pack, StudioPageClient api, CancellationToken token, Func<bool>? current = null, Action? publish = null)
    {
        var ticket = ++_generation;
        var background = pack?["background"] as JsonObject;
        if ((bool?)background?["image"] != true || (string?)pack?["id"] is not { } id)
        {
            Commit(null);
            return;
        }

        var bytes = await api.ThemeBackgroundAsync(id, token).ConfigureAwait(true);
        if (ticket != _generation || token.IsCancellationRequested || current?.Invoke() == false)
        {
            return;
        }

        using var stream = new MemoryStream(bytes, writable: false);

        // Read the dimensions before allocating pixels. Decode the longer side first
        // so portrait packs and ultrawide illustrations obey the same budget.
        using var codec = SkiaSharp.SKCodec.Create(stream) ?? throw new InvalidDataException("Unreadable theme artwork.");
        var size = codec.Info;
        if (size.Width < 1 || size.Height < 1)
        {
            throw new InvalidDataException("Invalid theme image dimensions.");
        }

        using var pixels = new MemoryStream(bytes, writable: false);
        var next = Math.Max(size.Width, size.Height) <= 2560
            ? new Bitmap(pixels)
            : size.Width >= size.Height ? Bitmap.DecodeToWidth(pixels, 2560) : Bitmap.DecodeToHeight(pixels, 2560);
        Commit(next);

        void Commit(Bitmap? image)
        {
            if (ticket != _generation || token.IsCancellationRequested || current?.Invoke() == false)
            {
                image?.Dispose();
                return;
            }

            var prior = _bitmap;
            _background = background;
            _bitmap = image;
            Picture.Source = image;
            Sky.Configure(pack?["sky"] as JsonObject);
            publish?.Invoke();
            Crop();
            Paint();
            prior?.Dispose();
        }
    }

    /// <summary>Reads a normalized theme opacity or focal coordinate.</summary>
    /// <param name="key">Pack field.</param>
    /// <param name="fallback">Value when absent.</param>
    /// <returns>A fraction.</returns>
    private double Fraction(string key, double fallback) => Math.Clamp((double?)_background?[key] ?? fallback, 0, 1);

    /// <summary>Matches the pack's background-size cover and focal position.</summary>
    private void Crop()
    {
        if (_bitmap is null || Frame.Bounds.Width <= 0 || Frame.Bounds.Height <= 0)
        {
            return;
        }

        var scale = Math.Max(Frame.Bounds.Width / _bitmap.PixelSize.Width, Frame.Bounds.Height / _bitmap.PixelSize.Height);
        Picture.Width = _bitmap.PixelSize.Width * scale;
        Picture.Height = _bitmap.PixelSize.Height * scale;
        Canvas.SetLeft(Picture, (Frame.Bounds.Width - Picture.Width) * Fraction("focusX", 0.5));
        Canvas.SetTop(Picture, (Frame.Bounds.Height - Picture.Height) * Fraction("focusY", 0.5));
    }

    /// <summary>Applies the chosen layer alpha in the active color scheme.</summary>
    private void Paint()
    {
        Picture.Opacity = Fraction(_busy ? "taskOpacity" : "homeOpacity", 1);
        Scrim.IsVisible = _bitmap is not null;
        Scrim.Opacity = Fraction("overlayStrength", 0) * (ActualThemeVariant == ThemeVariant.Light ? 0.58 : 1);
    }

    /// <summary>Releases an image only after removing it from the visual tree.</summary>
    private void ClearPicture()
    {
        Picture.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
