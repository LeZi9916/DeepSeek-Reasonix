using System;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Reasonix.Studio.Kernel.Boundary;

/// <summary>
/// The origin and credential one launch is guarded by, as the kernel announced them.
/// </summary>
/// <remarks>
/// The credential is minted by the kernel from <c>crypto/rand</c>, never read from
/// configuration and never persisted. This side only receives it: a shell that
/// chose its own token could be made to choose one a config file knows.
/// </remarks>
/// <param name="Origin">The origin this launch answers for, as an IP literal.</param>
/// <param name="Token">The launch's credential.</param>
public sealed record LaunchHandshake(Uri Origin, string Token)
{
    /// <summary>
    /// The handshake shape this shell understands.
    /// </summary>
    public const int ProtocolVersion = 1;

    /// <summary>
    /// The cookie the kernel's gate reads the credential from.
    /// </summary>
    public const string TokenCookie = "reasonix_token";

    /// <summary>
    /// The shortest credential a handshake may carry: 32 random bytes, hex-encoded
    /// by the kernel, so a shorter one is a truncated read rather than a token.
    /// </summary>
    private const int MinimumTokenLength = 32;

    /// <summary>
    /// Reads the one handshake line a kernel writes before anything is served.
    /// </summary>
    /// <param name="line">The line, as it arrived on the kernel's stdout.</param>
    /// <param name="handshake">The parsed handshake, when the line was one.</param>
    /// <param name="refusal">Why the line was refused, when it was.</param>
    /// <returns>True when the line was a handshake this shell may load.</returns>
    public static bool TryParse(string line, out LaunchHandshake? handshake, out string? refusal)
    {
        handshake = null;
        refusal = null;

        JsonElement body;
        try
        {
            using var document = JsonDocument.Parse(line);
            body = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            refusal = "the handshake was not JSON";
            return false;
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            refusal = "the handshake was not a JSON object";
            return false;
        }

        if (!body.TryGetProperty("version", out var version) || !version.TryGetInt32(out var parsedVersion)
            || parsedVersion != ProtocolVersion)
        {
            refusal = string.Create(CultureInfo.InvariantCulture, $"unsupported handshake version {version.GetRawText()}");
            return false;
        }

        if (!body.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.String)
        {
            refusal = "the handshake carried no usable credential";
            return false;
        }

        var credential = token.GetString() ?? string.Empty;
        if (credential.Length < MinimumTokenLength)
        {
            refusal = "the handshake carried no usable credential";
            return false;
        }

        if (!body.TryGetProperty("origin", out var originElement) || originElement.ValueKind != JsonValueKind.String
            || !TryReadOrigin(originElement.GetString(), out var origin, out refusal))
        {
            refusal ??= "the handshake carried no usable origin";
            return false;
        }

        handshake = new LaunchHandshake(origin!, credential);
        return true;
    }

    /// <summary>
    /// Accepts an origin only when it is this machine's loopback on a real port.
    /// </summary>
    /// <remarks>
    /// A name would leave a resolver deciding what this shell loads, and a wildcard
    /// address would let another machine's request arrive looking local. Both are
    /// refused here rather than trusted and checked later.
    /// </remarks>
    /// <param name="raw">The origin text the handshake carried.</param>
    /// <param name="origin">The parsed origin, when it was acceptable.</param>
    /// <param name="refusal">Why it was not, when it was not.</param>
    /// <returns>True when the origin is a loopback IP literal with a port.</returns>
    public static bool TryReadOrigin(string? raw, out Uri? origin, out string? refusal)
    {
        origin = null;
        refusal = null;

        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
        {
            refusal = "the handshake carried no usable origin";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp)
        {
            refusal = string.Create(CultureInfo.InvariantCulture, $"the handshake named {parsed.Scheme}, which this shell does not load");
            return false;
        }

        // Uri.IsLoopback is deliberately not used here: it answers true for
        // "localhost", which is a name a resolver gets to answer for, and it fills
        // in port 80 for an origin that named none. The kernel's own gate reads the
        // authority as netip.ParseAddrPort does — an IP literal with an explicit
        // port — so this reads it the same way rather than more loosely.
        var rejected = AuthorityRefusal(parsed.Authority);
        if (rejected is not null)
        {
            refusal = rejected;
            return false;
        }

        origin = parsed;
        return true;
    }

    /// <summary>
    /// Reads an authority as the kernel's gate does: an IP literal and a port.
    /// </summary>
    /// <param name="authority">The authority text, such as <c>127.0.0.1:7421</c>.</param>
    /// <returns>Null when the authority is acceptable, otherwise why it is not.</returns>
    private static string? AuthorityRefusal(string authority)
    {
        if (string.IsNullOrEmpty(authority))
        {
            return "the handshake carried no usable origin";
        }

        string host;
        string portText;

        if (authority.StartsWith('['))
        {
            // An IPv6 literal is bracketed, and its colons are not separators.
            var close = authority.IndexOf(']');
            if (close < 0)
            {
                return "the handshake carried no usable origin";
            }

            host = authority[..(close + 1)];
            var rest = authority[(close + 1)..];
            if (!rest.StartsWith(':'))
            {
                return "the handshake named no port";
            }

            portText = rest[1..];
        }
        else
        {
            var colon = authority.LastIndexOf(':');
            if (colon < 0)
            {
                return "the handshake named no port";
            }

            host = authority[..colon];
            portText = authority[(colon + 1)..];
        }

        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port < 1 || port > 65535)
        {
            return "the handshake named no port";
        }

        var literal = host.TrimStart('[').TrimEnd(']');
        if (!IPAddress.TryParse(literal, out var address))
        {
            return string.Create(CultureInfo.InvariantCulture, $"the handshake named {host}, which is not this machine");
        }

        return IPAddress.IsLoopback(address)
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"the handshake named {host}, which is not this machine");
    }
}
