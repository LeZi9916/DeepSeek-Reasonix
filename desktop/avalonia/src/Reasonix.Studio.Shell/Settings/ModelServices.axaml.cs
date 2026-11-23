using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The model settings provider accounts and selected protocol.</summary>
public sealed partial class ModelServices : UserControl
{
    /// <summary>The visible account controls indexed by endpoint and credential slot.</summary>
    private readonly Dictionary<string, ProviderAccountRow> _rows = new(StringComparer.Ordinal);

    /// <summary>The protocol last chosen for each account.</summary>
    private readonly Dictionary<string, string> _protocols = new(StringComparer.Ordinal);

    /// <summary>The current kernel inventory used to identify sibling services.</summary>
    private JsonArray _inventory = [];

    /// <summary>The protocol catalog used by source and connection editors.</summary>
    private JsonArray _catalog = [];

    /// <summary>Accounts in the saved display order.</summary>
    private IReadOnlyList<ProviderServiceAccount> _accounts = [];

    /// <summary>Currently selected service.</summary>
    private string? _selected;

    /// <summary>Language used to label the settings.</summary>
    private InterfaceLanguage _language;

    /// <summary>The host that owns the provider inventory.</summary>
    private StudioPageClient? _api;

    /// <summary>Cancellation for the mounted settings host.</summary>
    private CancellationToken _token;

    /// <summary>Allows leaving an editor with unsaved changes.</summary>
    private Func<Task<bool>>? _mayLeave;

    /// <summary>Formats kernel failures for the settings window.</summary>
    private Func<Exception, string>? _describe;

    /// <summary>Current ordering of provider names.</summary>
    private IReadOnlyList<string> _order = [];

    /// <summary>Settings window whose size supplies the reference viewport breakpoint.</summary>
    private Window? _window;

    /// <summary>The outer settings viewport used by the sticky desktop rail.</summary>
    private ScrollViewer? _viewport;

    /// <summary>The current responsive layout.</summary>
    private bool _compact;

    /// <summary>Initializes a new instance of the <see cref="ModelServices"/> class.</summary>
    public ModelServices()
    {
        InitializeComponent();
        Add.Click += async (_, _) =>
        {
            if (ProviderDetail.Content is not ProviderSourceForm && await CanLeaveAsync().ConfigureAwait(true))
            {
                OpenSource();
            }
        };
        Search.TextChanged += (_, _) => DrawList();
    }

    /// <summary>Raised when the models offered in the usage table may have changed.</summary>
    internal event Action? UsageChanged;

    /// <summary>Raised after the visible account order changes.</summary>
    internal event Action<IReadOnlyList<string>>? OrderChanged;

    /// <summary>Raised when the selected protocol entry should be removed.</summary>
    internal event Action<string>? DeleteRequested;

    /// <summary>Gets accounts in the same order as the configured service rail.</summary>
    internal IReadOnlyList<ProviderServiceAccount> UsageAccounts => _accounts;

    /// <summary>Gets the explicitly saved service order.</summary>
    internal IReadOnlyList<string> UsageOrder => _order;

    /// <summary>Gets a value indicating whether the current sheet has edits.</summary>
    internal bool HasChanges => ProviderDetail.Content is ProviderDetailSheet { HasChanges: true };

    /// <summary>Projects the provider inventory and opens the current account.</summary>
    /// <param name="providers">Authoritative provider inventory.</param>
    /// <param name="language">The settings language.</param>
    /// <param name="api">Provider commands.</param>
    /// <param name="token">Cancels the provider commands.</param>
    /// <param name="order">Saved provider order.</param>
    /// <param name="mayLeave">The settings window's unsaved draft confirmation.</param>
    /// <param name="describe">Localized error description.</param>
    internal void Show(
        JsonArray providers,
        InterfaceLanguage language,
        StudioPageClient api,
        CancellationToken token,
        IReadOnlyList<string> order,
        Func<Task<bool>> mayLeave,
        Func<Exception, string> describe)
    {
        _language = language;
        _api = api;
        _token = token;
        _order = order;
        _mayLeave = mayLeave;
        _describe = describe;
        AddLabel.Text = Text("models.add_model_service");
        Search.PlaceholderText = Text("models.search_added_services");
        AutomationProperties.SetName(Search, Search.PlaceholderText);
        Load(providers);
        _ = ReadProtocolsAsync(api, token);
    }

