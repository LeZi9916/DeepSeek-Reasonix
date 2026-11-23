using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

internal sealed partial class ProviderSourceForm
{
    /// <summary>Discovered and manually entered model identifiers.</summary>
    private readonly List<string> _models = [];

    /// <summary>Exact model identifiers supplied by the reader.</summary>
    private readonly HashSet<string> _manual = new(StringComparer.Ordinal);

    /// <summary>Enabled models in the order the reader selected them.</summary>
    private readonly List<string> _picked = [];

    /// <summary>The latest availability information for models on the draft connection.</summary>
    private readonly Dictionary<string, JsonObject> _checks = new(StringComparer.Ordinal);

    /// <summary>Probe data for the current address and credential.</summary>
    private ProviderProbe? _probe;

    /// <summary>The address normalized by the last successful probe.</summary>
    private string? _normalizedAddress;

    /// <summary>Clears connection findings when the address or credential changes.</summary>
    private void ClearProbe()
    {
        _probe = null;
        _normalizedAddress = null;
        ProbeMessage.IsVisible = false;
        ClearChecks();
    }

    /// <summary>Clears now-stale model checks for the edited connection.</summary>
    private void ClearChecks()
    {
        _checks.Clear();
        DrawModels();
        UpdateState();
    }

    /// <summary>Draws matching model rows and the exact-model add action.</summary>
    private void DrawModels()
    {
        var query = ModelSearch.Text?.Trim() ?? string.Empty;
        var matches = _models.Where(model => model.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        ModelRows.Children.Clear();
        foreach (var model in matches.Where(model => _picked.Contains(model)).Concat(matches.Where(model => !_picked.Contains(model)).Take(Math.Max(0, 60 - _picked.Count))))
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("20,*,Auto"),
                ColumnSpacing = 8,
                MinHeight = 45,
                Margin = new Thickness(8, 0),
            };
            var pick = new CheckBox { IsChecked = _picked.Contains(model), Classes = { "source-model" }, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(pick, MessageCatalog.Get("composer.enable_name", _language, ("name", model)));
            row.Children.Add(pick);
            var name = new TextBlock { Text = model, Classes = { "source-heading" }, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Bind(TextBlock.FontFamilyProperty, name.GetResourceObservable("WorkbenchMonoFamily"));
            var available = (string?)_checks.GetValueOrDefault(model)?["status"];
            var origin = Text(_manual.Contains(model) ? "composer.added_manually" : "composer.endpoint_catalog");
            var evidence = Text(available switch
            {
                "available" => "composer.verified_available",
                "unavailable" => "composer.currently_unavailable",
                _ => "composer.not_verified",
            });
            var note = new TextBlock { Text = origin + "  •  " + evidence, Classes = { "source-tip" } };
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, note } };
            Grid.SetColumn(info, 1);
            row.Children.Add(info);
            var test = new Button
            {
                Content = Text(available is null ? "composer.verify" : "composer.verify_again"),
                Classes = { "provider-action", "source-verify" },
                IsEnabled = pick.IsChecked == true && !_busy && !string.IsNullOrWhiteSpace(Endpoint.Text),
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetAutomationId(test, "action:provider.model-check");
            AutomationProperties.SetName(test, MessageCatalog.Get("composer.verify_model_name", _language, ("name", model)));
            test.Click += async (_, _) => await TestAsync([model]).ConfigureAwait(true);
            pick.IsCheckedChanged += (_, _) =>
            {
                if (pick.IsChecked == true)
                {
                    _picked.Add(model);
                }
                else
                {
                    _picked.Remove(model);
                }

                test.IsEnabled = pick.IsChecked == true && !_busy && !string.IsNullOrWhiteSpace(Endpoint.Text);
                UpdateModelCount();
                UpdateState();
            };
            Grid.SetColumn(test, 2);
            row.Children.Add(test);
            var frame = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0) };
            frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("HairBrush"));
            ModelRows.Children.Add(frame);
        }

        AddModel.IsVisible = query.Length > 0 && !_models.Contains(query, StringComparer.OrdinalIgnoreCase);
        AddModelLabel.Text = MessageCatalog.Get("composer.add_unlisted_model_name", _language, ("name", query));
        UpdateModelCount();
    }

    /// <summary>Updates the selected and tested model counts without rebuilding focused rows.</summary>
    private void UpdateModelCount()
    {
        var count = _picked.Count.ToString(CultureInfo.CurrentCulture);
        EnabledCount.Text = MessageCatalog.Get("models.on_of_all_enabled", _language, ("on", count), ("all", _models.Count.ToString(CultureInfo.CurrentCulture)));
        TestModels.Content = MessageCatalog.Get("models.test_enabled_models_n", _language, ("n", count));
    }

    /// <summary>Adds and enables the identifier typed into search.</summary>
    private void AppendModel()
    {
        var model = ModelSearch.Text?.Trim() ?? string.Empty;
        if (model.Length == 0 || _models.Contains(model, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _models.Insert(0, model);
        _manual.Add(model);
        _picked.Add(model);
        ModelSearch.Text = string.Empty;
        DrawModels();
        UpdateState();
    }

    /// <summary>Reads the endpoint models without replacing the selected protocol or manual choices.</summary>
    /// <returns>The endpoint probe.</returns>
    private async Task ProbeAsync()
    {
        if (_busy || _api is null || string.IsNullOrWhiteSpace(Endpoint.Text))
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                var result = await _api.ProbeAsync(Endpoint.Text.Trim(), Credential.Text?.Trim() ?? string.Empty, _token).ConfigureAwait(true);
                if (result.BaseUrl is { Length: > 0 } completed)
                {
                    _normalizedAddress = completed;
                    Endpoint.Text = completed;
                }

                _probe = result;
                if (result.NoProxy)
                {
                    BypassProxy.IsChecked = true;
                }

                foreach (var model in result.Models.Where(model => !_models.Contains(model)).Distinct(StringComparer.Ordinal))
                {
                    _models.Add(model);
                }

                if (_picked.Count == 0)
                {
                    _picked.AddRange(result.Models.Distinct(StringComparer.Ordinal).Take(8));
                }

                if (string.IsNullOrWhiteSpace(SourceName.Text))
                {
                    SourceName.Text = SuggestedName(Host(Endpoint.Text));
                }

                ProbeStatus.Classes.Remove("source-issue");
                ProbeStatus.Classes.Add("success");
                ProbeMessage.Classes.Remove("error");
                ProbeFailure.IsVisible = false;
                ProbeStatus.Text = MessageCatalog.Get("models.connection_works_n_models_found", _language, ("n", result.Models.Length.ToString(CultureInfo.CurrentCulture)));
                ProbeMessage.IsVisible = true;
            },
            ProbeStatus).ConfigureAwait(true);
    }

    /// <summary>Checks one or all enabled models through the kernel's existing draft check route.</summary>
    /// <param name="models">Model IDs chosen by the user.</param>
    /// <returns>The batch of checks.</returns>
    private async Task TestAsync(string[] models)
    {
        if (_busy || _api is null || string.IsNullOrWhiteSpace(Endpoint.Text))
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                foreach (var model in models)
                {
                    _token.ThrowIfCancellationRequested();
                    var request = ConnectionDraft();
                    request["model"] = model;
                    _checks[model] = await _api.CheckDraftModelAsync(request, _token).ConfigureAwait(true);
                }
            },
            ProbeStatus).ConfigureAwait(true);
    }
}
