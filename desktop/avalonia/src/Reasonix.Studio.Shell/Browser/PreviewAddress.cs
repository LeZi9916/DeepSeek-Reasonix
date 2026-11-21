using System;
using System.Diagnostics.CodeAnalysis;

namespace Reasonix.Studio.Shell.Browser;

/// <summary>The navigation boundary shared by the address bar, links and redirects.</summary>
internal static class PreviewAddress
{
    /// <summary>Accepts web URLs outside this launch's private kernel authority.</summary>
    /// <param name="text">Address supplied by the user or the browser.</param>
    /// <param name="kernel">Authenticated origin of the current session.</param>
    /// <param name="address">The accepted URL.</param>
    /// <returns>Whether navigation may proceed.</returns>
    internal static bool TryRead(string? text, Uri? kernel, [NotNullWhen(true)] out Uri? address)
    {
        address = null;
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var target)
            || target.Scheme is not ("http" or "https") || target.Host.Length == 0)
        {
            return false;
        }

        if (kernel is not null && target.Port == kernel.Port
            && (string.Equals(target.IdnHost, kernel.IdnHost, StringComparison.OrdinalIgnoreCase) || (target.IsLoopback && kernel.IsLoopback)))
        {
            return false;
        }

        address = target;
        return true;
    }
}
