using Avalonia.Markup.Xaml.MarkupExtensions;
using System.Globalization;

namespace Reasonix.Studio.Controls;

/// <summary>Dynamic type resources shared by XAML templates and code-built controls.</summary>
public static class StudioTypography
{
    /// <summary>Creates a binding for a particular base UI size.</summary>
    /// <param name="points">The unscaled size in device-independent pixels.</param>
    /// <returns>The reader's scaled size resource.</returns>
    public static DynamicResourceExtension Size(int points) => new("StudioType" + points.ToString(CultureInfo.InvariantCulture));

    /// <summary>Creates a binding for a named typography resource.</summary>
    /// <param name="name">A reading or monospace resource.</param>
    /// <returns>The live resource.</returns>
    public static DynamicResourceExtension Resource(string name) => new(name);
}
