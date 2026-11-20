using Reasonix.Studio.Pages.Composer;
using System;
using System.IO;

namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// Remembers which key sends, as this machine last chose it.
/// </summary>
/// <remarks>
/// A machine-local preference rather than a project setting, for the same reason
/// a draft is: it answers what this person's hands do, and a repository that
/// carried it would impose one typist's habit on everyone who opened it.
///
/// A file that cannot be read or written falls back to Enter, which is what the
/// window would use on a first launch. The preference is a convenience, never a
/// reason to refuse to start.
/// </remarks>
public static class SendShortcutStore
{
    /// <summary>
    /// The value the file holds for the modifier chord.
    /// </summary>
    private const string ModifierEnter = "modifier_enter";

    /// <summary>
    /// Reads the preference the last launch stored.
    /// </summary>
    /// <returns>The stored shortcut, or Enter when nothing usable was stored.</returns>
    public static SendShortcut Load()
    {
        try
        {
            var path = PathOf();
            if (!File.Exists(path))
            {
                return SendShortcut.Enter;
            }

            return string.Equals(File.ReadAllText(path).Trim(), ModifierEnter, StringComparison.Ordinal)
                ? SendShortcut.ModifierEnter
                : SendShortcut.Enter;
        }
        catch (Exception)
        {
            return SendShortcut.Enter;
        }
    }

    /// <summary>
    /// Stores the preference for the next launch.
    /// </summary>
    /// <param name="shortcut">The shortcut this machine now uses.</param>
    public static void Save(SendShortcut shortcut)
    {
        try
        {
            var path = PathOf();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, shortcut == SendShortcut.ModifierEnter ? ModifierEnter : "enter");
        }
        catch (Exception)
        {
            // The window keeps the choice it is using; only the next launch
            // forgets it.
        }
    }

    /// <summary>
    /// Names the file the preference is kept in.
    /// </summary>
    /// <returns>The path, under the same directory the drafts live in.</returns>
    private static string PathOf()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "Reasonix", "Studio", "send-shortcut.txt");
    }
}
