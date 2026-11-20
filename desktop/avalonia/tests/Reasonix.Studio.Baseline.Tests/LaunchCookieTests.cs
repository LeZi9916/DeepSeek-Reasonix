using Reasonix.Studio.Kernel.Boundary;
using System;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the credential cookie's attributes and the two moments the shell puts it
/// back.
/// </summary>
/// <remarks>
/// The attributes are the boundary rather than preferences: a cookie the page can
/// read is a credential the page can leak, one sent cross-site is a credential
/// another origin can spend, and one that outlives the launch is a credential still
/// valid when nothing is listening for it.
/// </remarks>
public sealed class LaunchCookieTests
{
    /// <summary>
    /// The credential the kernel produces: 32 random bytes, hex-encoded.
    /// </summary>
    private const string Token = "9f2c1c0e5b7a4d3e8f6a1b2c3d4e5f60718293a4b5c6d7e8f9012345678abcde";

    /// <summary>
    /// Checks that the header the shell arms the window with carries every attribute
    /// the boundary needs, and no expiry.
    /// </summary>
    [Fact]
    public void TheCookieIsHttpOnlyStrictAndHostScoped()
    {
        var header = Cookie().ToSetCookieHeader();

        Assert.Contains("reasonix_token=", header, StringComparison.Ordinal);
        Assert.Contains("Path=/", header, StringComparison.Ordinal);
        Assert.Contains("HttpOnly", header, StringComparison.Ordinal);
        Assert.Contains("SameSite=Strict", header, StringComparison.Ordinal);

        // No Max-Age and no Expires: a cookie with either outlives the launch whose
        // gate is the only thing that ever accepted it.
        Assert.DoesNotContain("Max-Age", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Expires", header, StringComparison.Ordinal);

        // No Domain: a cookie with one is sent to that domain, which is the opposite
        // of host-only.
        Assert.DoesNotContain("Domain=", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks that the credential never reaches the page.
    /// </summary>
    [Fact]
    public void ThePageCannotReadTheCookie()
    {
        Assert.True(Cookie().HttpOnly);
    }

    /// <summary>
    /// Checks that the comparison accepts only this launch's value.
    /// </summary>
    /// <param name="presented">What a request carried.</param>
    /// <param name="matches">Whether it should be accepted.</param>
    [Theory]
    [InlineData(Token, true)]
    [InlineData("", false)]
    [InlineData("9f2c1c0e5b7a4d3e8f6a1b2c3d4e5f60718293a4b5c6d7e8f9012345678abcdX", false)]
    [InlineData(null, false)]
    public void OnlyThisLaunchesCredentialMatches(string? presented, bool matches)
    {
        Assert.Equal(matches, Cookie().Matches(presented));
    }

    /// <summary>
    /// Checks that a jar which lost the cookie gets it back, and one that still has
    /// it is left alone.
    /// </summary>
    /// <param name="held">What the jar holds.</param>
    /// <param name="restores">Whether the shell should set the cookie again.</param>
    [Theory]
    [InlineData(Token, false)]
    [InlineData(null, true)]
    [InlineData("someone-elses-value", true)]
    public void TheCookieIsRestoredOnlyWhenTheJarLostIt(string? held, bool restores)
    {
        var keeper = new CredentialKeeper(Cookie());

        Assert.Equal(restores, keeper.RestoreFor(held) is not null);
    }

    /// <summary>
    /// Checks that a 403 is the moment a silent loss becomes visible and a 401 is
    /// not: the second belongs to a provider the person configured, and restoring a
    /// cookie in answer to it would hide the failure they need to see.
    /// </summary>
    /// <param name="status">The status the kernel answered with.</param>
    /// <param name="refused">Whether the gate refused it.</param>
    [Theory]
    [InlineData(403, true)]
    [InlineData(401, false)]
    [InlineData(404, false)]
    [InlineData(500, false)]
    public void OnlyForbiddenIsTheGateRefusingTheCredential(int status, bool refused)
    {
        Assert.Equal(refused, CredentialKeeper.RefusedByGate(status));
    }

    /// <summary>
    /// Builds the cookie every case arms a window with.
    /// </summary>
    /// <returns>The cookie for the launch the cases describe.</returns>
    private static LaunchCookie Cookie()
    {
        return new LaunchCookie(new Uri("http://127.0.0.1:7421/"), Token);
    }
}
