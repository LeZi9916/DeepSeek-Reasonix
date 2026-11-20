using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Reasonix.Studio.Baseline;

/// <summary>
/// Compares one captured domain against the boxes and inks a replacement shell
/// computes, and reports what a person would have to look at.
/// </summary>
/// <remarks>
/// A gate here answers one question: does the replacement draw the same box, in
/// the same ink, at the same type scale, with the same spacing? It does not
/// compare glyph rasterisation, native dialog chrome or scroll inertia, because
/// those differ per engine and per platform, and a gate that failed on them
/// would be switched off within a week.
/// </remarks>
public sealed class GeometryComparer
{
    /// <summary>
    /// Matches the <c>rgb()</c> and <c>rgba()</c> colour forms a browser reports.
    /// </summary>
    private static readonly Regex s_rgbPattern =
        new Regex(@"^rgba?\(\s*([0-9.]+)[,\s]+([0-9.]+)[,\s]+([0-9.]+)(?:[,/\s]+([0-9.%]+))?\s*\)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Matches a three, six or eight digit hexadecimal colour.
    /// </summary>
    private static readonly Regex s_hexPattern =
        new Regex(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Matches a value written in pixels, which is the only length unit compared.
    /// </summary>
    private static readonly Regex s_pxPattern =
        new Regex(@"^(-?[0-9.]+)px$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Holds the tolerances and the property set this comparison uses.
    /// </summary>
    private readonly GeometryComparerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeometryComparer"/> class.
    /// </summary>
    /// <param name="options">The tolerances and the property set to compare.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public GeometryComparer(GeometryComparerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Builds the identity a replacement shell must carry for a baseline node.
    /// </summary>
    /// <param name="node">The captured node.</param>
    /// <returns>
    /// The action when the node names one, otherwise its role, otherwise its
    /// class list: a shell reproduces what a person can address.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> is null.</exception>
    public static string IdentityOf(BaselineNode node)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        if (!string.IsNullOrEmpty(node.Action))
        {
            return "action:" + node.Action;
        }

        if (!string.IsNullOrEmpty(node.Role))
        {
            return "role:" + node.Role;
        }

        return "class:" + node.Classes;
    }

    /// <summary>
    /// Builds the key a node observation is addressed by.
    /// </summary>
    /// <param name="node">The captured node.</param>
    /// <returns>
    /// The node's identity joined with its occurrence among the nodes sharing
    /// that identity. An identity alone addresses many elements — twenty labels
    /// share one class — so the pair is what names exactly one.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> is null.</exception>
    public static string NodeKey(BaselineNode node)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        return IdentityOf(node) + "#" + node.Occurrence.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds the key a token observation is addressed by.
    /// </summary>
    /// <param name="name">The custom property name, including its leading dashes.</param>
    /// <returns>The observation key, prefixed so it cannot collide with a node identity.</returns>
    public static string TokenKey(string name)
    {
        return "token:" + name;
    }

    /// <summary>
    /// Compares every node the baseline holds against the matching observation.
    /// </summary>
    /// <param name="expected">The captured domain.</param>
    /// <param name="observed">
    /// The replacement shell's nodes and tokens, keyed by the identity the shell assigns.
    /// </param>
    /// <returns>One finding per difference; empty when the domain matches.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="expected"/> or <paramref name="observed"/> is null.
    /// </exception>
    public IReadOnlyList<GeometryFinding> Compare(
        BaselineDomain expected,
        IReadOnlyDictionary<string, ObservedNode> observed)
    {
        if (expected is null)
        {
            throw new ArgumentNullException(nameof(expected));
        }

        if (observed is null)
        {
            throw new ArgumentNullException(nameof(observed));
        }

        var findings = new List<GeometryFinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in expected.Tokens.Keys)
        {
            if (!observed.TryGetValue(TokenKey(key), out var token))
            {
                findings.Add(new GeometryFinding("token", key, $"expected {expected.Tokens[key]}", "missing"));
                continue;
            }

            var want = expected.Tokens[key];
            if (!TokenEquals(want, token.Text))
            {
                findings.Add(new GeometryFinding("token", key, want, token.Text));
            }
        }

        foreach (var node in expected.Nodes)
        {
            var key = NodeKey(node);
            seen.Add(key);

            if (!observed.TryGetValue(key, out var actual))
            {
                findings.Add(new GeometryFinding("missing", key, Describe(expected, node), "not observed"));
                continue;
            }

            CompareBox(findings, key, node, actual);
            CompareStyle(findings, key, expected, node, actual);
        }

        foreach (var key in observed.Keys)
        {
            if (seen.Contains(key) || key.StartsWith("token:", StringComparison.Ordinal))
            {
                continue;
            }

            findings.Add(new GeometryFinding("extra", key, string.Empty, "not in baseline"));
        }

        return findings;
    }

    /// <summary>
    /// Compares two colours by their channels, so the same ink written as hex or
    /// as <c>rgb()</c> reads as equal.
    /// </summary>
    /// <param name="expected">The colour the baseline recorded.</param>
    /// <param name="actual">The colour the shell resolved.</param>
    /// <returns>True when both parse to the same red, green and blue channel.</returns>
    /// <remarks>
    /// A fully transparent fill has no colour at all: the browser writes
    /// <c>rgba(0, 0, 0, 0)</c> for a control that never painted, and a shell that
    /// resolved a transparent white brush painted exactly as little. Comparing
    /// their channels would call the same absence two different inks.
    /// </remarks>
    private static bool ColorEquals(string expected, string actual)
    {
        var left = ParseColor(expected);
        var right = ParseColor(actual);
        if (left is null || right is null)
        {
            return false;
        }

        if (left.Value.A == 0 && right.Value.A == 0)
        {
            return true;
        }

        return left.Value.R == right.Value.R
            && left.Value.G == right.Value.G
            && left.Value.B == right.Value.B;
    }

    /// <summary>
    /// Parses a CSS colour into its channels.
    /// </summary>
    /// <param name="value">
    /// The colour text, as <c>#rgb</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c> or
    /// <c>rgb()</c>/<c>rgba()</c>.
    /// </param>
    /// <returns>The channels, or null when the text is in a form this does not read.</returns>
    private static (int R, int G, int B, int A)? ParseColor(string value)
    {
        var text = value.Trim();
        var hex = s_hexPattern.Match(text);
        if (hex.Success)
        {
            var digits = hex.Groups[1].Value;
            if (digits.Length == 3)
            {
                var r = Convert.ToInt32(new string(digits[0], 2), 16);
                var g = Convert.ToInt32(new string(digits[1], 2), 16);
                var b = Convert.ToInt32(new string(digits[2], 2), 16);
                return (r, g, b, 255);
            }

            var red = Convert.ToInt32(digits.Substring(0, 2), 16);
            var green = Convert.ToInt32(digits.Substring(2, 2), 16);
            var blue = Convert.ToInt32(digits.Substring(4, 2), 16);
            var alpha = digits.Length == 8 ? Convert.ToInt32(digits.Substring(6, 2), 16) : 255;
            return (red, green, blue, alpha);
        }

        var rgb = s_rgbPattern.Match(text);
        if (!rgb.Success)
        {
            return null;
        }

        var rr = (int)Math.Round(double.Parse(rgb.Groups[1].Value, CultureInfo.InvariantCulture));
        var gg = (int)Math.Round(double.Parse(rgb.Groups[2].Value, CultureInfo.InvariantCulture));
        var bb = (int)Math.Round(double.Parse(rgb.Groups[3].Value, CultureInfo.InvariantCulture));
        var aa = rgb.Groups[4].Success
            ? (int)Math.Round(double.Parse(rgb.Groups[4].Value.TrimEnd('%'), CultureInfo.InvariantCulture)
                * (rgb.Groups[4].Value.EndsWith('%') ? 2.55 : 255))
            : 255;
        return (rr, gg, bb, aa);
    }

    /// <summary>
    /// Compares two token values, accepting a pixel difference below the rounding
    /// step so a fractional layout pass does not read as a different value.
    /// </summary>
    /// <param name="expected">The token value the baseline recorded.</param>
    /// <param name="actual">The token value the shell resolved.</param>
    /// <returns>True when the values name the same length or colour.</returns>
    private static bool TokenEquals(string expected, string actual)
    {
        if (string.Equals(expected.Trim(), actual.Trim(), StringComparison.Ordinal))
        {
            return true;
        }

        var left = s_pxPattern.Match(expected.Trim());
        var right = s_pxPattern.Match(actual.Trim());
        if (left.Success && right.Success)
        {
            var a = double.Parse(left.Groups[1].Value, CultureInfo.InvariantCulture);
            var b = double.Parse(right.Groups[1].Value, CultureInfo.InvariantCulture);
            return Math.Abs(a - b) < 0.01;
        }

        return ColorEquals(expected, actual);
    }

    /// <summary>
    /// Describes a baseline node together with the domain it came from.
    /// </summary>
    /// <param name="domain">The domain the node belongs to.</param>
    /// <param name="node">The node to describe.</param>
    /// <returns>A single line naming the node's tag, classes, box and scheme.</returns>
    private static string Describe(BaselineDomain domain, BaselineNode node)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{node.Tag} .{node.Classes} box={node.Box.X},{node.Box.Y},{node.Box.Width},{node.Box.Height} scheme={domain.Scheme}");
    }

