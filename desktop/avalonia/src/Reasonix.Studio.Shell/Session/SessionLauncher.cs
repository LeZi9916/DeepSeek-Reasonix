using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Session;

/// <summary>
/// Starts the conversation a window shows: finds the kernel, starts it, and opens
/// one engine over it.
/// </summary>
/// <remarks>
/// The launch is separated from the window so that "there is no kernel here" is an
/// answer rather than a crash. A shell built without the host beside it, or run
/// from a directory that has none, still opens and says so — the alternative is a
/// window that fails to appear with nothing on screen explaining why.
/// </remarks>
public sealed class SessionLauncher
{
    /// <summary>
    /// Finds the kernel binary, or the path that was configured.
    /// </summary>
    private readonly Func<string?> _locate;

    /// <summary>
    /// Starts a kernel from a binary path.
    /// </summary>
    private readonly Func<string, Task<LaunchedKernel>> _start;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionLauncher"/> class that
    /// finds and starts the shipped kernel.
    /// </summary>
    public SessionLauncher()
        : this(HostLauncher.Locate, binary => HostLauncher.StartAsync(binary, Arguments()))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionLauncher"/> class over
    /// the two steps the caller supplies.
    /// </summary>
    /// <param name="locate">Finds the kernel binary, or returns null when there is none.</param>
    /// <param name="start">Starts a kernel from a binary path.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="locate"/> or <paramref name="start"/> is null.
    /// </exception>
    public SessionLauncher(Func<string?> locate, Func<string, Task<LaunchedKernel>> start)
    {
        _locate = locate ?? throw new ArgumentNullException(nameof(locate));
        _start = start ?? throw new ArgumentNullException(nameof(start));
    }

    /// <summary>
    /// Gets the arguments this shell starts the kernel with.
    /// </summary>
    /// <returns>
    /// No arguments: the kernel is asked for its socket and handshake, and its page
    /// is the one it finds beside itself. This shell draws its own conversation, so
    /// the page the kernel would serve is never loaded.
    /// </returns>
    public static IReadOnlyList<string> Arguments()
    {
        return Array.Empty<string>();
    }

    /// <summary>
    /// Starts the conversation, or answers why it could not.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>
    /// The engine, or the reason there is none. A caller shows the reason; it never
    /// has to distinguish a missing binary from a kernel that would not start.
    /// </returns>
    public async Task<SessionLaunch> LaunchAsync(CancellationToken cancellationToken = default)
    {
        var binary = _locate();
        if (binary is null)
        {
            return new SessionLaunch(null, string.Create(
                CultureInfo.InvariantCulture,
                $"找不到 {HostLauncher.BinaryName}：请把它放在本程序旁边，或先用 reasonix setup 配置一个模型。"));
        }

        try
        {
            var kernel = await _start(binary).ConfigureAwait(false);
            return new SessionLaunch(new SessionEngine(kernel), null);
        }
        catch (Exception error)
        {
            // A kernel that will not start is a fact the person has to read, not a
            // crash: the window is already open around this message.
            return new SessionLaunch(null, error.Message);
        }
    }
}
