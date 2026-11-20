using System.Collections.Generic;

namespace Reasonix.Studio.Baseline;

/// <summary>
/// One node as the replacement shell observed it.
/// </summary>
public sealed class ObservedNode
{
    /// <summary>
    /// Gets or sets the left edge the shell laid out, in device-independent pixels.
    /// </summary>
    public double X
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the top edge the shell laid out, in device-independent pixels.
    /// </summary>
    public double Y
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the width the shell laid out, in device-independent pixels.
    /// </summary>
    public double Width
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the height the shell laid out, in device-independent pixels.
    /// </summary>
    public double Height
    {
        get; set;
    }

    /// <summary>
    /// Gets the style values the shell resolved.
    /// </summary>
    /// <remarks>
    /// Values are CSS-shaped strings so the comparison is over meaning rather
    /// than over a framework-specific colour or length type.
    /// </remarks>
    public Dictionary<string, string> Styles { get; } = new Dictionary<string, string>(System.StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the raw value a token observation carries.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
