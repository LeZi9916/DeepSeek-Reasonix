using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Boundary;

/// <summary>
/// Puts the launch credential on every request this shell sends, and on nothing else.
/// </summary>
/// <remarks>
/// The kernel's gate accepts a request only when it is addressed to this launch's
/// listener, carries this launch's cookie, and — for anything that changes state —
/// names this launch as its origin. The shell therefore has three obligations, and
/// this handler is the one place that keeps all three:
///
/// <list type="bullet">
/// <item>The credential goes out with every request, because the gate refuses a
/// request without it rather than treating it as anonymous.</item>
/// <item>The exact origin goes out on every write, because a read may arrive
/// without one while a write may not.</item>
/// <item>The credential never goes anywhere else. A request to another origin is
/// refused here rather than sent unauthenticated: the second produces a 403 that
/// reads like the kernel's fault, and the first is the leak this handler exists to
/// prevent.</item>
/// </list>
/// </remarks>
public sealed class CredentialHandler : DelegatingHandler
{
    /// <summary>
    /// The cookie this launch armed the window with.
    /// </summary>
    private readonly LaunchCookie _cookie;

    /// <summary>
    /// The origin every request must be addressed to.
    /// </summary>
    private readonly Uri _origin;

    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialHandler"/> class.
    /// </summary>
    /// <param name="cookie">The cookie this launch armed the window with.</param>
    /// <param name="inner">The handler that sends the request, or null to send it directly.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cookie"/> is null.</exception>
    public CredentialHandler(LaunchCookie cookie, HttpMessageHandler? inner = null)
        : base(inner ?? new HttpClientHandler())
    {
        _cookie = cookie ?? throw new ArgumentNullException(nameof(cookie));
        _origin = cookie.Origin;
    }

    /// <summary>
    /// Gets the origin every request must be addressed to.
    /// </summary>
    public Uri Origin
    {
        get => _origin;
    }

    /// <summary>
    /// Reports whether two addresses name one listener.
    /// </summary>
    /// <remarks>
    /// Scheme, host and port only: two paths on one listener are one origin, and a
    /// different port is a different listener even on the same address.
    /// </remarks>
    /// <param name="candidate">The address a request names.</param>
    /// <param name="origin">The origin this launch answers for.</param>
    /// <returns>True when both name the same scheme, host and port.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    public static bool SameOrigin(Uri candidate, Uri origin)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        if (origin is null)
        {
            throw new ArgumentNullException(nameof(origin));
        }

        return string.Equals(candidate.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
            && candidate.Port == origin.Port;
    }

    /// <summary>
    /// Reports whether a method changes state, and so must name its origin.
    /// </summary>
    /// <param name="method">The method a request carries.</param>
    /// <returns>True for anything except GET and HEAD.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="method"/> is null.</exception>
    public static bool ChangesState(HttpMethod method)
    {
        if (method is null)
        {
            throw new ArgumentNullException(nameof(method));
        }

        return method != HttpMethod.Get && method != HttpMethod.Head;
    }

    /// <summary>
    /// Arms one request with the credential, and refuses to send it anywhere else.
    /// </summary>
    /// <param name="request">The request on its way out.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response the inner handler produced.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the request names no address.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the request is addressed to another origin, because sending the
    /// credential there would hand it to whatever listens on that port.
    /// </exception>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.RequestUri is null)
        {
            throw new ArgumentException("a request on this launch's client must name an address", nameof(request));
        }

        if (!SameOrigin(request.RequestUri, _origin))
        {
            throw new InvalidOperationException(
                $"this launch's credential belongs to {_origin.GetLeftPart(UriPartial.Authority)}; refusing to send it to {request.RequestUri.GetLeftPart(UriPartial.Authority)}");
        }

        request.Headers.TryAddWithoutValidation("Cookie", _cookie.Name + "=" + _cookie.Token);

        if (ChangesState(request.Method))
        {
            // The exact origin, not a derived one: the gate compares the header
            // against the listener it guards, and a trailing slash or a default port
            // spelled out would be a different string.
            request.Headers.TryAddWithoutValidation("Origin", _origin.GetLeftPart(UriPartial.Authority));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
