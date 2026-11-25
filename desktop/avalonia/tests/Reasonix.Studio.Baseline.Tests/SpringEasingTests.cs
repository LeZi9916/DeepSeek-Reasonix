using Reasonix.Studio.Controls;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Holds the captured spring easing to the reference stops.</summary>
public sealed class SpringEasingTests
{
    /// <summary>Checks the settle, overshoot and endpoints of the captured curve.</summary>
    [Fact]
    public void SpringEasingMatchesTheReferenceStops()
    {
        var easing = new SpringEasing();
        Assert.Equal(0d, easing.Ease(0));
        Assert.Equal(0.539, easing.Ease(0.189), 3);
        Assert.Equal(1.017, easing.Ease(0.639), 3);
        Assert.Equal(1d, easing.Ease(1));
    }
}
