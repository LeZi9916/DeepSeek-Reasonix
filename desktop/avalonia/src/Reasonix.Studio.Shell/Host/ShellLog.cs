using System;
using System.Globalization;
using System.IO;

namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// The shell's own launch log, and the place a person is sent to read it.
/// </summary>
/// <remarks>
/// A shell that will not start has nothing on screen to say why, so the file on
/// disk is the only account of it. It is written under the platform's user state
/// directory rather than beside the binary, which may be in a read-only install.
/// </remarks>
public static class ShellLog
{
    /// <summary>
    /// The file name the log is written to.
    /// </summary>
    private const string FileName = "shell.log";

    /// <summary>
    /// The directory the shell keeps its own state in.
    /// </summary>
    /// <returns>The directory, created on first use.</returns>
    public static string Directory()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        var path = System.IO.Path.Combine(root, "reasonix", "studio");
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Gets the log file's path.
    /// </summary>
    /// <returns>The path the log is appended to.</returns>
    public static string Path()
    {
        return System.IO.Path.Combine(Directory(), FileName);
    }

    /// <summary>
    /// Appends one line to the log.
    /// </summary>
    /// <param name="line">The line to append.</param>
    public static void Line(string line)
    {
        try
        {
            // A log that throws while logging would replace the failure it was
            // recording with its own, so a write error is swallowed.
            File.AppendAllText(
                Path(),
                string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.UtcNow:O} {line}{Environment.NewLine}"));
        }
        catch (Exception)
        {
            // Nothing can be done here that is better than losing the line.
        }
    }
}
