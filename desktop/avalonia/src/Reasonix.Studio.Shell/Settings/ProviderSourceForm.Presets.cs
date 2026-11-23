using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

internal sealed partial class ProviderSourceForm
{
    /// <summary>Model source presets returned by the host.</summary>
    private JsonArray _presets = [];

    /// <summary>The selected preset, or null for the custom form.</summary>
    private JsonObject? _preset;

    /// <summary>The vendor filter selected in the gallery.</summary>
    private string _vendor = string.Empty;

    /// <summary>Loads the presets from the active kernel.</summary>
    /// <returns>The catalogue read.</returns>
    private async Task LoadPresetsAsync()
    {
        try
        {
            _presets = await _api!.ProviderPresetsAsync(_token).ConfigureAwait(true);
            PresetsCount.Text = _presets.Count.ToString(CultureInfo.CurrentCulture);
            PresetNotice.Text = _presets.Count == 0 ? Text("persona.the_kernel_offers_no_service") : string.Empty;
            DrawPresets();
        }
        catch (Exception error) when (!_token.IsCancellationRequested)
        {
            PresetNotice.Text = MessageCatalog.Get("persona.the_list_of_common_services", _language, ("detail", _describe!(error)));
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
            return;
        }

        UpdateState();
    }

    /// <summary>Draws the vendor filters and matching service cards grouped by host.</summary>
    private void DrawPresets()
    {
        Vendors.Children.Clear();
        PresetGroups.Children.Clear();
        var catalogue = _presets.OfType<JsonObject>().ToArray();
        var vendors = catalogue.Select(item => (string?)item["vendor"]).OfType<string>().Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (vendors.Length > 0)
        {
            var ordered = vendors.Take(8).Concat(vendors.Skip(8).OrderBy(VendorLabel, StringComparer.CurrentCultureIgnoreCase));
            foreach (var vendor in ordered.Prepend(string.Empty))
            {
                var label = vendor.Length == 0 ? Text("common.all") : VendorLabel(vendor);
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
                if (ProviderVendorMark.Create(this, vendor, 14) is { } logo)
                {
                    content.Children.Add(logo);
                }

                content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
                var button = new Button { Content = content, Classes = { "provider-action", "preset-vendor" }, Height = 27 };
                button.Classes.Set("active", vendor == _vendor);
                AutomationProperties.SetAutomationId(button, "action:provider.preset-vendor");
                button.Tag = vendor;
                button.Click += (_, _) =>
                {
                    _vendor = vendor;
                    DrawPresets();
                };
                Vendors.Children.Add(button);
            }
        }

        var query = PresetSearch.Text?.Trim() ?? string.Empty;
        var shown = catalogue.Where(item => (_vendor.Length == 0 || (string?)item["vendor"] == _vendor)
            && $"{item["label"]} {item["id"]} {Host((string?)item["baseUrl"])}".Contains(query, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => Host((string?)item["baseUrl"]));
        var available = Math.Max(168, Bounds.Width - 30);
        var columns = Math.Max(1, (int)((available + 8) / 176));
        foreach (var group in shown)
        {
            var cards = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            var items = group.ToArray();

            // CSS auto-fill retains all tracks even when one host has fewer presets.
            var cardWidth = Math.Floor((available - ((columns - 1) * 8)) / columns);
            foreach (var item in items)
            {
                var labels = new StackPanel { Spacing = 2 };
                var title = new DockPanel();
                if (ProviderVendorMark.Create(this, (string?)item["vendor"], 18) is { } logo)
                {
                    logo.Margin = new Thickness(0, 0, 7, 0);
                    title.Children.Add(logo);
                }

                title.Children.Add(new TextBlock { Text = (string?)item["label"], Classes = { "source-heading" } });
                labels.Children.Add(title);
                labels.Children.Add(new TextBlock { Text = ProtocolName((string?)item["kind"] ?? string.Empty), Classes = { "source-tip" } });
                var button = new Button { Content = labels, Width = cardWidth, Classes = { "provider-action", "source-card" }, Height = double.NaN };
                AutomationProperties.SetAutomationId(button, "action:provider.preset:" + (string?)item["id"]);
                button.Click += (_, _) => SetPreset(item);
                cards.Children.Add(button);
            }

            PresetGroups.Children.Add(new StackPanel
            {
                Spacing = 6,
                Children = { new TextBlock { Text = group.Key, Classes = { "source-heading" } }, cards },
            });
        }

        if (PresetGroups.Children.Count == 0)
        {
            PresetGroups.Children.Add(new TextBlock { Text = Text("persona.no_matching_service"), Classes = { "provider-caption" } });
        }
    }

    /// <summary>Switches between the custom form and a curated preset without losing the custom draft.</summary>
    /// <param name="preset">The selected preset, or null to go back.</param>
    private void SetPreset(JsonObject? preset)
    {
        _preset = preset;
        SourceFields.IsVisible = preset is null;
        PresetFields.IsVisible = Back.IsVisible = preset is not null;
        Footer.Anchor = preset is null ? SourceFields : null;
        SaveError.IsVisible = false;
        if (preset is null)
        {
            Eyebrow.Text = Text("models.custom_source");
            Title.Text = Text("models.add_a_model_source");
            Introduction.Text = Text("models.use_the_service_documentation_connection");
            PresetCredential.Text = string.Empty;
            PresetName.Text = string.Empty;
        }
        else
        {
            Eyebrow.Text = ProtocolName((string?)preset["kind"] ?? string.Empty) + " · " + Host((string?)preset["baseUrl"]);
            Title.Text = (string?)preset["label"];
            Introduction.Text = Text("persona.the_kernel_creates_the_model");
            PresetCredential.Focus();
        }

        UpdateState();
    }

    /// <summary>Capitalizes the fallback vendor label shown when a mark is missing.</summary>
    /// <param name="vendor">Preset vendor identifier.</param>
    /// <returns>Vendor name.</returns>
    private string VendorLabel(string vendor) => vendor switch
    {
        "deepseek" => "DeepSeek",
        "openrouter" => "OpenRouter",
        "openai" => "OpenAI",
        "minimax" => "MiniMax",
        "zhipu" => "Zhipu",
        "qwen" => "Qwen",
        "mimo" => "MiMo",
        "longcat" => "LongCat",
        "stepfun" => "StepFun",
        "token-rhythm" => "token-rhythm",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(vendor),
    };
}
