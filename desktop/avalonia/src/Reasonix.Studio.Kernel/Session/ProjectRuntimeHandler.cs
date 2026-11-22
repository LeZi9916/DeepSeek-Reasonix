using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Session;

/// <summary>Routes pane reads, writes and SSE through a validated runtime on the same authenticated hub.</summary>
internal sealed class ProjectRuntimeHandler : DelegatingHandler
{
    /// <summary>The runtime base returned by the hub.</summary>
    private readonly string _prefix;

    /// <summary>Initializes a new instance of the <see cref="ProjectRuntimeHandler"/> class.</summary>
    /// <param name="prefix">Validated local runtime path.</param>
    /// <param name="inner">The launch's normal credential and origin checks.</param>
    public ProjectRuntimeHandler(string prefix, HttpMessageHandler inner)
        : base(inner) => _prefix = prefix;

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var segment = uri.Segments.Length > 1 ? uri.Segments[1].TrimEnd('/') : string.Empty;
        if (segment is not ("tree" or "runtimes" or "rt" or "remotes" or "studio" or "host" or "tray"))
        {
            request.RequestUri = new Uri(uri.GetLeftPart(UriPartial.Authority) + _prefix + uri.PathAndQuery);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
