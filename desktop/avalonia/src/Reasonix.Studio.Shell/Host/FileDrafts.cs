using Reasonix.Studio.Pages.Composer;
using System;
using System.Collections.Generic;
using System.IO;

namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// Keeps the composer's drafts in one directory beside the shell's other state.
/// </summary>
/// <remarks>
/// One file per draft rather than one map: a write touches only the conversation
/// it belongs to, and a file that cannot be read costs that conversation's draft
/// rather than every draft on the machine.
///
/// Every failure here is swallowed on purpose. Storage can be disabled, full, or
/// taken away while the window is open, and none of those is a reason for a send
/// to fail: the live line in the composer is the one the person is looking at.
/// </remarks>
public sealed class FileDrafts : IDraftStorage
{
    /// <summary>
    /// Where the drafts are kept.
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileDrafts"/> class.
    /// </summary>
    /// <param name="directory">The directory drafts are kept in.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directory"/> is null.</exception>
    public FileDrafts(string directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    /// <summary>
    /// Builds the store the shell uses: drafts live beside the window's geometry.
    /// </summary>
    /// <returns>The store, whether or not its directory exists yet.</returns>
    public static FileDrafts Default()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new FileDrafts(Path.Combine(local, "Reasonix", "Studio", "drafts"));
    }

    /// <inheritdoc/>
    public string Read(string key)
    {
        try
        {
            var path = PathOf(key);
            return path is not null && File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception)
        {
            // An unreadable draft reads as no draft: the composer opens empty,
            // which is where it would open on a first launch anyway.
            return string.Empty;
        }
    }

    /// <inheritdoc/>
    public void Write(string key, string text)
    {
        try
        {
            var path = PathOf(key);
            if (path is null)
            {
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            Directory.CreateDirectory(_directory);
            File.WriteAllText(path, text);
        }
        catch (Exception)
        {
            // A refused write leaves the live draft where it is, on screen. The
            // person's words are not lost by the storage being unavailable.
        }
    }

    /// <summary>
    /// Names the file one draft's key is kept in.
    /// </summary>
    /// <param name="key">The draft's key.</param>
    /// <returns>
    /// The path, or null when the key names nothing. The name is a hash rather
    /// than the key itself: a key holds a transcript path, and a path is not a
    /// filename on every platform this ships to.
    /// </returns>
    private string? PathOf(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        var hash = 0xcbf29ce484222325UL;
        foreach (var unit in key)
        {
            hash ^= unit;
            hash *= 0x00000100000001B3UL;
        }

        return Path.Combine(_directory, hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
    }
}
