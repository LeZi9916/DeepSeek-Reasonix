using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Reasonix.Studio.Baseline;

/// <summary>
/// Reads a live Avalonia visual tree into the same shape the reference frontend
/// was captured in, so the two can be compared value by value.
/// </summary>
/// <remarks>
/// The baseline was captured from a DOM, where an element has a class list, a
/// role and a computed style. Avalonia has none of those, so the mapping is by
/// declared identity rather than by structure: a control carries its identity in
/// <see cref="AutomationProperties.AutomationIdProperty"/> — the same string the
/// baseline recorded as <c>action:</c>, <c>role:</c> or <c>class:</c> — and its
/// resolved properties are formatted back into CSS-shaped text.
/// </remarks>
public sealed class GeometryExporter
{
    /// <summary>
    /// Holds the property set whose value forms this exporter produces.
    /// </summary>
    private readonly GeometryComparerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeometryExporter"/> class.
    /// </summary>
    /// <param name="options">The property set and tolerances whose value forms this exporter must produce.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public GeometryExporter(GeometryComparerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Walks a visual tree and returns every element that carries an identity.
    /// </summary>
    /// <param name="root">The visual to start from, usually the window's content.</param>
    /// <returns>
    /// The observed nodes and tokens, keyed by the same identity the baseline uses.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="root"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when two controls declare the same identity, because an identity
    /// must name exactly one element for a comparison to mean anything.
    /// </exception>
    public IReadOnlyDictionary<string, ObservedNode> Export(Visual root)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var observed = new Dictionary<string, ObservedNode>(StringComparer.Ordinal);
        var origin = root.TranslatePoint(default, root) ?? default;
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var visual in root.GetSelfAndVisualDescendants())
        {
            if (visual is not Control control)
            {
                continue;
            }

            var identity = IdentityOf(control);

            // An element without a declared identity is layout scaffolding: the
            // baseline never recorded it, so it has nothing to be compared to.
            if (identity is null)
            {
                continue;
            }

            // An identity names many elements, so the key pairs it with this
            // control's position among the ones sharing it, in visual-tree
            // order — the same rule the capture used on the document.
            var occurrence = occurrences.TryGetValue(identity, out var at) ? at : 0;
            occurrences[identity] = occurrence + 1;
            var key = identity + "#" + occurrence.ToString(CultureInfo.InvariantCulture);

            var topLeft = visual.TranslatePoint(default, root) ?? origin;
            var node = new ObservedNode
            {
                X = topLeft.X,
                Y = topLeft.Y,
                Width = control.Bounds.Width,
                Height = control.Bounds.Height,
            };

            foreach (var property in _options.Properties)
            {
                var value = Read(control, property);
                if (value is not null)
                {
                    node.Styles[property] = value;
                }
            }

            observed[key] = node;
        }

        return observed;
    }

    /// <summary>
    /// Reads the identity a control declares.
    /// </summary>
    /// <param name="control">The control to read.</param>
    /// <returns>
    /// The value of its automation id, or null when it declares none.
    /// </returns>
    private static string? IdentityOf(Control control)
    {
        var id = AutomationProperties.GetAutomationId(control);
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    /// <summary>
    /// Formats a length the way CSS reports it.
    /// </summary>
    /// <param name="value">The device-independent length.</param>
    /// <returns>The length in pixels, without trailing zeros.</returns>
    private static string Px(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture) + "px";
    }

    /// <summary>
    /// Formats a colour the way CSS reports an opaque ink.
    /// </summary>
    /// <param name="color">The colour to format.</param>
    /// <returns>The colour as <c>rgb(r, g, b)</c>.</returns>
    private static string Rgb(Color color)
    {
        return string.Create(CultureInfo.InvariantCulture, $"rgb({color.R}, {color.G}, {color.B})");
    }

    /// <summary>
    /// Formats a colour the way CSS reports a fill that may be transparent.
    /// </summary>
    /// <param name="color">The colour to format.</param>
    /// <returns>The colour as <c>rgba(r, g, b, a)</c>.</returns>
    private static string Rgba(Color color)
    {
        var alpha = color.A / 255d;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"rgba({color.R}, {color.G}, {color.B}, {alpha:0.###})");
    }

    /// <summary>
    /// Reads one property and formats it the way the browser reported it.
    /// </summary>
    /// <param name="control">The control to read from.</param>
    /// <param name="property">The CSS-shaped property name.</param>
    /// <returns>The formatted value, or null when this property has no Avalonia source.</returns>
    private string? Read(Control control, string property)
    {
        switch (property)
        {
            case "display":
                return control.IsVisible ? "block" : "none";

            case "fontFamily":
                return control is TemplatedControl templated && templated.FontFamily is { } family
                    ? family.ToString()
                    : null;

            case "fontSize":
                if (control is TemplatedControl sized)
                {
                    return Px(sized.FontSize);
                }

                return null;

            case "fontWeight":
                if (control is TemplatedControl weighted)
                {
                    return ((int)weighted.FontWeight).ToString(CultureInfo.InvariantCulture);
                }

                return null;

            case "lineHeight":
                if (control is TextBlock lined && lined.LineHeight > 0)
                {
                    return Px(lined.LineHeight);
                }

                return "normal";

            case "color":
                if (control is TemplatedControl inked && inked.Foreground is ISolidColorBrush fore)
                {
                    return Rgb(fore.Color);
                }

                return null;

            case "backgroundColor":
                if (control is TemplatedControl filled && filled.Background is ISolidColorBrush back)
                {
                    return Rgba(back.Color);
                }

                return null;

            case "paddingTop":
                if (control is TemplatedControl paddedTop)
                {
                    return Px(paddedTop.Padding.Top);
                }

                return null;

            case "paddingRight":
                if (control is TemplatedControl paddedRight)
                {
                    return Px(paddedRight.Padding.Right);
                }

                return null;

            case "paddingBottom":
                if (control is TemplatedControl paddedBottom)
                {
                    return Px(paddedBottom.Padding.Bottom);
                }

                return null;

            case "paddingLeft":
                if (control is TemplatedControl paddedLeft)
                {
                    return Px(paddedLeft.Padding.Left);
                }

                return null;

            case "marginTop":
                return Px(control.Margin.Top);

            case "marginRight":
                return Px(control.Margin.Right);

            case "marginBottom":
                return Px(control.Margin.Bottom);

            case "marginLeft":
                return Px(control.Margin.Left);

            case "borderTopWidth":
                if (control is TemplatedControl bordered)
                {
                    return Px(bordered.BorderThickness.Top);
                }

                return null;

            case "borderTopLeftRadius":
                if (control is TemplatedControl rounded)
                {
                    return Px(rounded.CornerRadius.TopLeft);
                }

                return null;

            case "opacity":
                return control.Opacity.ToString("0.###", CultureInfo.InvariantCulture);

            case "zIndex":
                return control.ZIndex.ToString(CultureInfo.InvariantCulture);

            case "gap":
            case "rowGap":
            case "columnGap":
                if (control is StackPanel stack && stack.Spacing > 0)
                {
                    return Px(stack.Spacing);
                }

                return "normal";

            default:
                return null;
        }
    }
}
