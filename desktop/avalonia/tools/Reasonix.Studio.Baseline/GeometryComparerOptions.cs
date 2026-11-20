using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Baseline;

/// <summary>
/// The tolerances and property set a <see cref="GeometryComparer"/> works with.
/// </summary>
public sealed class GeometryComparerOptions
{
    /// <summary>
    /// Gets the options the current Studio baseline is compared with.
    /// </summary>
    public static GeometryComparerOptions Studio { get; } = new GeometryComparerOptions();

    /// <summary>
    /// Gets or sets how far a box or length may drift, in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// Half a pixel: layout engines round differently at the last step, and a
    /// replacement shell cannot be held to a fraction no person named.
    /// </remarks>
    public double PixelTolerance { get; set; } = 0.5;

    /// <summary>
    /// Gets the properties compared on every node.
    /// </summary>
    public IReadOnlyList<string> Properties
    {
        get;
    } = new[]
    {
        "display",
        "fontFamily",
        "fontSize",
        "fontWeight",
        "lineHeight",
        "letterSpacing",
        "textAlign",
        "color",
        "backgroundColor",
        "borderTopWidth",
        "borderTopLeftRadius",
        "paddingTop",
        "paddingRight",
        "paddingBottom",
        "paddingLeft",
        "marginTop",
        "marginRight",
        "marginBottom",
        "marginLeft",
        "gap",
        "rowGap",
        "columnGap",
        "opacity",
        "zIndex",
    };

    /// <summary>
    /// Gets the properties whose values are compared as colours rather than as text.
    /// </summary>
    public IReadOnlySet<string> ColorProperties
    {
        get;
    } = new HashSet<string>(StringComparer.Ordinal)
    {
        "color",
        "backgroundColor",
    };

    /// <summary>
    /// Gets the properties whose values are compared as lengths.
    /// </summary>
    public IReadOnlySet<string> LengthProperties
    {
        get;
    } = new HashSet<string>(StringComparer.Ordinal)
    {
        "fontSize",
        "borderTopWidth",
        "borderTopLeftRadius",
        "paddingTop",
        "paddingRight",
        "paddingBottom",
        "paddingLeft",
        "marginTop",
        "marginRight",
        "marginBottom",
        "marginLeft",
        "gap",
        "rowGap",
        "columnGap",
    };

    /// <summary>
    /// Gets the properties that are not compared, each with the reason it cannot
    /// be.
    /// </summary>
    /// <remarks>
    /// The one place a difference is excused, so every entry names what the
    /// browser reports and what takes its place here. Only properties in
    /// <see cref="Properties"/> belong: an entry for one that is never compared
    /// would widen the list without widening the gate.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Uncomparable
    {
        get;
    } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["display"] = "The panel type names the layout here; CSS names it with a display value.",
        ["fontFamily"] = "Font stacks differ per platform; glyph fallback is an allowed difference.",
        ["lineHeight"] = "A line box follows the font's own metrics here rather than a length.",
        ["gap"] = "Spacing is StackPanel.Spacing, not a container property.",
        ["rowGap"] = "Spacing is StackPanel.Spacing, not a container property.",
        ["columnGap"] = "Spacing is StackPanel.Spacing, not a container property.",
        ["zIndex"] = "Stacking is panel order and overlay layers, not a per-element integer.",
        ["marginLeft"] = "A browser offsets a column with margin; here a Grid column carries the width.",
        ["marginRight"] = "A browser offsets a column with margin; here a Grid column carries the width.",
    };

    /// <summary>
    /// Gets the value each property takes when neither side sets it, so a
    /// property the baseline omits is still compared.
    /// </summary>
    public IReadOnlyDictionary<string, string> Defaults
    {
        get;
    } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["display"] = "block",
        ["fontFamily"] = string.Empty,
        ["fontSize"] = "0px",
        ["fontWeight"] = "400",
        ["lineHeight"] = "normal",
        ["letterSpacing"] = "normal",
        ["textAlign"] = "start",
        ["color"] = "rgb(0, 0, 0)",
        ["backgroundColor"] = "rgba(0, 0, 0, 0)",
        ["borderTopWidth"] = "0px",
        ["borderTopLeftRadius"] = "0px",
        ["paddingTop"] = "0px",
        ["paddingRight"] = "0px",
        ["paddingBottom"] = "0px",
        ["paddingLeft"] = "0px",
        ["marginTop"] = "0px",
        ["marginRight"] = "0px",
        ["marginBottom"] = "0px",
        ["marginLeft"] = "0px",
        ["gap"] = "normal",
        ["rowGap"] = "normal",
        ["columnGap"] = "normal",
        ["opacity"] = "1",
        ["zIndex"] = "auto",
    };
}
