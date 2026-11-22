using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
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
    /// <param name="root">The window coordinate system.</param>
    /// <param name="region">Optional visible subtree to compare.</param>
    /// <returns>
    /// The observed nodes and tokens, keyed by the same identity the baseline uses.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="root"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when two controls declare the same identity, because an identity
    /// must name exactly one element for a comparison to mean anything.
    /// </exception>
    public IReadOnlyDictionary<string, ObservedNode> Export(Visual root, Visual? region = null)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var observed = new Dictionary<string, ObservedNode>(StringComparer.Ordinal);
        var origin = root.TranslatePoint(default, root) ?? default;
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var visual in (region ?? root).GetSelfAndVisualDescendants())
        {
            if (visual is not Control control || !control.IsEffectivelyVisible || control.Bounds.Width == 0 || control.Bounds.Height == 0)
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

        ExportTokens(observed, root.ActualThemeVariant);

        return observed;
    }

    /// <summary>
    /// Copies the captured token values out of the application's resources.
    /// </summary>
    /// <param name="observed">The observations to add the tokens to.</param>
    /// <param name="actual">The theme of the window being measured.</param>
    /// <remarks>
    /// A token is not an element: it is a value the stylesheet resolved once and
    /// every element reads. The dictionary holds each one twice — a typed
    /// resource a control binds to, and the captured text under <c>token:</c>
    /// — and the comparison is against the text, because a round trip through a
    /// Color or a Double would answer a question about the type rather than about
    /// the value the browser resolved.
    /// </remarks>
    private static void ExportTokens(Dictionary<string, ObservedNode> observed, ThemeVariant actual)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        // The dictionary declares Light and Dark; a headless application reports
        // Default until a window asks for one, so an unresolved variant reads the
        // light tier rather than finding nothing.
        var variant = actual;
        if (variant == ThemeVariant.Default)
        {
            variant = ThemeVariant.Light;
        }

        foreach (var merged in app.Resources.MergedDictionaries)
        {
            if (merged is not ResourceDictionary dictionary)
            {
                continue;
            }

            var source = dictionary.ThemeDictionaries.TryGetValue(variant, out var provider)
                && provider is ResourceDictionary themed
                    ? themed
                    : dictionary;

            foreach (var key in source.Keys)
            {
                if (key is not string name
                    || !name.StartsWith("token:", StringComparison.Ordinal)
                    || !source.TryGetResource(name, null, out var value)
                    || value is not string text)
                {
                    continue;
                }

                observed[name] = new ObservedNode { Text = text };
            }
        }
    }

    /// <summary>
    /// Reads the font family a control resolves, following inheritance.
    /// </summary>
    /// <param name="control">The control to read.</param>
    /// <returns>The family the control draws its text in, or null when it draws none.</returns>
    private static string? FamilyOf(Control control)
    {
        return TextElement.GetFontFamily(control)?.ToString();
    }

    /// <summary>
    /// Reads the alignment of the content a control draws.
    /// </summary>
    /// <param name="control">The control to read.</param>
    /// <returns>The CSS spelling of that alignment, or "start" when it has none.</returns>
    /// <remarks>
    /// CSS reports one property for both a block's text and a button's label;
    /// Avalonia splits it into <see cref="TextBlock.TextAlignment"/> and
    /// <see cref="ContentControl.HorizontalContentAlignment"/>, so this reads
    /// whichever the control actually has rather than inventing a third value.
    /// </remarks>
    private static string AlignOf(Control control)
    {
        return control switch
        {
            TextBlock text => text.TextAlignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "justify",
                _ => "start",
            },
            ContentControl content => content.HorizontalContentAlignment switch
            {
                HorizontalAlignment.Center => "center",
                HorizontalAlignment.Right => "right",
                HorizontalAlignment.Left when content.IsSet(ContentControl.HorizontalContentAlignmentProperty) => "left",

                // Stretch is how a control fills its slot, not how its text is
                // aligned inside it: CSS reports `start` for that, and reading
                // Stretch as `justify` would name a property neither side set.
                _ => "start",
            },
            _ => "start",
        };
    }

    /// <summary>
    /// Formats a corner radius the way CSS reports a pill or a circle.
    /// </summary>
    /// <param name="radius">The radius the control resolves.</param>
    /// <param name="control">The control the radius belongs to.</param>
    /// <returns>"50%" when the radius is half the shorter side, otherwise the length.</returns>
    /// <remarks>
    /// The reference stylesheet asks for a circle with <c>border-radius: 50%</c>;
    /// Avalonia takes a length, so the same shape is spelled as half the shorter
    /// side. Reading the shape rather than the spelling is what lets the two be
    /// compared at all.
    /// </remarks>
    private static string RadiusText(CornerRadius radius, Control control)
    {
        var shorter = Math.Min(control.Bounds.Width, control.Bounds.Height);
        var value = Math.Max(Math.Max(radius.TopLeft, radius.TopRight), Math.Max(radius.BottomRight, radius.BottomLeft));
        if (shorter > 0 && Math.Abs(value - (shorter / 2)) < 0.5)
        {
            return "50%";
        }

        return Px(radius.TopLeft);
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
                return FamilyOf(control);

            case "fontSize":
                return Px(TextElement.GetFontSize(control));

            case "fontWeight":
                return ((int)TextElement.GetFontWeight(control)).ToString(CultureInfo.InvariantCulture);

            case "lineHeight":
                if (control is TextBlock lined && lined.LineHeight > 0)
                {
                    return Px(lined.LineHeight);
                }

                return "normal";

            case "color":
                return TextElement.GetForeground(control) is ISolidColorBrush fore ? Rgb(fore.Color) : null;

            case "letterSpacing":
                // The reference frontend sets it per element; a text control here
                // carries it as TextBlock.LetterSpacing, and anything else has none.
                return control is TextBlock spaced
                    ? Px(spaced.LetterSpacing)
                    : "normal";

            case "textAlign":
                // CSS reports the horizontal alignment of the text inside its box.
                // A button says it with HorizontalContentAlignment, a text block
                // with TextAlignment; neither is the CSS property, so it is read
                // from whichever one this control actually has.
                return AlignOf(control);

            case "backgroundColor":
                if (control is Border { Background: ISolidColorBrush borderFill })
                {
                    return Rgba(borderFill.Color);
                }

                if (control is TemplatedControl filled && filled.Background is ISolidColorBrush back)
                {
                    return Rgba(back.Color);
                }

                return null;

            case "paddingTop":
                if (control is Border borderTop)
                {
                    return Px(borderTop.Padding.Top);
                }

                if (control is TemplatedControl paddedTop)
                {
                    return Px(paddedTop.Padding.Top);
                }

                return null;

            case "paddingRight":
                if (control is Border borderRight)
                {
                    return Px(borderRight.Padding.Right);
                }

                if (control is TemplatedControl paddedRight)
                {
                    return Px(paddedRight.Padding.Right);
                }

                return null;

            case "paddingBottom":
                if (control is Border borderBottom)
                {
                    return Px(borderBottom.Padding.Bottom);
                }

                if (control is TemplatedControl paddedBottom)
                {
                    return Px(paddedBottom.Padding.Bottom);
                }

                return null;

            case "paddingLeft":
                if (control is Border borderLeft)
                {
                    return Px(borderLeft.Padding.Left);
                }

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
                if (control is Border border)
                {
                    return Px(border.BorderThickness.Top);
                }

                if (control is TemplatedControl bordered)
                {
                    return Px(bordered.BorderThickness.Top);
                }

                return null;

            case "borderTopLeftRadius":
                if (control is Border roundBorder)
                {
                    return RadiusText(roundBorder.CornerRadius, control);
                }

                if (control is TemplatedControl rounded)
                {
                    return RadiusText(rounded.CornerRadius, control);
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
