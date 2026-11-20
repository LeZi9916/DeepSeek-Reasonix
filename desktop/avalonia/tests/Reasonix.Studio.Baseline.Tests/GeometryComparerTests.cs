using Reasonix.Studio.Baseline;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the comparison against the baseline that was captured from the
/// reference frontend, so a passing run means the gate reads that data rather
/// than a fixture written to agree with this code.
/// </summary>
public sealed class GeometryComparerTests
{
    /// <summary>
    /// Checks that a faithful mirror of the baseline reports nothing.
    /// </summary>
    [Fact]
    public void AFaithfulMirrorReportsNothing()
    {
        var domain = Domain();
        var comparer = new GeometryComparer(GeometryComparerOptions.Studio);

        var findings = comparer.Compare(domain, Mirror(domain));

        Assert.Empty(findings);
    }

    /// <summary>
    /// Checks that a box moved off its captured position is reported.
    /// </summary>
    [Fact]
    public void AMovedBoxIsReported()
    {
        var domain = Domain();
        var observed = Mirror(domain);
        var target = GeometryComparer.NodeKey(domain.Nodes[0]);
        observed[target].X += 12;

        var findings = new GeometryComparer(GeometryComparerOptions.Studio).Compare(domain, observed);

        Assert.Contains(findings, f => f.Kind == "box" && f.Identity == target);
    }

    /// <summary>
    /// Checks that a box differing by less than the tolerance is not reported,
    /// so a rounding step at the last layout pass does not read as drift.
    /// </summary>
    [Fact]
    public void ASubPixelBoxDifferenceIsNotReported()
    {
        var domain = Domain();
        var observed = Mirror(domain);
        observed[GeometryComparer.NodeKey(domain.Nodes[0])].X += 0.25;

        var findings = new GeometryComparer(GeometryComparerOptions.Studio).Compare(domain, observed);

        Assert.Empty(findings);
    }

    /// <summary>
    /// Checks that the same ink written in another form is not reported, because
    /// the comparison is over the colour a person sees, not over its spelling.
    /// </summary>
    [Fact]
    public void AnEquivalentColourSpellingIsNotReported()
    {
        var domain = Domain();
        var observed = Mirror(domain);
        var node = domain.Nodes.First(n => domain.Styles[n.Style].ContainsKey("color"));
        var style = observed[GeometryComparer.NodeKey(node)].Styles;

        // #292b29 and rgb(41, 43, 41) name one colour.
        style["color"] = "#292b29";

        var findings = new GeometryComparer(GeometryComparerOptions.Studio).Compare(domain, observed);

        Assert.DoesNotContain(findings, f => f.Kind == "style");
    }

    /// <summary>
    /// Checks that a node the shell did not draw is reported, because a missing
    /// element is the difference a box comparison can never see.
    /// </summary>
    [Fact]
    public void AMissingNodeIsReported()
    {
        var domain = Domain();
        var observed = Mirror(domain);
        var target = GeometryComparer.NodeKey(domain.Nodes[0]);
        observed.Remove(target);

        var findings = new GeometryComparer(GeometryComparerOptions.Studio).Compare(domain, observed);

        Assert.Contains(findings, f => f.Kind == "missing" && f.Identity == target);
    }

    /// <summary>
    /// Checks that an element the baseline never recorded is reported, so a
    /// shell cannot pass by drawing more than the reference did.
    /// </summary>
    [Fact]
    public void AnExtraNodeIsReported()
    {
        var domain = Domain();
        var observed = Mirror(domain);
        observed["action:not.in.the.baseline#0"] = new ObservedNode();

        var findings = new GeometryComparer(GeometryComparerOptions.Studio).Compare(domain, observed);

        Assert.Contains(findings, f => f.Kind == "extra");
    }

    /// <summary>
    /// Checks that a token whose value moved is reported, because the tokens are
    /// the vocabulary every rule is written in.
    /// </summary>
    [Fact]
    public void AChangedTokenIsReported()
    {
        var domain = Domain();
        var observed = Mirror(domain);
        var name = domain.Tokens.Keys.First(k => domain.Tokens[k].EndsWith("px", StringComparison.Ordinal));
        observed[GeometryComparer.TokenKey(name)].Text = "1px";

        var findings = new GeometryComparer(GeometryComparerOptions.Studio).Compare(domain, observed);

        Assert.Contains(findings, f => f.Kind == "token" && f.Identity == name);
    }

    /// <summary>
    /// Checks that every baseline domain parses and that its node keys are
    /// unique, because a duplicate key would silently drop an element from the
    /// comparison rather than fail it.
    /// </summary>
    [Fact]
    public void EveryBaselineDomainHasUniqueNodeKeys()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "baseline");

        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            if (Path.GetFileName(file) == "index.json")
            {
                continue;
            }

            var domain = Read(file);
            var keys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in domain.Nodes)
            {
                var key = GeometryComparer.NodeKey(node);
                Assert.True(keys.Add(key), $"{file} repeats {key}");
            }
        }
    }

    /// <summary>
    /// Reads the domain every comparison in this class runs against.
    /// </summary>
    /// <returns>The conversation page in the light scheme.</returns>
    private static BaselineDomain Domain()
    {
        return Read(Path.Combine(AppContext.BaseDirectory, "baseline", "conversation.light.json"));
    }

    /// <summary>
    /// Reads one captured domain from disk.
    /// </summary>
    /// <param name="path">The file holding the domain.</param>
    /// <returns>The parsed domain.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file does not parse into a domain.</exception>
    private static BaselineDomain Read(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BaselineDomain>(json)
            ?? throw new InvalidOperationException($"{path} did not parse into a domain");
    }

    /// <summary>
    /// Builds an observation that matches the baseline exactly, which is what a
    /// faithful replacement shell would produce.
    /// </summary>
    /// <param name="domain">The domain to mirror.</param>
    /// <returns>One observation per baseline node and token.</returns>
    private static Dictionary<string, ObservedNode> Mirror(BaselineDomain domain)
    {
        var observed = new Dictionary<string, ObservedNode>(StringComparer.Ordinal);

        foreach (var node in domain.Nodes)
        {
            var mirror = new ObservedNode
            {
                X = node.Box.X,
                Y = node.Box.Y,
                Width = node.Box.Width,
                Height = node.Box.Height,
            };

            foreach (var pair in domain.Styles[node.Style])
            {
                mirror.Styles[pair.Key] = pair.Value;
            }

            observed[GeometryComparer.NodeKey(node)] = mirror;
        }

        foreach (var pair in domain.Tokens)
        {
            observed[GeometryComparer.TokenKey(pair.Key)] = new ObservedNode { Text = pair.Value };
        }

        return observed;
    }
}
