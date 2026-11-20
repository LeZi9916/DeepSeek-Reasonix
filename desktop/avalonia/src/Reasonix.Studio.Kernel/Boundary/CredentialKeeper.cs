using System;

namespace Reasonix.Studio.Kernel.Boundary;

/// <summary>
/// Decides whether the window's credential cookie needs putting back.
/// </summary>
/// <remarks>
/// The kernel's gate answers every request without the cookie with 403, and a
/// cookie belongs to the host rather than to the port. Three things remove it
/// without the shell being told: a page clearing cookies for 127.0.0.1, a jar
/// eviction, and a network-stack restart that drops session cookies silently. The
/// shell therefore checks twice — on every removal it is told about, and on the
/// first 403 it sees, which is the moment a silent loss becomes visible.
///
/// This is a decision rather than a side effect so the rule can be held by a test
/// without a browser: what is being decided is "does the jar still hold this
/// launch's value", and that answer does not need a window.
/// </remarks>
public sealed class CredentialKeeper
{
    /// <summary>
    /// The cookie this launch armed the window with.
    /// </summary>
    private readonly LaunchCookie _armed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialKeeper"/> class.
    /// </summary>
    /// <param name="armed">The cookie this launch armed the window with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="armed"/> is null.</exception>
    public CredentialKeeper(LaunchCookie armed)
    {
        _armed = armed ?? throw new ArgumentNullException(nameof(armed));
    }

    /// <summary>
    /// Reports whether a refusal is the moment a lost cookie becomes visible.
    /// </summary>
    /// <remarks>
    /// Only 403 says the gate refused the credential; a 401 belongs to a provider
    /// the person configured, and restoring a cookie in answer to it would hide the
    /// failure they need to see.
    /// </remarks>
    /// <param name="statusCode">The status the kernel answered with.</param>
    /// <returns>True when the request was refused by the gate rather than by the kernel's own policy.</returns>
    public static bool RefusedByGate(int statusCode)
    {
        return statusCode == 403;
    }

    /// <summary>
    /// Gets the cookie to restore, or null when the jar still holds this launch's value.
    /// </summary>
    /// <param name="held">
    /// The value the jar holds for the cookie, or null when it holds none. A jar
    /// with no cookie is the same question as one with the wrong cookie: both leave
    /// every request refused until it is put back.
    /// </param>
    /// <returns>The cookie to set again, or null when nothing needs restoring.</returns>
    public LaunchCookie? RestoreFor(string? held)
    {
        return _armed.Matches(held) ? null : _armed;
    }
}
