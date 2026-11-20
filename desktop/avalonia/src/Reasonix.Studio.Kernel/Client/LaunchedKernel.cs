using Reasonix.Studio.Kernel.Boundary;
using System;
using System.Diagnostics;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// A running kernel, its handshake and the way to stop it.
/// </summary>
/// <remarks>
/// Disposing stops the child. The kernel is a child process this shell owns: a
/// launch that left it running would leave a listener holding a port and a gate
/// nobody can reach.
/// </remarks>
public sealed class LaunchedKernel : IDisposable
{
    /// <summary>
    /// The child process.
    /// </summary>
    private readonly Process _process;

    /// <summary>
    /// The handshake the kernel announced with.
    /// </summary>
    private readonly LaunchHandshake _handshake;

    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchedKernel"/> class.
    /// </summary>
    /// <param name="process">The child process.</param>
    /// <param name="handshake">The handshake the kernel announced with.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="process"/> or <paramref name="handshake"/> is null.
    /// </exception>
    public LaunchedKernel(Process process, LaunchHandshake handshake)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _handshake = handshake ?? throw new ArgumentNullException(nameof(handshake));
    }

    /// <summary>
    /// Gets the origin and credential this launch is guarded by.
    /// </summary>
    public LaunchHandshake Handshake => _handshake;

    /// <summary>
    /// Gets the origin the page is served from.
    /// </summary>
    public Uri Origin => _handshake.Origin;

    /// <summary>
    /// Gets the child's process id, for a log line that names what was started.
    /// </summary>
    public int Pid => _process.Id;

    /// <summary>
    /// Gets a value indicating whether the kernel is still running.
    /// </summary>
    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (Exception)
            {
                // A process that cannot be asked has already gone.
                return true;
            }
        }
    }

    /// <summary>
    /// Reads the cookie this launch arms the window with.
    /// </summary>
    /// <returns>The credential cookie for this handshake.</returns>
    public LaunchCookie Cookie()
    {
        return new LaunchCookie(_handshake.Origin, _handshake.Token);
    }

    /// <summary>
    /// Stops the kernel.
    /// </summary>
    public void Dispose()
    {
        HostLauncher.Kill(_process);
    }
}