    /// <summary>
    /// Describes a baseline node on its own.
    /// </summary>
    /// <param name="node">The node to describe.</param>
    /// <returns>A single line naming the node's tag, classes and text.</returns>
    private static string Describe(BaselineNode node)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{node.Tag} .{node.Classes} text={node.Text}");
    }

    /// <summary>
    /// Compares one node's box against the baseline.
    /// </summary>
    /// <param name="findings">The list a difference is appended to.</param>
    /// <param name="identity">The node's identity, used in the finding.</param>
    /// <param name="node">The baseline node.</param>
    /// <param name="actual">What the shell laid out.</param>
    private void CompareBox(
        List<GeometryFinding> findings,
        string identity,
        BaselineNode node,
        ObservedNode actual)
    {
        var tolerance = _options.PixelTolerance;
        var diffs = new List<string>();

        if (Math.Abs(node.Box.X - actual.X) > tolerance)
        {
            diffs.Add($"x {node.Box.X} -> {actual.X}");
        }

        if (Math.Abs(node.Box.Y - actual.Y) > tolerance)
        {
            diffs.Add($"y {node.Box.Y} -> {actual.Y}");
        }

        if (Math.Abs(node.Box.Width - actual.Width) > tolerance)
        {
            diffs.Add($"w {node.Box.Width} -> {actual.Width}");
        }

        if (Math.Abs(node.Box.Height - actual.Height) > tolerance)
        {
            diffs.Add($"h {node.Box.Height} -> {actual.Height}");
        }

        if (diffs.Count > 0)
        {
            findings.Add(new GeometryFinding("box", identity, string.Join(", ", diffs), Describe(node)));
        }
    }

    /// <summary>
    /// Compares one node's resolved style against the baseline's style table.
    /// </summary>
    /// <param name="findings">The list a difference is appended to.</param>
    /// <param name="identity">The node's identity, used in the finding.</param>
    /// <param name="domain">The domain holding the style table.</param>
    /// <param name="node">The baseline node.</param>
    /// <param name="actual">What the shell resolved.</param>
    private void CompareStyle(
        List<GeometryFinding> findings,
        string identity,
        BaselineDomain domain,
        BaselineNode node,
        ObservedNode actual)
    {
        if (node.Style < 0 || node.Style >= domain.Styles.Count)
        {
            return;
        }

        var want = domain.Styles[node.Style];
        foreach (var property in _options.Properties)
        {
            // A property the two engines express differently is not compared; the
            // reason is recorded beside the entry rather than here, so the list
            // stays the one place a difference is excused.
            if (_options.Uncomparable.ContainsKey(property))
            {
                continue;
            }

            // A property the baseline omits held the browser default, so the
            // replacement must also leave it at its default. Comparing only the
            // recorded keys would let a new explicit value pass unseen.
            var expectedValue = want.TryGetValue(property, out var recorded) ? recorded : _options.Defaults[property];
            if (!actual.Styles.TryGetValue(property, out var actualValue))
            {
                actualValue = _options.Defaults[property];
            }

            if (StyleEquals(property, expectedValue, actualValue))
            {
                continue;
            }

            findings.Add(new GeometryFinding("style", identity + " " + property, expectedValue, actualValue));
        }
    }

    /// <summary>
    /// Compares one property, choosing the rule its values are compared by.
    /// </summary>
    /// <param name="property">The CSS property name.</param>
    /// <param name="expected">The value the baseline recorded.</param>
    /// <param name="actual">The value the shell resolved.</param>
    /// <returns>True when the two values mean the same thing.</returns>
    private bool StyleEquals(string property, string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return true;
        }

        if (_options.ColorProperties.Contains(property))
        {
            return ColorEquals(expected, actual);
        }

        if (_options.LengthProperties.Contains(property))
        {
            return LengthEquals(expected, actual);
        }

        return false;
    }

    /// <summary>
    /// Compares two lengths within the pixel tolerance.
    /// </summary>
    /// <param name="expected">The length the baseline recorded.</param>
    /// <param name="actual">The length the shell resolved.</param>
    /// <returns>True when both parse as pixels and differ by no more than the tolerance.</returns>
    private bool LengthEquals(string expected, string actual)
    {
        var left = s_pxPattern.Match(expected);
        var right = s_pxPattern.Match(actual);
        if (!left.Success || !right.Success)
        {
            return false;
        }

        var a = double.Parse(left.Groups[1].Value, CultureInfo.InvariantCulture);
        var b = double.Parse(right.Groups[1].Value, CultureInfo.InvariantCulture);
        return Math.Abs(a - b) <= _options.PixelTolerance;
    }
}
