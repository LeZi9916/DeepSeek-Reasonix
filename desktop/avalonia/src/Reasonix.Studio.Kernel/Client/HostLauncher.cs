using Reasonix.Studio.Kernel.Boundary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// Starts the kernel process and reads the one line it announces itself with.
/// </summary>
/// <remarks>
/// The kernel is a child process, not a library: it owns the socket, the gate and
/// the state, and this shell reaches it the way a browser does. The handshake goes
/// down the child's stdout rather than through a file or an environment variable,
/// because either of those outlives the launch and is readable by more than the
/// process that spawned it.
/// </remarks>
public static class HostLauncher
{
    /// <summary>
    /// The name the kernel binary is shipped under.
    /// </summary>
    public const string BinaryName = "reasonix-studio-host";

    /// <summary>
    /// How long a kernel may take to announce itself before the launch is called off.
    /// </summary>
    /// <remarks>
    /// A child that never writes a handshake must not be able to grow this process
    /// without bound, and a person watching a splash has to be told something.
    /// </remarks>
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Finds the kernel binary beside this application, or on the path.
    /// </summary>
    /// <returns>The binary's path, or null when this installation has none.</returns>
    public static string? Locate()
    {
        var directory = AppContext.BaseDirectory;
        var names = OperatingSystem.IsWindows()
            ? new[] { BinaryName + ".exe", BinaryName }
            : new[] { BinaryName };

        foreach (var name in names)
        {
            var beside = Path.Combine(directory, name);
            if (File.Exists(beside))
            {
                return beside;
            }
        }

        return null;
    }

    /// <summary>
    /// Starts the kernel and reads the handshake it announces itself with.
    /// </summary>
    /// <param name="binary">The kernel binary's path.</param>
    /// <param name="arguments">The arguments to start it with.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <param name="profileDirectory">Optional isolated configuration and state directory for this launch.</param>
    /// <returns>The running kernel and the handshake it announced.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="binary"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the kernel exited or announced itself with a line this shell
    /// will not load.
    /// </exception>
    /// <exception cref="TimeoutException">Thrown when no handshake arrived in time.</exception>
    public static async Task<LaunchedKernel> StartAsync(
        string binary,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default,
        string? profileDirectory = null)
    {
        if (binary is null)
        {
            throw new ArgumentNullException(nameof(binary));
        }

        var start = new ProcessStartInfo(binary)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (profileDirectory is not null)
        {
            start.Environment["REASONIX_HOME"] = profileDirectory;
            start.Environment["REASONIX_STATE_HOME"] = profileDirectory;
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"the kernel at {binary} did not start");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HandshakeTimeout);

        string? line;
        try
        {
            line = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new TimeoutException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the kernel was still running but sent no handshake within {HandshakeTimeout.TotalSeconds:0} s"));
        }

        if (line is null)
        {
            // Nothing on stdout means the process is gone or said nothing: either
            // way there is no origin to load and no credential to carry.
            var exitCode = process.HasExited ? process.ExitCode : (int?)null;
            Kill(process);
            throw new InvalidOperationException(
                exitCode is null
                    ? "the kernel sent no handshake"
                    : string.Create(CultureInfo.InvariantCulture, $"the kernel exited before its handshake (code {exitCode})"));
        }

        if (!LaunchHandshake.TryParse(line, out var handshake, out var refusal))
        {
            Kill(process);
            throw new InvalidOperationException(refusal);
        }

        return new LaunchedKernel(process, handshake!);
    }

    /// <summary>
    /// Stops a kernel that will not be used.
    /// </summary>
    /// <param name="process">The process to stop.</param>
    public static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // A process that already went away is the outcome asked for.
        }
        finally
        {
            process.Dispose();
        }
    }
}
