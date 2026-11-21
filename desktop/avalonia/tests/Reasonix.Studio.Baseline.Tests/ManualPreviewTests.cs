using Reasonix.Studio.Shell.Browser;
using System;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Checks the URL boundary of the manual guest independently of any platform browser.</summary>
public sealed class ManualPreviewTests
{
    /// <summary>The guest can preview web servers but never the authenticated kernel origin or OS protocols.</summary>
    /// <param name="address">User input or a redirected destination.</param>
    /// <param name="allowed">Whether the preview may navigate.</param>
    [Theory]
    [InlineData("https://example.org/path", true)]
    [InlineData("http://127.0.0.1:3000/", true)]
    [InlineData("http://127.0.0.1:42819/status", false)]
    [InlineData("http://localhost:42819/status", false)]
    [InlineData("file:///etc/hosts", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("reasonix://session/1", false)]
    [InlineData("about:blank", false)]
    public void NavigationHonorsTheGuestBoundary(string address, bool allowed)
    {
        Assert.Equal(allowed, PreviewAddress.TryRead(address, new Uri("http://127.0.0.1:42819"), out _));
    }
}
