using Reasonix.Studio.Kernel.Boundary;
using System;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the shell's half of the loopback boundary: what it accepts from a
/// kernel's handshake, and what it refuses.
/// </summary>
/// <remarks>
/// The kernel mints the credential and owns the gate; this side only receives the
/// handshake and holds the cookie. Every case here is a refusal that must happen
/// before anything loads, because a window opened on a handshake this side should
/// have rejected is one the person cannot tell from a working one.
/// </remarks>
public sealed class LaunchHandshakeTests
{
    /// <summary>
    /// The credential the kernel produces: 32 random bytes, hex-encoded.
    /// </summary>
    private const string ValidToken = "9f2c1c0e5b7a4d3e8f6a1b2c3d4e5f60718293a4b5c6d7e8f9012345678abcde";

    /// <summary>
    /// Checks that the handshake the kernel writes is accepted and read whole.
    /// </summary>
    [Fact]
    public void AWellFormedHandshakeIsAccepted()
    {
        var line = Handshake("http://127.0.0.1:7421", ValidToken);

        Assert.True(LaunchHandshake.TryParse(line, out var handshake, out var refusal), refusal);
        Assert.Null(refusal);
        Assert.NotNull(handshake);
        Assert.Equal("http://127.0.0.1:7421/", handshake.Origin.ToString());
        Assert.Equal(ValidToken, handshake.Token);
    }

    /// <summary>
    /// Checks that a line that is not JSON is refused rather than read as empty.
    /// </summary>
    [Fact]
    public void ANonJsonLineIsRefused()
    {
        Assert.False(LaunchHandshake.TryParse("reasonix-studio-host ready", out var handshake, out var refusal));

        Assert.Null(handshake);
        Assert.Equal("the handshake was not JSON", refusal);
    }

    /// <summary>
    /// Checks that an unknown protocol version is refused, because a newer kernel
    /// may have changed what the fields mean.
    /// </summary>
    /// <param name="version">The version the line claims.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AnUnknownVersionIsRefused(int version)
    {
        var line = Handshake("http://127.0.0.1:7421", ValidToken, version);

        Assert.False(LaunchHandshake.TryParse(line, out _, out var refusal));
        Assert.Contains("unsupported handshake version", refusal);
    }

    /// <summary>
    /// Checks that a credential shorter than what the kernel mints is refused: a
    /// short one is a truncated read, not a token.
    /// </summary>
    /// <param name="token">The credential the line carries.</param>
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0123456789abcdef0123456789abcde")]
    public void ATruncatedCredentialIsRefused(string token)
    {
        var line = Handshake("http://127.0.0.1:7421", token);

        Assert.False(LaunchHandshake.TryParse(line, out _, out var refusal));
        Assert.Equal("the handshake carried no usable credential", refusal);
    }

    /// <summary>
    /// Checks that an origin another machine could answer for is refused. A name
    /// would leave a resolver deciding what this shell loads; a wildcard address
    /// would let a request arrive looking local.
    /// </summary>
    /// <param name="origin">The origin the line names.</param>
    [Theory]
    [InlineData("http://example.com:7421")]
    [InlineData("http://0.0.0.0:7421")]
    [InlineData("http://localhost:7421")]
    [InlineData("http://192.168.1.4:7421")]
    public void ANonLoopbackOriginIsRefused(string origin)
    {
        var line = Handshake(origin, ValidToken);

        Assert.False(LaunchHandshake.TryParse(line, out _, out var refusal));
        Assert.Contains("which is not this machine", refusal);
    }

    /// <summary>
    /// Checks that a scheme this shell does not load is refused rather than
    /// followed: a handshake naming https would load a page no listener answers for.
    /// </summary>
    [Fact]
    public void ANonHttpOriginIsRefused()
    {
        var line = Handshake("https://127.0.0.1:7421", ValidToken);

        Assert.False(LaunchHandshake.TryParse(line, out _, out var refusal));
        Assert.Contains("which this shell does not load", refusal);
    }

    /// <summary>
    /// Checks that an origin without a port is refused, because the kernel always
    /// listens on an ephemeral one and its absence means the line is not one.
    /// </summary>
    [Fact]
    public void AnOriginWithoutAPortIsRefused()
    {
        var line = Handshake("http://127.0.0.1", ValidToken);

        Assert.False(LaunchHandshake.TryParse(line, out _, out var refusal));
        Assert.Equal("the handshake named no port", refusal);
    }

    /// <summary>
    /// Checks that an origin the line does not carry is refused rather than guessed.
    /// </summary>
    [Fact]
    public void AMissingOriginIsRefused()
    {
        var line = "{\"version\":1,\"token\":\"" + ValidToken + "\"}";

        Assert.False(LaunchHandshake.TryParse(line, out _, out var refusal));
        Assert.Equal("the handshake carried no usable origin", refusal);
    }

    /// <summary>
    /// Builds one handshake line for a case.
    /// </summary>
    /// <param name="origin">The origin to name.</param>
    /// <param name="token">The credential to carry.</param>
    /// <param name="version">The protocol version to claim.</param>
    /// <returns>The line a kernel would write.</returns>
    private static string Handshake(string origin, string token, int version = LaunchHandshake.ProtocolVersion)
    {
        return $"{{\"version\":{version},\"origin\":\"{origin}\",\"token\":\"{token}\"}}";
    }
}
