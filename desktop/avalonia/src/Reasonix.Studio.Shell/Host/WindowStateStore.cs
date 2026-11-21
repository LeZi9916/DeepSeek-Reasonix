using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// Remembers the window's place between launches.
/// </summary>
/// <remarks>
/// A geometry read back on another monitor could sit off-screen, so the store
/// answers what was written and the window decides whether that is still on a
/// screen; deciding here would need a display the store does not have.
/// </remarks>
public sealed class WindowStateStore
{
    /// <summary>
    /// The width a window opens at when nothing was remembered.
    /// </summary>
    public const double DefaultWidth = 1440;

    /// <summary>
    /// The height a window opens at when nothing was remembered.
    /// </summary>
    public const double DefaultHeight = 900;

    /// <summary>
    /// Where the geometry is kept on disk.
    /// </summary>
    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowStateStore"/> class.
    /// </summary>
    /// <param name="path">The file the geometry is kept in.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
    public WindowStateStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>
    /// Gets the geometry to open with: what was remembered, or the default size.
    /// </summary>
    /// <returns>The remembered geometry, or the default when nothing was written or the file is unreadable.</returns>
    public WindowGeometry Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return Default();
            }

            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path));
            if (stored is null || stored.Width <= 0 || stored.Height <= 0)
            {
                return Default();
            }

            return new WindowGeometry(stored.X, stored.Y, stored.Width, stored.Height, stored.Maximised, stored.RailWidth, stored.Theme, stored.ReducedEffects, stored.QueueDuringRun);
        }
        catch (Exception)
        {
            // An unreadable file is not a reason to refuse to open: the window
            // falls back to its default size, which is where it would start on a
            // first launch anyway.
            return Default();
        }
    }

    /// <summary>
    /// Remembers the geometry for the next launch.
    /// </summary>
    /// <param name="geometry">Where the window was and how it was sized.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="geometry"/> is null.</exception>
    public void Save(WindowGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stored = new Stored
        {
            X = geometry.X,
            Y = geometry.Y,
            Width = geometry.Width,
            Height = geometry.Height,
            Maximised = geometry.Maximised,
            RailWidth = geometry.RailWidth,
            Theme = geometry.Theme,
            ReducedEffects = geometry.ReducedEffects,
            QueueDuringRun = geometry.QueueDuringRun,
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(stored));
    }

    /// <summary>
    /// Reads the geometry the window opens at before anything was remembered.
    /// </summary>
    /// <returns>A centred-size window that remembers no position.</returns>
    private static WindowGeometry Default()
    {
        return new WindowGeometry(null, null, DefaultWidth, DefaultHeight, Maximised: false);
    }

    /// <summary>
    /// The file's own shape, kept separate so the public record can stay
    /// immutable and free of serialisation attributes.
    /// </summary>
    private sealed record Stored
    {
        /// <summary>Gets a value indicating whether messages sent during a run are queued.</summary>
        public bool QueueDuringRun
        {
            get; init;
        }

        /// <summary>Gets a value indicating whether to reduce visual effects.</summary>
        public bool ReducedEffects
        {
            get; init;
        }

        /// <summary>Gets the preferred width of the expanded workspace.</summary>
        public double RailWidth { get; init; } = 264;

        /// <summary>Gets the explicit theme, or null when following the system.</summary>
        public string? Theme
        {
            get; init;
        }

        /// <summary>
        /// Gets the window's left edge.
        /// </summary>
        [JsonPropertyName("x")]
        public double? X
        {
            get; init;
        }

        /// <summary>
        /// Gets the window's top edge.
        /// </summary>
        [JsonPropertyName("y")]
        public double? Y
        {
            get; init;
        }

        /// <summary>
        /// Gets the window's width.
        /// </summary>
        [JsonPropertyName("width")]
        public double Width
        {
            get; init;
        }

        /// <summary>
        /// Gets the window's height.
        /// </summary>
        [JsonPropertyName("height")]
        public double Height
        {
            get; init;
        }

        /// <summary>
        /// Gets a value indicating whether the window was maximised.
        /// </summary>
        [JsonPropertyName("maximised")]
        public bool Maximised
        {
            get; init;
        }
    }
}
