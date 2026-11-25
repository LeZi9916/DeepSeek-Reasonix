using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Maps pack colors to the same surfaces as the React theme renderer.</summary>
    private static readonly (string Token, string Resource)[] s_paletteColors =
    [
        ("bg", "Page"), ("bgSoft", "Surface"), ("panel", "Raised"), ("bgElev", "Overlay"),
        ("border", "Border"), ("borderSoft", "Hair"), ("fg", "Text"), ("fgDim", "Muted"),
        ("fgStrong", "TextStrong"), ("fgFaint", "Faint"), ("fgFaint", "Ghost"),
        ("accent", "Accent"), ("accentFg", "AccentFg"), ("accentInk", "AccentInk"),
        ("link", "Link"), ("brand", "Brand"), ("halo", "Halo"), ("labelAgent", "LabelAgent"),
        ("float", "Float"), ("floatHi", "FloatHi"), ("codeBg", "FaceCode"), ("sunkBg", "FaceSunk"),
        ("synKeyword", "SynKey"), ("synString", "SynStr"), ("synNumber", "SynNum"), ("synFunction", "SynFn"),
    ];

    /// <summary>Maps the Electron composer’s brightness filter to the theme paint it affects.</summary>
    private static readonly (string Source, string Hover)[] s_sendHoverColors =
    [
        ("Accent", "SendHoverBrush"), ("AccentFg", "SendHoverInkBrush"),
        ("Text", "SteerHoverBrush"), ("Surface", "SteerHoverInkBrush"),
    ];

    /// <summary>The override owned by the active kernel theme.</summary>
    private ResourceDictionary? _palette;

    /// <summary>Prevents an earlier inventory read overwriting a newer selection.</summary>
    private int _paletteRead;

    /// <summary>Serializes selections made from the same window.</summary>
    private bool _pickingPalette;

    /// <summary>Builds theme variant resources using only known Studio color slots.</summary>
    /// <param name="pack">The installed pack whose colors are being resolved.</param>
    /// <returns>Paired light and dark resource dictionaries.</returns>
    private static ResourceDictionary PaletteResources(JsonObject pack)
    {
        var result = new ResourceDictionary();
        foreach (var (scheme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            var colors = new ResourceDictionary();
            foreach (var (token, resource) in s_paletteColors)
            {
                if ((string?)pack["tokens"]?[scheme]?[token] is not { } css)
                {
                    continue;
                }

                // CSS places alpha last in four- and eight-digit hexadecimal colors.
                if (css.StartsWith('#') && css.Length is 5 or 9)
                {
                    var alpha = (css.Length - 1) / 4;
                    css = "#" + css[^alpha..] + css[1..^alpha];
                }

                if (Color.TryParse(css, out var color))
                {
                    colors[resource] = color;
                    colors[resource + "Brush"] = new SolidColorBrush(color);
                }
            }

            foreach (var (source, hover) in s_sendHoverColors)
            {
                if (colors.TryGetResource(source, variant, out var value) && value is Color color)
                {
                    colors[hover] = new SolidColorBrush(Color.FromArgb(color.A, Brightened(color.R), Brightened(color.G), Brightened(color.B)));
                }
            }

            if (pack["sky"] is not null || (bool?)pack["background"]?["image"] == true)
            {
                foreach (var (field, slot) in new[] { ("rail", "ArtworkRailBrush"), ("chrome", "ArtworkChromeBrush"), ("side", "ArtworkSideBrush"), ("compose", "ArtworkComposeBrush") })
                {
                    var key = field == "compose" ? "RaisedBrush" : "PageBrush";
                    if (!colors.TryGetResource(key, variant, out var brush))
                    {
                        Application.Current!.TryGetResource(key, variant, out brush);
                    }

                    if (brush is ISolidColorBrush source)
                    {
                        var opacity = (double?)pack["background"]?["surfaceOpacity"]?[scheme]?[field] ?? (field == "compose" ? 0.8 : 0.62);
                        colors[slot] = new SolidColorBrush(source.Color, Math.Clamp(opacity, 0, 1));
                    }
                }
            }

            result.ThemeDictionaries[variant] = colors;
        }

        return result;

        static byte Brightened(byte channel) => (byte)Math.Min(255, Math.Round(channel * 1.18, MidpointRounding.AwayFromZero));
    }

    /// <summary>Draws the sidebar, page and accent of one pack in the current scheme.</summary>
    /// <param name="pack">The pack, or null for Studio's built-in colors.</param>
    /// <returns>The swatch.</returns>
    private static Grid PaletteSample(JsonObject? pack)
    {
        var art = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*") };
        var dictionary = pack is null ? new ResourceDictionary() : PaletteResources(pack);
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            if (!dictionary.ThemeDictionaries.ContainsKey(variant))
            {
                dictionary.ThemeDictionaries[variant] = new ResourceDictionary();
            }

            var colors = (ResourceDictionary)dictionary.ThemeDictionaries[variant];
            foreach (var key in new[] { "PageBrush", "SurfaceBrush", "RaisedBrush", "BorderBrush", "HairBrush", "TextBrush", "MutedBrush", "GhostBrush", "AccentBrush" })
            {
                if (!colors.ContainsKey(key) && Application.Current!.TryGetResource(key, variant, out var fallback))
                {
                    colors[key] = fallback;
                }
            }
        }

        art.Resources.MergedDictionaries.Add(dictionary);
        art.Bind(Panel.BackgroundProperty, art.GetResourceObservable("PageBrush"));
        var rail = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
        rail.Bind(Border.BackgroundProperty, rail.GetResourceObservable("SurfaceBrush"));
        rail.Bind(Border.BorderBrushProperty, rail.GetResourceObservable("HairBrush"));
        art.Children.Add(rail);

        var body = new Border
        {
            Margin = new Thickness(10),
            Padding = new Thickness(8, 9),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };
        body.Bind(Border.BackgroundProperty, body.GetResourceObservable("RaisedBrush"));
        body.Bind(Border.BorderBrushProperty, body.GetResourceObservable("HairBrush"));
        Grid.SetColumn(body, 1);
        art.Children.Add(body);

        var lines = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 5 };
        var first = new Border { Height = 4, CornerRadius = new CornerRadius(2), Opacity = 0.78 };
        first.Bind(Border.BackgroundProperty, first.GetResourceObservable("TextBrush"));
        lines.Children.Add(first);

        var shortened = new Border { Height = 4, CornerRadius = new CornerRadius(2), Opacity = 0.72 };
        shortened.Bind(Border.BackgroundProperty, shortened.GetResourceObservable("MutedBrush"));
        var shortRow = new Grid { ColumnDefinitions = new ColumnDefinitions("58*,42*") };
        shortRow.Children.Add(shortened);
        Grid.SetRow(shortRow, 1);
        lines.Children.Add(shortRow);

        var mark = new Border { Height = 5, CornerRadius = new CornerRadius(2) };
        mark.Bind(Border.BackgroundProperty, mark.GetResourceObservable("AccentBrush"));
        var markRow = new Grid { ColumnDefinitions = new ColumnDefinitions("34*,66*") };
        markRow.Children.Add(mark);
        Grid.SetRow(markRow, 3);
        lines.Children.Add(markRow);
        body.Child = lines;

        return art;
    }

    /// <summary>Reads the kernel's selection and applies it to both window theme variants.</summary>
    /// <param name="api">The connected kernel.</param>
    /// <returns>The inventory for the picker.</returns>
    private async Task<JsonArray> LoadPaletteAsync(StudioPageClient api)
    {
        var ticket = ++_paletteRead;
        var packs = await api.ThemesAsync(_closing.Token).ConfigureAwait(true);
        if (ticket == _paletteRead && !_closing.IsCancellationRequested)
        {
            if (_palette is not null)
            {
                Resources.MergedDictionaries.Remove(_palette);
                _palette = null;
            }

            var active = packs.OfType<JsonObject>().FirstOrDefault(p => (bool?)p["active"] == true);
            if (active is not null)
            {
                _palette = PaletteResources(active);
                Resources.MergedDictionaries.Add(_palette);
            }

            Classes.Set("illustrated", active?["sky"] is not null || (bool?)active?["background"]?["image"] == true);
            Backdrop.SetBusy(Conversation.State.Running);
            try
            {
                await Backdrop.ApplyAsync(active, api, _closing.Token).ConfigureAwait(true);
            }
            catch (Exception error) when (!_closing.IsCancellationRequested)
            {
                if (ticket == _paletteRead)
                {
                    SettingsError.Text = DescribeError(error);
                }
            }
        }

        return packs;
    }

    /// <summary>Displays installed color previews and saves a selection through the kernel.</summary>
    /// <param name="card">The appearance card.</param>
    /// <param name="api">The connected kernel.</param>
    /// <returns>The initial read.</returns>
    private async Task ShowPalettesAsync(StackPanel card, StudioPageClient api)
    {
        var packs = await LoadPaletteAsync(api).ConfigureAwait(true);
        var group = new StackPanel { Spacing = 8, Tag = "look.interface_theme", Margin = new Thickness(0, 12) };
        card.Children.Add(group);
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(new TextBlock { Text = MessageCatalog.Get("look.interface_theme", Language), FontWeight = FontWeight.Medium });
        var now = new TextBlock
        {
            Text = MessageCatalog.Get("look.in_effect_now", Language),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        now.Bind(TextBlock.ForegroundProperty, now.GetResourceObservable("FaintBrush"));
        Grid.SetColumn(now, 1);
        heading.Children.Add(now);
        group.Children.Add(heading);
        group.Children.Add(Detail(MessageCatalog.Get("look.choose_the_workbench_surfaces_and", Language)));
        var featured = new Settings.PaletteGrid();
        group.Children.Add(featured);
        Settings.ThemeMore? more = null;
        Populate(packs);

        void Populate(JsonArray inventory)
        {
            if (more is not null)
            {
                group.Children.Remove(more);
                more = null;
            }

            var illustrated = new Settings.PaletteGrid();
            featured.Children.Clear();
            var active = inventory.OfType<JsonObject>().FirstOrDefault(p => (bool?)p["active"] == true);
            foreach (var pack in inventory.OfType<JsonObject>().Prepend(null))
            {
                var id = (string?)pack?["id"] ?? string.Empty;
                var name = (string?)pack?["name"] ?? MessageCatalog.Get(MessageKey.Common.Default, Language);
                var selected = ReferenceEquals(active, pack);

                var frame = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    ClipToBounds = true,
                    Child = new AspectRatioView { Ratio = 1.6, Child = PaletteSample(pack) },
                };
                frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable(selected ? "AccentBrush" : "BorderBrush"));

                var preview = new Panel { UseLayoutRounding = true };
                preview.Children.Add(frame);
                if (selected)
                {
                    var selectionRing = new Border
                    {
                        BorderThickness = new Thickness(2),
                        CornerRadius = new CornerRadius(7),
                        IsHitTestVisible = false,
                    };
                    selectionRing.Bind(Border.BorderBrushProperty, selectionRing.GetResourceObservable("AccentBrush"));
                    preview.Children.Add(selectionRing);
                    var ring = new Ellipse { Width = 11, Height = 11 };
                    ring.Bind(Shape.FillProperty, ring.GetResourceObservable("PageBrush"));
                    var dot = new Ellipse { Width = 7, Height = 7 };
                    dot.Bind(Shape.FillProperty, dot.GetResourceObservable("AccentBrush"));
                    var mark = new Grid
                    {
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 7, 7, 0),
                    };
                    mark.Children.Add(ring);
                    mark.Children.Add(dot);
                    preview.Children.Add(mark);
                }

                var nameBlock = new TextBlock
                {
                    Text = name,
                    FontSize = 13,
                    LineHeight = 16,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                nameBlock.Bind(TextBlock.ForegroundProperty, nameBlock.GetResourceObservable(selected ? "TextBrush" : "MutedBrush"));
                var authorBlock = new TextBlock
                {
                    Text = (string?)pack?["author"] ?? MessageCatalog.Get("common.built_in", Language),
                    FontSize = 10,
                    LineHeight = 16,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                authorBlock.Bind(TextBlock.ForegroundProperty, authorBlock.GetResourceObservable("GhostBrush"));

                var content = new StackPanel { Spacing = 7 };
                content.Children.Add(preview);
                var label = new StackPanel();
                label.Children.Add(nameBlock);
                label.Children.Add(authorBlock);
                content.Children.Add(label);
                var button = new Button
                {
                    Content = content,
                    Classes = { "palette" },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                };
                AutomationProperties.SetAutomationId(button, "action:theme.activate");
                AutomationProperties.SetName(button, name);
                button.Click += async (_, _) =>
                {
                    if (_pickingPalette || ReferenceEquals(active, pack))
                    {
                        return;
                    }

                    _pickingPalette = true;
                    group.IsEnabled = false;
                    try
                    {
                        await api.ActivateThemeAsync(id, _closing.Token).ConfigureAwait(true);
                        Populate(await LoadPaletteAsync(api).ConfigureAwait(true));
                    }
                    catch (Exception error)
                    {
                        if (TopLevel.GetTopLevel(group) == this)
                        {
                            SettingsError.Text = DescribeError(error);
                        }
                    }
                    finally
                    {
                        _pickingPalette = false;
                        group.IsEnabled = true;
                    }
                };

                var cell = new StackPanel { Spacing = 6 };
                cell.Children.Add(button);
                if ((bool?)pack?["removable"] == true)
                {
                    cell.Children.Add(Remove(id, name));
                }

                var container = pack?["sky"] is not null || (bool?)pack?["background"]?["image"] == true ? illustrated : featured;
                container.Children.Add(cell);
            }

            if (illustrated.Children.Count > 0)
            {
                more = new Settings.ThemeMore(
                    MessageCatalog.Get("look.more_installed_themes", Language),
                    MessageCatalog.Get("look.illustrated_and_background_themes_you", Language),
                    MessageCatalog.Get("common.n", Language, ("n", illustrated.Children.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                    illustrated);
                group.Children.Add(more);
            }
        }

        Button Remove(string id, string name)
        {
            var armed = false;
            var button = new Button
            {
                Content = MessageCatalog.Get("common.delete", Language),
                Classes = { "palette-remove" },
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetAutomationId(button, "action:theme.remove");
            AutomationProperties.SetName(button, MessageCatalog.Get("look.delete_theme_name", Language, ("name", name)));
            button.Click += async (_, _) =>
            {
                if (_pickingPalette)
                {
                    return;
                }

                if (!armed)
                {
                    armed = true;
                    button.Content = MessageCatalog.Get("common.confirm_delete", Language);
                    button.Classes.Add("confirming");
                    return;
                }

                _pickingPalette = true;
                button.IsEnabled = false;
                try
                {
                    await api.RemoveThemeAsync(id, _closing.Token).ConfigureAwait(true);
                    Populate(await LoadPaletteAsync(api).ConfigureAwait(true));
                }
                catch (Exception error) when (!_closing.IsCancellationRequested)
                {
                    SettingsError.Text = DescribeError(error);
                    button.IsEnabled = true;
                    armed = false;
                    button.Classes.Remove("confirming");
                    button.Content = MessageCatalog.Get("common.delete", Language);
                }
                finally
                {
                    _pickingPalette = false;
                }
            };
            button.PointerExited += (_, _) => Disarm();
            button.LostFocus += (_, _) => Disarm();
            return button;

            void Disarm()
            {
                if (!armed)
                {
                    return;
                }

                armed = false;
                button.Classes.Remove("confirming");
                button.Content = MessageCatalog.Get("common.delete", Language);
            }
        }
    }
}
