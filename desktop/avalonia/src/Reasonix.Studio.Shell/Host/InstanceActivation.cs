using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// Carries "raise the window that is already running" from a second launch to the
/// first.
/// </summary>
/// <remarks>
/// A named event rather than a socket or a file: the first process creates it and
/// waits, a second launch sets it and exits, and the operating system destroys it
/// when the owner dies. A file would need a stale-claim policy and a socket would
/// need a port nobody reserved; neither is worth it for one bit.
///
/// Named events exist on Windows only. Elsewhere this class carries nothing: the
/// claim still keeps one shell per machine, but a second launch has no way to
/// raise the first and closes. That is the honest answer on a platform with no
/// such surface, rather than a channel that looks present and never fires.
///
/// The wait runs on a background thread and reports through
/// <see cref="Activated"/>, which the window marshals to its own thread. A shell
/// that is shutting down stops waiting with <see cref="Dispose"/>.
/// </remarks>
public sealed class InstanceActivation : IDisposable
{
    /// <summary>
    /// How long one wait blocks before it checks whether this shell is closing.
    /// </summary>
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The event the first process waits on and a later launch sets, or null on a
    /// platform without named events.
    /// </summary>
    private readonly EventWaitHandle? _signal;

    /// <summary>
    /// Cancels the wait when this shell closes.
    /// </summary>
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();

    /// <summary>
    /// The wait loop, or null when this process is not listening.
    /// </summary>
    private readonly Task? _waiting;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceActivation"/> class.
    /// </summary>
    /// <param name="identity">The installation's identity, the same one the claim uses.</param>
    /// <param name="listen">True in the first process, which waits; false in a later launch.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identity"/> is null.</exception>
    public InstanceActivation(string identity, bool listen)
    {
        if (identity is null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, NameFor(identity));
        if (listen)
        {
            _waiting = Task.Run(() => Wait(_stopping.Token));
        }
    }

    /// <summary>
    /// Raised in the first process when a later launch asks for its window.
    /// </summary>
    /// <remarks>
    /// Raised on a background thread. A listener that draws must marshal to the
    /// interface thread itself, which is what <see cref="App"/> does.
    /// </remarks>
    public event Action? Activated;

    /// <summary>
    /// Builds the name the activation event is created under for one identity.
    /// </summary>
    /// <param name="identity">The installation's identity.</param>
    /// <returns>The name the platform uses for the event.</returns>
    public static string NameFor(string identity)
    {
        return "Reasonix.Studio.Activate." + identity;
    }

    /// <summary>
    /// Builds the identity a launch's claim and activation are keyed by.
    /// </summary>
    /// <returns>A short, stable identity derived from the install's own folder.</returns>
    /// <remarks>
    /// The folder rather than the version: a preview build updated in place is the
    /// same installation, and two folders are two installations allowed to run
    /// side by side.
    /// </remarks>
    public static string Identity()
    {
        var folder = AppContext.BaseDirectory.ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(folder));
        return Convert.ToHexString(digest, 0, 8).ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Asks the first process to raise its window.
    /// </summary>
    /// <param name="identity">The installation's identity.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identity"/> is null.</exception>
    /// <remarks>
    /// A launch calling this does not wait for an answer: the second process has
    /// nothing left to do once the first has been told, and a first process that
    /// is gone has no window to raise.
    /// </remarks>
    public static void Raise(string identity)
    {
        if (identity is null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var signal = EventWaitHandle.OpenExisting(NameFor(identity));
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The first process is closing. Starting a second window would add a
            // second writer for one transcript, which is worse than a launch that
            // does nothing.
        }
    }

    /// <summary>
    /// Stops waiting for later launches.
    /// </summary>
    public void Dispose()
    {
        _stopping.Cancel();
        _signal?.Dispose();
        _stopping.Dispose();

        // The loop notices the cancellation on its next poll; waiting for it here
        // would block the interface thread that closes the window.
        _ = _waiting;
    }

    /// <summary>
    /// Waits for a later launch and reports each one.
    /// </summary>
    /// <param name="stopping">Cancels the wait when the shell closes.</param>
    private void Wait(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                if (_signal is not null && _signal.WaitOne(s_pollInterval))
                {
                    Activated?.Invoke();
                }
            }
            catch (ObjectDisposedException)
            {
                // The shell closed while this loop was waiting: the outcome asked
                // for, not a failure to report.
                return;
            }
        }
    }
}
