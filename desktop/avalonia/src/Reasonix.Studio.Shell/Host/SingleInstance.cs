using System;
using System.Threading;

namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// Keeps one shell per machine, so a second launch raises the first instead of
/// opening a second window onto the same state.
/// </summary>
/// <remarks>
/// A named mutex rather than a lock file: the operating system releases it when
/// the process dies, so a crash never leaves a stale claim behind. The name is
/// derived from the installation identity, because two installs are allowed to
/// run side by side.
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// The mutex this process holds while it is the one that owns the name.
    /// </summary>
    private readonly Mutex _mutex;

    /// <summary>
    /// Whether this process owns the name, or merely observed that another does.
    /// </summary>
    private readonly bool _owned;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstance"/> class.
    /// </summary>
    /// <param name="identity">The installation's identity; two installs must differ.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identity"/> is null.</exception>
    public SingleInstance(string identity)
    {
        if (identity is null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        var name = NameFor(identity);
        _mutex = new Mutex(initiallyOwned: true, name, out _owned);
    }

    /// <summary>
    /// Gets a value indicating whether this process is the first for its identity.
    /// </summary>
    public bool IsFirst => _owned;

    /// <summary>
    /// Builds the mutex name for one installation identity.
    /// </summary>
    /// <param name="identity">The installation's identity.</param>
    /// <returns>The name the platform uses for the claim.</returns>
    public static string NameFor(string identity)
    {
        return "Reasonix.Studio." + identity;
    }

    /// <summary>
    /// Releases the claim, so a later launch may take it.
    /// </summary>
    public void Dispose()
    {
        if (_owned)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