    /// <summary>Gets an explicitly chosen protocol for an account.</summary>
    /// <param name="key">Account identity.</param>
    /// <returns>The selected wire protocol when one was chosen.</returns>
    internal string? PreferredProtocol(string key) => _protocols.GetValueOrDefault(key);

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            _window = window;
            window.SizeChanged += Resize;
            SetLayout(window.ClientSize);
        }

        _viewport = this.FindAncestorOfType<ScrollViewer>();
        if (_viewport is not null)
        {
            _viewport.ScrollChanged += UpdateRail;
            LayoutUpdated += UpdateRail;
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.SizeChanged -= Resize;
            _window = null;
        }

        if (_viewport is not null)
        {
            _viewport.ScrollChanged -= UpdateRail;
            LayoutUpdated -= UpdateRail;
            _viewport = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Uses the same breakpoint and list height as the Electron settings viewport.</summary>
    /// <param name="sender">Settings window.</param>
    /// <param name="e">Updated size.</param>
    private void Resize(object? sender, SizeChangedEventArgs e) => SetLayout(_window?.ClientSize ?? e.NewSize);

    /// <summary>Places the rail beside or above the detail pane.</summary>
    /// <param name="size">The settings viewport size.</param>
    private void SetLayout(Size size)
    {
        _compact = size.Width <= 860;
        Split.ColumnDefinitions[0].Width = _compact ? GridLength.Star : new GridLength(224);
        Split.ColumnDefinitions[1].Width = _compact ? new GridLength(0) : GridLength.Star;
        Split.ColumnSpacing = _compact ? 0 : 20;
        Split.RowSpacing = _compact ? 20 : 0;
        Grid.SetColumn(ProviderDetail, _compact ? 0 : 1);
        Grid.SetRow(ProviderDetail, _compact ? 1 : 0);
        ProviderViewport.MaxHeight = _compact ? 240 : Math.Clamp(size.Height * 0.6, 49, 520);
        if (ProviderDetail.Content is ProviderDetailSheet sheet)
        {
            sheet.SetCompact(_compact);
        }
    }

    /// <summary>Keeps the wide rail beside its service form during scrolling.</summary>
    /// <param name="sender">The viewport or the service split.</param>
    /// <param name="e">The scroll or layout update.</param>
    private void UpdateRail(object? sender, EventArgs e)
    {
        var transform = (TranslateTransform)ProviderRail.RenderTransform!;
        if (_compact || _viewport is not { } viewport || ProviderRail.TranslatePoint(default, viewport) is not { } position)
        {
            transform.Y = 0;
            return;
        }

        var travel = Math.Max(0, Split.Bounds.Height - ProviderRail.Bounds.Height);
        transform.Y = Math.Clamp(24 - (position.Y - transform.Y), 0, travel);
    }

    /// <summary>Regroups a fresh inventory without forgetting the active account.</summary>
    /// <param name="providers">Provider inventory.</param>
    /// <param name="preferAdded">Whether to select a previously unlisted account.</param>
    private void Load(JsonArray providers, bool preferAdded = false)
    {
        var old = _accounts;
        _inventory = providers;
        _accounts = ProviderServiceAccount.Group(providers, _order);
        if (preferAdded && _accounts.FirstOrDefault(account => old.All(previous => previous.Key != account.Key)) is { } added)
        {
            _selected = added.Key;
        }

        if (!_accounts.Any(account => account.Key == _selected))
        {
            _selected = (_accounts.FirstOrDefault(account => account.InUse) ?? _accounts.FirstOrDefault())?.Key;
        }

        Search.IsVisible = _accounts.Count >= 6;
        DrawList();
        DrawDetail();
        UsageChanged?.Invoke();
    }

    /// <summary>Updates the visible rails and leaves the active form mounted.</summary>
    private void DrawList()
    {
        _rows.Clear();
        ProviderList.Children.Clear();
        var query = Search.IsVisible ? Search.Text?.Trim() ?? string.Empty : string.Empty;
        var shown = _accounts.Where(account => account.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || account.Host.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (shown.Length == 0)
        {
            ProviderList.Children.Add(new TextBlock
            {
                Text = Text(_accounts.Count == 0 ? "models.no_model_sources_configured_yet" : "models.no_matching_service"),
                Classes = { "provider-caption" },
                Margin = new Thickness(10, 8),
            });
        }

        for (var i = 0; i < shown.Length; i++)
        {
            var account = shown[i];
            var row = new ProviderAccountRow { Tag = account.Key };
            row.Show(account, _language, (query.Length == 0 && i > 0, query.Length == 0 && i < shown.Length - 1));
            row.SetSelected(account.Key == _selected && ProviderDetail.Content is not ProviderSourceForm);
            row.OnActions(
                async () =>
                {
                    if ((account.Key != _selected || ProviderDetail.Content is ProviderSourceForm) && await CanLeaveAsync().ConfigureAwait(true))
                    {
                        _selected = account.Key;
                        Add.Classes.Remove("active");
                        foreach (var (key, member) in _rows)
                        {
                            member.SetSelected(key == _selected);
                        }

                        DrawDetail();
                    }
                },
                () => Rename(account),
                direction =>
                {
                    if (query.Length == 0)
                    {
                        Move(account, direction);
                    }
                });
            ProviderList.Children.Add(row);
            _rows.Add(account.Key, row);
        }
    }

    /// <summary>Replaces the service details with the inline add-source form.</summary>
    private void OpenSource()
    {
        if (_api is not { } api)
        {
            return;
        }

        var form = new ProviderSourceForm();
        ProviderDetail.Content = form;
        Add.Classes.Add("active");
        foreach (var row in _rows.Values)
        {
            row.SetSelected(false);
        }

        form.Open(api, _inventory, _catalog, _language, _token, _describe!, SourceAddedAsync, CloseSource);
    }

    /// <summary>Restores the selected account when the inline form closes.</summary>
    private void CloseSource()
    {
        Add.Classes.Remove("active");
        DrawDetail();
        foreach (var (key, row) in _rows)
        {
            row.SetSelected(key == _selected);
        }
    }

    /// <summary>Restores the service list and reads the accepted source from the kernel.</summary>
    /// <returns>The provider readback.</returns>
    private async Task SourceAddedAsync()
    {
        CloseSource();
        try
        {
            if (_api is { } api)
            {
                Load(await api.ProvidersAsync(_token).ConfigureAwait(true), preferAdded: true);
            }
        }
        catch (Exception error)
        {
            if (!_token.IsCancellationRequested)
            {
                Errors.Text = _describe!(error);
                Errors.IsVisible = true;
            }
        }
    }

    /// <summary>Mounts a sheet for the selected account and its chosen protocol.</summary>
    private void DrawDetail()
    {
        var account = _accounts.FirstOrDefault(item => item.Key == _selected);
        if (account is null || _api is not { } api)
        {
            ProviderDetail.Content = new TextBlock
            {
                Text = Text("models.add_a_model_service_to"),
                Classes = { "provider-caption" },
                Margin = new Thickness(10, 8),
            };
            return;
        }

        var entry = _protocols.TryGetValue(account.Key, out var preferred) && account.Entries.TryGetValue(preferred, out var picked)
            ? picked
            : account.Entries.Values.FirstOrDefault(value => (bool?)value["inUse"] == true) ?? account.Entries.Values.First();
        var sheet = new ProviderDetailSheet();
        sheet.SetCompact(_compact);
        sheet.Show(
            account,
            entry,
            _language,
            api,
            _token,
            async protocol =>
            {
                if (protocol != ProviderServiceAccount.Kind(entry) && await CanLeaveAsync().ConfigureAwait(true))
                {
                    _protocols[account.Key] = protocol;
                    DrawDetail();
                    UsageChanged?.Invoke();
                }
            },
            () => Rename(account),
            async () =>
            {
                if (await CanLeaveAsync().ConfigureAwait(true))
                {
                    DeleteRequested?.Invoke(ProviderServiceAccount.Name(entry));
                }
            },
            RefreshAsync,
            DrawList,
            _describe!);
        sheet.OfferProtocols(_catalog, _language);
        ProviderDetail.Content = sheet;
    }

    /// <summary>Reads the host's optional protocol catalog without blocking the current form.</summary>
    /// <param name="api">Provider commands.</param>
    /// <param name="token">Cancels the catalog read.</param>
    /// <returns>The catalog read and projection.</returns>
    private async Task ReadProtocolsAsync(StudioPageClient api, CancellationToken token)
    {
        try
        {
            _catalog = await api.ProtocolsAsync(token).ConfigureAwait(true);
            if (ProviderDetail.Content is ProviderDetailSheet sheet)
            {
                sheet.OfferProtocols(_catalog, _language);
            }
            else if (ProviderDetail.Content is ProviderSourceForm form)
            {
                form.SetProtocols(_catalog);
            }
        }
        catch (Exception error) when (error is KernelRefusal or System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            // Older kernels omit this catalog. The configured protocol is still editable through its existing fields.
        }
    }

    /// <summary>Edits the display name of all configured protocols in the rail.</summary>
    /// <param name="account">Account from the current inventory.</param>
    private void Rename(ProviderServiceAccount account)
    {
        if (_api is not { } api || !_rows.TryGetValue(account.Key, out var row))
        {
            return;
        }

        row.BringIntoView();
        row.BeginRename(account.DisplayName ?? account.DefaultLabel, account.DefaultLabel, async value =>
        {
            Errors.IsVisible = false;
            try
            {
                var names = account.Entries.Values.Select(ProviderServiceAccount.Name).ToArray();
                await api.SetProviderDisplayNameAsync(names, value == account.DefaultLabel ? string.Empty : value, _token).ConfigureAwait(true);
                var latest = await api.ProvidersAsync(_token).ConfigureAwait(true);
                if (HasChanges)
                {
                    _accounts = ProviderServiceAccount.Group(latest, _order);
                    DrawList();
                    UsageChanged?.Invoke();
                }
                else
                {
                    Load(latest);
                }
            }
            catch (Exception error) when (!_token.IsCancellationRequested)
            {
                Errors.Text = _describe!(error);
                Errors.IsVisible = true;
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested)
            {
                return;
            }
        });
    }

    /// <summary>Moves a grouped service without interleaving its protocols with another account.</summary>
    /// <param name="account">The service to move.</param>
    /// <param name="direction">One position up or down.</param>
    private void Move(ProviderServiceAccount account, int direction)
    {
        var accounts = _accounts.ToList();
        var at = accounts.IndexOf(account);
        var neighbor = at + direction;
        if (neighbor < 0 || neighbor >= accounts.Count)
        {
            return;
        }

        (accounts[at], accounts[neighbor]) = (accounts[neighbor], accounts[at]);
        _accounts = accounts;
        _order = accounts.SelectMany(item => item.Entries.Values.Select(ProviderServiceAccount.Name)).ToArray();
        OrderChanged?.Invoke(_order);
        UsageChanged?.Invoke();
        DrawList();
        _rows[account.Key].FocusAccount();
    }

    /// <summary>Rereads provider data after the kernel acknowledges a change.</summary>
    /// <returns>The refreshed inventory.</returns>
    private async Task RefreshAsync()
    {
        if (_api is { } api)
        {
            Load(await api.ProvidersAsync(_token).ConfigureAwait(true));
        }
    }

    /// <summary>Checks whether navigation can leave the current editor.</summary>
    /// <returns>True if navigation may proceed.</returns>
    private Task<bool> CanLeaveAsync() => HasChanges && _mayLeave is { } confirm ? confirm() : Task.FromResult(true);

    /// <summary>Reads a provider setting caption in the window language.</summary>
    /// <param name="key">Localized message key.</param>
    /// <returns>The caption.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);
}
