using Reasonix.Studio.Baseline;
using Reasonix.Studio.Controls;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the skeleton's load-bearing sizes to the boxes the baseline recorded.
/// </summary>
/// <remarks>
/// The rails and the composer are drawn from constants rather than from a
/// measurement, so nothing in the shell would notice one of them drifting. This
/// reads the captured page and compares, which is the same check the geometry
/// gate runs over a rendered window — applied to the numbers the window is
/// built from, before there is a window to measure.
/// </remarks>
public sealed class SkeletonMetricsTests
{
    /// <summary>
    /// Checks that the rail's width is the one the baseline drew.
    /// </summary>
    [Fact]
    public void RailWidthMatchesTheBaseline()
    {
        Assert.Equal(SkeletonMetrics.RailWidth, Box("rail").Width);
    }

    /// <summary>
    /// Checks that the chrome bar's height is the one the baseline drew.
    /// </summary>
    [Fact]
    public void ChromeHeightMatchesTheBaseline()
    {
        Assert.Equal(SkeletonMetrics.ChromeHeight, Box("chrome").Height);
    }

    /// <summary>
    /// Checks that the composer's box is the one the baseline drew.
    /// </summary>
    [Fact]
    public void ComposerBoxMatchesTheBaseline()
    {
        var box = Box("compose");

        Assert.Equal(SkeletonMetrics.ComposerWidth, box.Width);
        Assert.Equal(SkeletonMetrics.ComposerHeight, box.Height);
    }

    /// <summary>
    /// Reads the first box a class opened with in the conversation baseline.
    /// </summary>
    /// <param name="className">The leading class the element carries.</param>
    /// <returns>The box the capture recorded for it.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no element opens with that class.</exception>
    private static BaselineBox Box(string className)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "baseline", "conversation.light.json");
        var domain = JsonSerializer.Deserialize<BaselineDomain>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} did not parse into a domain");

        var node = domain.Nodes.FirstOrDefault(n =>
            n.Classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == className)
            ?? throw new InvalidOperationException($"the baseline has no element opening with .{className}");

        return node.Box;
    }
}
