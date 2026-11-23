using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The searchable community marketplace in the extension settings.</summary>
internal sealed partial class ExtensionMarketplaceView : UserControl
{
    /// <summary>The displayed result cards.</summary>
    private readonly List<MarketplaceCard> _cards = [];

    /// <summary>The corresponding package identities for deduplication across pages.</summary>
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);

    /// <summary>The selected capability and sorting options.</summary>
    private string _kind = string.Empty;

    /// <summary>The current marketplace sort order.</summary>
    private string _sort = "recommended";

    /// <summary>The page offset returned by the last host read.</summary>
    private int _offset;

    /// <summary>The generation guarding against outdated search responses.</summary>
    private int _generation;

    /// <summary>The runtime that owns the marketplace.</summary>
    private StudioPageClient? _api;

    /// <summary>The display language.</summary>
    private InterfaceLanguage _language;

    /// <summary>The lifetime of the window.</summary>
    private CancellationToken _token;

    /// <summary>Formats errors coming from the marketplace.</summary>
    private Func<Exception, string>? _describe;

    /// <summary>Opens a chosen package detail or preview.</summary>
    private Func<string, bool, Task>? _choose;

    /// <summary>The publisher handle on the current authenticated runtime.</summary>
    private string? _handle;

    /// <summary>The selected publisher view.</summary>
    private string _accountView = "browse";

    /// <summary>Opens the installed capability chosen from an installation receipt.</summary>
    private Func<string, string, Task>? _showInstalled;

    /// <summary>Controls navigation while a publisher write is in flight.</summary>
    private Action<bool>? _applyingChanged;

    /// <summary>Initializes a new instance of the <see cref="ExtensionMarketplaceView"/> class.</summary>
    public ExtensionMarketplaceView() => InitializeComponent();

    /// <summary>Opens the discovery surface with server-side filtering.</summary>
    /// <param name="api">Settings runtime.</param>
    /// <param name="language">Interface language.</param>
    /// <param name="describe">Error formatter.</param>
    /// <param name="choose">Package details and reviewed install preview.</param>
    /// <param name="signIn">Opens account settings.</param>
    /// <param name="token">Request lifetime.</param>
    /// <param name="showInstalled">Shows selected installed capability from a preview receipt.</param>
    /// <param name="applyingChanged">Locks the outer tabs while a publish or install is applying.</param>
    /// <returns>The initial inventory.</returns>
    internal async Task OpenAsync(
        StudioPageClient api,
        InterfaceLanguage language,
        Func<Exception, string> describe,
        Func<string, bool, Task> choose,
        Func<Task> signIn,
        CancellationToken token,
        Func<string, string, Task>? showInstalled = null,
        Action<bool>? applyingChanged = null)
    {
        _api = api;
        _language = language;
        _describe = describe;
        _choose = choose;
        _token = token;
        _showInstalled = showInstalled;
        _applyingChanged = applyingChanged;
        MarketTitle.Text = Text("ext.community_market");
        MarketHint.Text = Text("ext.skills_plugins_mcp_servers_and");
        AccountHint.Text = Text("ext.sign_in_to_publish_skills");
        SignIn.Content = Text("ext.sign_in");
        SignIn.Click += async (_, _) => await signIn().ConfigureAwait(true);
        Search.PlaceholderText = Text("ext.search_skills_plugins_mcp_servers");
        AutomationProperties.SetName(Search, Text("ext.search_the_community_market"));
        Search.TextChanged += async (_, _) =>
        {
            var id = ++_generation;
            try
            {
                await Task.Delay(250, token).ConfigureAwait(true);
                if (_generation == id)
                {
                    await ReloadAsync().ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The window was closed during the debounce.
            }
        };
        Pinned.Content = Text("ext.pinned_only");
        Pinned.IsCheckedChanged += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        AddOptions(
            KindOptions,
            "market.kind",
            _kind,
            value => _kind = value,
            [(string.Empty, "common.all"), ("skill", "settings.skills"), ("plugin", "ext.plugins"), ("mcp", "persona.mcp_servers"), ("theme", "ext.theme")]);
        AddOptions(
            SortOptions,
            "market.sort",
            _sort,
            value => _sort = value,
            [("recommended", "composer.recommended"), ("trending", "ext.trending"), ("installs", "ext.most_installed"), ("new", "ext.newest")]);
        More.Content = Text("ext.load_more");
        More.Click += async (_, _) => await ReadAsync(_offset).ConfigureAwait(true);
        Retry.Content = Text("common.retry");
        Retry.Click += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        SizeChanged += (_, e) => SetLayout(e.NewSize.Width);

        var account = await api.AccountAsync(token).ConfigureAwait(true);
        _handle = (bool?)account["signedIn"] == true ? (string?)account["user"]?["handle"] : null;
        AccountViews.IsVisible = !string.IsNullOrEmpty(_handle);
        SignInBar.IsVisible = !AccountViews.IsVisible;
        if (AccountViews.IsVisible)
        {
            ConfigureAccountTabs();
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Shows or closes the details while retaining the market heading and filter state.</summary>
    /// <param name="detail">The selected detail, or null for the listing.</param>
    internal void ShowDetail(Control? detail)
    {
        DetailHost.Content = detail;
        DetailHost.IsVisible = detail is not null;
        Browse.IsVisible = detail is null && _accountView == "browse";
    }

    /// <summary>Refreshes the current filter after a package detail is closed.</summary>
    /// <returns>The new first page.</returns>
    internal Task ReloadAsync() => ReadAsync(0);

    /// <summary>Connects the account-only view choices to their actual content.</summary>
    private void ConfigureAccountTabs()
    {
        AutomationProperties.SetName(AccountViews, Text("ext.community_market"));
        foreach (var (id, label) in new[] { ("browse", "ext.browse"), ("mine", "ext.my_packages"), ("publish", "ext.publish") })
        {
            var tab = new Button { Content = Text(label), Tag = id, Classes = { "market-option", "account-tab" } };
            AutomationProperties.SetAutomationId(tab, "action:market.view");
            tab.Classes.Set("active", id == _accountView);
            tab.Click += async (_, _) => await ShowAccountViewAsync(id).ConfigureAwait(true);
            tab.KeyDown += async (_, e) =>
            {
                if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End))
                {
                    return;
                }

                e.Handled = true;
                var backward = e.Key is Key.Left or Key.Up;
                var index = e.Key switch
                {
                    Key.Home => 0,
                    Key.End => AccountTabs.Children.Count - 1,
                    _ => (AccountTabs.Children.IndexOf(tab) + (backward ? AccountTabs.Children.Count - 1 : 1)) % AccountTabs.Children.Count,
                };
                if (AccountTabs.Children[index] is Button { Tag: string selected } next)
                {
                    await ShowAccountViewAsync(selected).ConfigureAwait(true);
                    next.Focus();
                }
            };
            AccountTabs.Children.Add(tab);
        }
    }

    /// <summary>Shows browsing, the publisher inventory, or a publish draft.</summary>
    /// <param name="id">Selected account view.</param>
    /// <param name="earlier">Optional earlier package for a new version.</param>
    /// <returns>Any initial inventory request.</returns>
    private async Task ShowAccountViewAsync(string id, JsonObject? earlier = null)
    {
        if (_api is not { } api || string.IsNullOrEmpty(_handle) || (id == _accountView && earlier is null) || !AccountTabs.IsEnabled)
        {
            return;
        }

        _accountView = id;
        foreach (var tab in AccountTabs.Children.OfType<Button>())
        {
            tab.Classes.Set("active", Equals(tab.Tag, id));
        }

        ShowDetail(null);
        AccountPane.IsVisible = id != "browse";
        if (id == "mine")
        {
            var mine = new MarketMineView();
            AccountPane.Content = mine;
            await mine.OpenAsync(
                api,
                _language,
                _describe!,
                package => _ = ShowAccountViewAsync("publish", package),
                _showInstalled,
                SetApplying,
                _token).ConfigureAwait(true);
        }
        else if (id == "publish")
        {
            var publish = new MarketPublishView();
            AccountPane.Content = publish;
            publish.Open(api, _language, _handle, earlier, _describe!, () => ShowAccountViewAsync("mine"), SetApplying, _token);
        }
        else
        {
            AccountPane.Content = null;
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Locks the tabs only for an active submission or installation.</summary>
    /// <param name="applying">Whether a write is being applied.</param>
    private void SetApplying(bool applying)
    {
        AccountTabs.IsEnabled = !applying;
        _applyingChanged?.Invoke(applying);
    }

    /// <summary>Lays out the filter bar and auto-fill grid at the current width.</summary>
    /// <param name="width">Available width of the marketplace.</param>
    private void SetLayout(double width)
    {
        var compact = width < 850;
        Filters.ColumnDefinitions = new ColumnDefinitions(compact ? "Auto,Auto,*,0" : "*,Auto,Auto,Auto");
        Grid.SetColumnSpan(Search, compact ? 4 : 1);
        Grid.SetColumn(Kinds, compact ? 0 : 1);
        Grid.SetColumnSpan(Kinds, compact ? 4 : 1);
        Grid.SetRow(Kinds, compact ? 1 : 0);
        Grid.SetColumn(Order, compact ? 0 : 2);
        Grid.SetRow(Order, compact ? 2 : 0);
        Grid.SetColumn(Pinned, compact ? 1 : 3);
        Grid.SetRow(Pinned, compact ? 2 : 0);
        Pinned.Margin = compact ? new Thickness(4, 0, 0, 0) : default;
        Filters.RowSpacing = compact ? 7 : 0;

        if (width <= 0)
        {
            return;
        }

        var columns = Math.Max(1, (int)Math.Floor((width + 12) / 252));
        var cardWidth = Math.Floor((width - ((columns - 1) * 12)) / columns);
        foreach (var card in _cards)
        {
            card.Width = cardWidth;
            card.MinHeight = columns == 1 ? 137.4 : 158.4;
            card.SetSingleColumn(columns == 1);
        }
    }

    /// <summary>Creates a single-choice filter without disabling its selected button.</summary>
    /// <param name="host">The segmented track.</param>
    /// <param name="action">Canonical action name.</param>
    /// <param name="active">The default selection.</param>
    /// <param name="select">Updates the corresponding query field.</param>
    /// <param name="items">Values and their message keys.</param>
    private void AddOptions(StackPanel host, string action, string active, Action<string> select, (string Value, string Label)[] items)
    {
        foreach (var (value, label) in items)
        {
            var option = new Button { Content = Text(label), Tag = value, Classes = { "market-option" } };
            option.Classes.Set("active", value == active);
            AutomationProperties.SetAutomationId(option, "action:" + action);
            option.Click += async (_, _) =>
            {
                select(value);
                foreach (var other in host.Children.OfType<Button>())
                {
                    other.Classes.Set("active", ReferenceEquals(option, other));
                }

                await ReloadAsync().ConfigureAwait(true);
            };
            host.Children.Add(option);
        }
    }

    /// <summary>Reads a catalog page and ignores results for replaced queries.</summary>
    /// <param name="offset">Read offset; zero replaces the current list.</param>
    /// <returns>The marketplace read.</returns>
    private async Task ReadAsync(int offset)
    {
        if (_api is not { } api || _token.IsCancellationRequested)
        {
            return;
        }

        var generation = ++_generation;
        Notice.IsVisible = true;
        Feedback.Text = Text("common.loading");
        Retry.IsVisible = false;
        More.IsVisible = false;
        if (offset == 0)
        {
            _cards.Clear();
            _shown.Clear();
            Cards.Children.Clear();
        }

        try
        {
            var result = await api.MarketSearchAsync(_kind, Search.Text?.Trim() ?? string.Empty, _sort, Pinned.IsChecked == true, offset, _token).ConfigureAwait(true);
            if (generation != _generation || _token.IsCancellationRequested)
            {
                return;
            }

            var page = result["packages"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
            var cache = result["cache"];
            foreach (var package in page.Where(item => _shown.Add((string?)item["slug"] ?? string.Empty)))
            {
                var card = new MarketplaceCard();
                card.Present(package, _language, cache is not null, _choose!);
                _cards.Add(card);
                Cards.Children.Add(card);
            }

            SetLayout(Bounds.Width);
            _offset = offset + page.Length;
            Notice.IsVisible = _cards.Count == 0 || cache is not null;
            if (cache is not null)
            {
                Feedback.Text = Text("ext.showing_cached_data");
                Retry.IsVisible = true;
            }
            else if (_cards.Count == 0)
            {
                Feedback.Text = Text(Pinned.IsChecked == true ? "ext.no_pinned_packages_found_turn" : "ext.no_matching_packages");
            }

            More.IsVisible = page.Length > 0 && page.Length >= ((int?)result["limit"] ?? 24);
        }
        catch (Exception error)
        {
            if (generation == _generation && !_token.IsCancellationRequested)
            {
                Notice.IsVisible = Retry.IsVisible = true;
                Feedback.Text = Text("ext.could_not_read_the_community") + " · " + _describe!(error);
            }
        }
    }

    /// <summary>Reads localized marketplace copy.</summary>
    /// <param name="key">Message key.</param>
    /// <returns>Localized caption.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);
}
