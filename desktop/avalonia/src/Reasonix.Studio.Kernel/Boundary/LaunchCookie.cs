using System;
using System.Globalization;

namespace Reasonix.Studio.Kernel.Boundary;

/// <summary>
/// The credential cookie one launch is held by, and how it is spelled.
/// </summary>
/// <remarks>
/// The kernel's gate answers every request without this cookie with 403, and a
/// cookie belongs to the host rather than to the port. Anything that removes it —
/// an eviction, a page clearing cookies for 127.0.0.1, a network stack restart —
/// leaves the window refused until a relaunch, so the shell holds the value and
/// puts it back.
///
/// The attributes are the boundary, not preferences. HttpOnly because nothing in
/// the page ever reads it; SameSite=Strict because no cross-site navigation ever
/// needs to carry it; no expiry so it dies with the launch rather than outliving
/// it; host-only because a cookie with a domain would be sent to that domain.
/// </remarks>
public sealed record LaunchCookie
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchCookie"/> class.
    /// </summary>
    /// <param name="origin">The origin the cookie belongs to.</param>
    /// <param name="token">The launch's credential.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="origin"/> is null.</exception>
    public LaunchCookie(Uri origin, string token)
    {
        Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        Token = token ?? string.Empty;
    }

    /// <summary>
    /// Gets the origin the cookie belongs to.
    /// </summary>
    public Uri Origin
    {
        get;
    }

    /// <summary>
    /// Gets the launch's credential.
    /// </summary>
    public string Token
    {
        get;
    }

    /// <summary>
    /// Gets the name the cookie is set under.
    /// </summary>
    public string Name => LaunchHandshake.TokenCookie;

    /// <summary>
    /// Gets the path the cookie is scoped to.
    /// </summary>
    public string Path => "/";

    /// <summary>
    /// Gets a value indicating whether the page may read this cookie.
    /// </summary>
    /// <remarks>Always false: the credential never reaches the page.</remarks>
    public bool HttpOnly => true;

    /// <summary>
    /// Builds the header the shell sets before anything loads.
    /// </summary>
    /// <remarks>
    /// Set before the first request, or the window opens on a 403. The function is
    /// separate from the cookie so a test can read the attributes it produces
    /// without standing up a window.
    /// </remarks>
    /// <returns>The <c>Set-Cookie</c> value this launch is armed with.</returns>
    public string ToSetCookieHeader()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Name}={Token}; Path={Path}; HttpOnly; SameSite=Strict");
    }

    /// <summary>
    /// Reports whether a presented value is this launch's credential.
    /// </summary>
    /// <param name="presented">The value a request carried, or null when it carried none.</param>
    /// <returns>True when the values match in constant time.</returns>
    public bool Matches(string? presented)
    {
        if (presented is null || presented.Length != Token.Length)
        {
            return false;
        }

        // Constant time: a comparison that returns early on the first wrong byte
        // tells an attacker how much of the credential it guessed.
        var difference = 0;
        for (var i = 0; i < Token.Length; i++)
        {
            difference |= Token[i] ^ presented[i];
        }

        return difference == 0;
    }
}
