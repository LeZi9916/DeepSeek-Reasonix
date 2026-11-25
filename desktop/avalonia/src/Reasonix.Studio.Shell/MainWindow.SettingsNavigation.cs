using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Owns the navigation state of the settings surface.</summary>
    private readonly SettingsNavigation _settings = new();

    /// <summary>Connects searchable controls, keyboard navigation and the leave prompt.</summary>
    private void InitializeSettingsSearch()
    {
        SettingsPanel.SizeChanged += (_, e) => SettingsPanel.Classes.Set("compact", e.NewSize.Width <= 760);
        SettingsNav.AddHandler(PointerWheelChangedEvent, ScrollCompactSettingsNav, RoutingStrategies.Tunnel);
        SettingsScroller.PointerWheelChanged += (_, _) => CancelSettingsScroll();
        SettingsScroller.ScrollChanged += (_, _) => PinSettingsIndex();
        SettingsHeadingBlock.SizeChanged += (_, _) => PinSettingsIndex();
        SettingsSearch.TextChanged += (_, _) => ShowSettingsHits();
        SettingsSearch.AddHandler(KeyDownEvent, OnSettingsSearchKey, RoutingStrategies.Tunnel);
        SettingsHits.Tapped += async (_, e) =>
        {
            if (e.Source is Avalonia.Visual visual && (visual as ListBoxItem ?? visual.FindAncestorOfType<ListBoxItem>())?.Content is SettingsNavigation.SettingHit hit)
            {
                SettingsHits.SelectedItem = hit;
                await OpenSettingHitAsync().ConfigureAwait(true);
            }
        };
        SettingsHits.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await OpenSettingHitAsync().ConfigureAwait(true);
            }
        };
        KeepSettings.Click += (_, _) => _settings.ResolveConfirmation(false);
        DismissLeavePrompt.Click += (_, _) => _settings.ResolveConfirmation(false);
        SettingsLeavePrompt.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _settings.ResolveConfirmation(false);
            }
        };
        DiscardSettings.Click += (_, _) => _settings.ResolveConfirmation(true);
    }

    /// <summary>Scrolls the compact tab strip horizontally with a vertical mouse wheel.</summary>
    /// <param name="sender">The settings navigation control.</param>
    /// <param name="e">The wheel delta.</param>
    private void ScrollCompactSettingsNav(object? sender, PointerWheelEventArgs e)
    {
        if (!SettingsPanel.Classes.Contains("compact") || e.Delta.X != 0 || e.Delta.Y == 0 ||
            SettingsNav.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroll)
        {
            return;
        }

        scroll.Offset -= new Vector(e.Delta.Y * scroll.SmallChange.Width * 3, 0);
        e.Handled = true;
    }

    /// <summary>Offers sections and matching settings in the current language.</summary>
    private void ShowSettingsHits()
    {
        var query = SettingsSearch.Text?.Trim() ?? string.Empty;
        if (query.Length == 0 || !SettingsPanel.IsVisible)
        {
            SearchResultsPanel.IsVisible = false;
            return;
        }

        var wasVisible = SearchResultsPanel.IsVisible;

        var hits = SettingsNavigation.Search(query, Language, SettingAvailable);
        SettingsHits.ItemsSource = hits;
        SettingsHits.SelectedIndex = hits.Count == 0 ? -1 : 0;
        SettingsHits.Classes.Remove("keyboard-search");
        SettingsHits.IsVisible = hits.Count != 0;
        NoSettingsHits.IsVisible = hits.Count == 0;
        NoSettingsHits.Text = MessageCatalog.Get("settings.no_matching_setting", Language);
        SearchResultsPanel.IsVisible = true;
        if (!wasVisible)
        {
            EnterSearchResults();
        }
    }

    /// <summary>Tests whether the local host exposes this setting.</summary>
    /// <param name="key">The setting's canonical key.</param>
    /// <returns>Whether search can open the setting.</returns>
    private bool SettingAvailable(string key) => key != "settings.global_show_hide_shortcut" || WindowShortcut is not null;

    /// <summary>Search owns its keys while its result list is open.</summary>
    /// <param name="sender">The search box.</param>
    /// <param name="e">Pressed key.</param>
    private async void OnSettingsSearchKey(object? sender, KeyEventArgs e)
    {
        if (!SearchResultsPanel.IsVisible)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            DismissSettingsSearch();
        }
        else if (e.Key is Key.Up or Key.Down && SettingsHits.ItemCount > 0)
        {
            var n = SettingsHits.ItemCount;
            SettingsHits.SelectedIndex = (SettingsHits.SelectedIndex + (e.Key == Key.Up ? n - 1 : 1)) % n;
            SettingsHits.Classes.Add("keyboard-search");
            SettingsHits.ScrollIntoView(SettingsHits.SelectedIndex);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await OpenSettingHitAsync().ConfigureAwait(true);
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    /// <summary>Clears the query and returns keyboard focus to the search box.</summary>
    private void DismissSettingsSearch()
    {
        SettingsSearch.Text = string.Empty;
        SearchResultsPanel.IsVisible = false;
        SettingsSearch.Focus();
    }

    /// <summary>Opens the result's real section and brings the corresponding setting into view.</summary>
    /// <returns>The section read.</returns>
    private async Task OpenSettingHitAsync()
    {
        if (SettingsHits.SelectedItem is not SettingsNavigation.SettingHit hit)
        {
            return;
        }

        SettingsSearch.Text = string.Empty;
        SearchResultsPanel.IsVisible = false;
        if (_settings.Section != hit.Section)
        {
            await ShowSectionAsync(hit.Section).ConfigureAwait(true);
        }

        if (_settings.Section != hit.Section || !SettingsPanel.IsVisible)
        {
            return;
        }

        SelectSettingsNav(hit.Section.Split(':')[0]);
        var anchor = hit.Key switch
        {
            "common.default_model" => "settings.models_by_use",
            "ext.installed" => "settings.plugin_packages",
            "onboarding.connect_a_model_service" => "common.model_services",
            _ => hit.Key,
        };
        var target = SettingsBody.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => Equals(c.Tag, anchor));
        if (target is not null)
        {
            FocusSetting(target);
        }
    }

    /// <summary>Selects the owning tab without starting a second request.</summary>
    /// <param name="id">Top-level section id.</param>
    private void SelectSettingsNav(string id)
    {
        _settings.Selecting = true;
        try
        {
            SettingsNav.SelectedItem = SettingsNav.Items.OfType<SectionEntry>().FirstOrDefault(item => item.Id == id);
        }
        finally
        {
            _settings.Selecting = false;
        }
    }

    /// <summary>Preserves a pending edit until the reader explicitly leaves.</summary>
    /// <returns>Whether navigation should proceed.</returns>
    private async Task<bool> MayLeaveSettingsAsync()
    {
        if (!_settings.HasEdits)
        {
            return true;
        }

        if (_settings.IsConfirming)
        {
            return false;
        }

        var decision = _settings.BeginConfirmation();
        LeaveQuestion.Text = MessageCatalog.Get("persona.discard_unsaved_changes", Language);
        LeaveDetail.Text = MessageCatalog.Get("common.they_will_be_lost_if", Language);
        KeepSettings.Content = MessageCatalog.Get("persona.keep_editing", Language);
        AutomationProperties.SetName(DismissLeavePrompt, MessageCatalog.Get("common.close", Language));
        LeaveChanges.Children.Clear();
        foreach (var change in _settings.Changes?.Invoke() ?? [])
        {
            LeaveChanges.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 2),
                [!Border.BackgroundProperty] = StudioTypography.Resource("OverlayBrush"),
                Child = new TextBlock { Text = change, [!TextBlock.FontSizeProperty] = StudioTypography.Size(11) },
            });
        }

        LeaveChanges.IsVisible = LeaveChanges.Children.Count > 0;
        DiscardSettings.Content = MessageCatalog.Get("persona.discard_changes", Language);
        var previous = FocusManager?.GetFocusedElement();
        SettingsSheet.IsEnabled = false;
        SettingsTitleBar.IsEnabled = false;
        SearchResultsPanel.IsVisible = false;
        SettingsLeavePrompt.IsVisible = true;
        KeepSettings.Focus();
        try
        {
            return await decision.ConfigureAwait(true);
        }
        finally
        {
            SettingsLeavePrompt.IsVisible = false;
            SettingsSheet.IsEnabled = true;
            SettingsTitleBar.IsEnabled = true;
            previous?.Focus();
        }
    }
}
