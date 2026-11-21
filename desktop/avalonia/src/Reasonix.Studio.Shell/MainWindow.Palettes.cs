using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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

            result.ThemeDictionaries[variant] = colors;
        }

        return result;
    }

    /// <summary>Draws the sidebar, page and accent of one pack in the current scheme.</summary>
    /// <param name="pack">The pack, or null for Studio's built-in colors.</param>
    /// <param name="name">Displayed pack name.</param>
    /// <returns>The swatch and its name.</returns>
    private static StackPanel PaletteSample(JsonObject? pack, string name)
    {
        var art = new Grid { Width = 150, Height = 80, ColumnDefinitions = new ColumnDefinitions("25,*") };
        var dictionary = pack is null ? new ResourceDictionary() : PaletteResources(pack);
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            if (!dictionary.ThemeDictionaries.ContainsKey(variant))
            {
                dictionary.ThemeDictionaries[variant] = new ResourceDictionary();
            }

            var colors = (ResourceDictionary)dictionary.ThemeDictionaries[variant];
            foreach (var key in new[] { "PageBrush", "SurfaceBrush", "MutedBrush", "AccentBrush" })
            {
                if (!colors.ContainsKey(key) && Application.Current!.TryGetResource(key, variant, out var fallback))
                {
                    colors[key] = fallback;
                }
            }
        }

        art.Resources.MergedDictionaries.Add(dictionary);
        art.Bind(Panel.BackgroundProperty, art.GetResourceObservable("PageBrush"));
        var rail = new Border();
        rail.Bind(Border.BackgroundProperty, rail.GetResourceObservable("SurfaceBrush"));
        art.Children.Add(rail);
        var lines = new StackPanel { Margin = new Thickness(9, 12), Spacing = 6 };
        Grid.SetColumn(lines, 1);
        art.Children.Add(lines);
        foreach (var (width, brush) in new[] { (91, "MutedBrush"), (60, "MutedBrush"), (32, "AccentBrush") })
        {
            var line = new Border { Width = width, Height = brush == "AccentBrush" ? 5 : 2, HorizontalAlignment = HorizontalAlignment.Left };
            line.Bind(Border.BackgroundProperty, line.GetResourceObservable(brush));
            lines.Children.Add(line);
        }

        var tile = new StackPanel { Spacing = 4 };
        tile.Children.Add(art);
        tile.Children.Add(new TextBlock { Text = name, [!TextBlock.FontSizeProperty] = StudioTypography.Size(12) });
        return tile;
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

            if (packs.OfType<JsonObject>().FirstOrDefault(p => (bool?)p["active"] == true) is { } active)
            {
                _palette = PaletteResources(active);
                Resources.MergedDictionaries.Add(_palette);
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
        group.Children.Add(new TextBlock { Text = MessageCatalog.Get("look.interface_theme", Language), FontWeight = FontWeight.SemiBold });
        group.Children.Add(Detail(MessageCatalog.Get("look.choose_the_workbench_surfaces_and", Language)));
        var featured = new WrapPanel();
        var illustrated = new WrapPanel();
        group.Children.Add(featured);
        var more = new Expander { IsExpanded = true, Header = MessageCatalog.Get("look.more_installed_themes", Language), Content = illustrated, HorizontalAlignment = HorizontalAlignment.Stretch };
        group.Children.Add(more);
        Populate(packs);

        void Populate(JsonArray inventory)
        {
            featured.Children.Clear();
            illustrated.Children.Clear();
            var active = inventory.OfType<JsonObject>().FirstOrDefault(p => (bool?)p["active"] == true);
            foreach (var pack in inventory.OfType<JsonObject>().Prepend(null))
            {
                var id = (string?)pack?["id"] ?? string.Empty;
                var name = (string?)pack?["name"] ?? MessageCatalog.Get(MessageKey.Common.Default, Language);
                var button = new Button { Content = PaletteSample(pack, name), Padding = new Thickness(7), Margin = new Thickness(0, 0, 9, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
                button.Bind(Button.BorderBrushProperty, button.GetResourceObservable(ReferenceEquals(active, pack) ? "AccentBrush" : "BorderBrush"));
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
                var container = pack?["sky"] is not null || (bool?)pack?["background"]?["image"] == true ? illustrated : featured;
                container.Children.Add(button);
            }

            more.IsVisible = illustrated.Children.Count > 0;
        }
    }
}
