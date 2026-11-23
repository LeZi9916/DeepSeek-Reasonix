using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Shows community package details and the specific installation plan issued by the kernel.</summary>
public sealed partial class MarketInstall : SettingsFormView
{
    /// <summary>The package owner's host.</summary>
    private StudioPageClient _api = null!;

    /// <summary>The publisher-qualified package identity.</summary>
    private string _slug = string.Empty;

    /// <summary>Formats kernel refusals.</summary>
    private Func<Exception, string> _describe = null!;

    /// <summary>The return action for the retained discovery list.</summary>
    private Func<Task> _back = null!;

    /// <summary>Opens the installed capabilities from a successful result.</summary>
    private Func<string, string, Task>? _showInstalled;

    /// <summary>The destination of the installed-capabilities button.</summary>
    private (string Kind, string Name)? _installedTarget;

    /// <summary>The publisher's entry when reviewing an owned package.</summary>
    private JsonObject? _ownPackage;

    /// <summary>Locks the publisher tabs while an own-package apply is running.</summary>
    private Action<bool>? _applyingChanged;

    /// <summary>Current window request lifetime.</summary>
    private CancellationToken _token;

    /// <summary>Detail returned by the host.</summary>
    private JsonObject? _detail;

    /// <summary>Plan retained until the user installs or cancels.</summary>
    private JsonObject? _plan;

    /// <summary>Whether a read or install is in progress.</summary>
    private bool _busy;

    /// <summary>Whether this package has a different version installed.</summary>
    private bool _update;

    /// <summary>Whether a plan needs explicit trust in the publisher.</summary>
    private bool _trust;

    /// <summary>Whether the reader must acknowledge multiple skills.</summary>
    private bool _many;

    /// <summary>Initializes a new instance of the <see cref="MarketInstall"/> class.</summary>
    public MarketInstall()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ReviewFrame.Width = Math.Min(560, e.NewSize.Width);
        ListingFrame.SizeChanged += (_, e) =>
        {
            ListingSource.MaxWidth = Math.Max(0, e.NewSize.Width - 34);
            ListingName.MaxWidth = Math.Max(120, e.NewSize.Width - 185);
            FooterLayout.ColumnDefinitions = new ColumnDefinitions(e.NewSize.Width <= 560 ? "*,Auto" : "*,Auto,*");
            FooterLayout.ColumnDefinitions[0].MaxWidth = 480.25;
        };
    }

    /// <summary>Opens the selected marketplace package and optionally reads its pinned preview.</summary>
    /// <param name="api">Owning kernel.</param>
    /// <param name="slug">The selected package.</param>
    /// <param name="language">Settings language.</param>
    /// <param name="describe">Refusal formatter.</param>
    /// <param name="back">Returns to the retained catalog.</param>
    /// <param name="token">Window lifetime.</param>
    /// <param name="inspect">Whether the card's install shortcut requested a pinned preview.</param>
    /// <param name="signIn">Opens account settings.</param>
    /// <param name="showInstalled">Navigates to the installed result selected by the user.</param>
    /// <returns>The initial read.</returns>
    internal async Task OpenAsync(
        StudioPageClient api,
        string slug,
        InterfaceLanguage language,
        Func<Exception, string> describe,
        Func<Task> back,
        CancellationToken token,
        bool inspect = false,
        Func<Task>? signIn = null,
        Func<string, string, Task>? showInstalled = null)
    {
        Bind(api, slug, language, describe, back, token, signIn, showInstalled);
        await LoadAsync(false, signIn).ConfigureAwait(true);
        if (inspect && (bool?)_detail?["pinned"] == true && _detail?["installed"] is null && Inspect.IsEnabled && Inspect.IsVisible)
        {
            await ReviewAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Previews the signed-in publisher's own package through the account route.</summary>
    /// <param name="api">Owning kernel.</param>
    /// <param name="package">The publisher's inventory entry.</param>
    /// <param name="language">Settings language.</param>
    /// <param name="describe">Refusal formatter.</param>
    /// <param name="back">Returns to the publisher inventory.</param>
    /// <param name="applyingChanged">Locks navigation during the apply.</param>
    /// <param name="token">Window lifetime.</param>
    /// <param name="showInstalled">Navigates to an installed result.</param>
    /// <returns>The initial preview.</returns>
    internal async Task OpenOwnAsync(
        StudioPageClient api,
        JsonObject package,
        InterfaceLanguage language,
        Func<Exception, string> describe,
        Func<Task> back,
        Action<bool> applyingChanged,
        CancellationToken token,
        Func<string, string, Task>? showInstalled = null)
    {
        _ownPackage = package;
        _applyingChanged = applyingChanged;
        var installed = (string?)package["installed"]?["version"];
        _update = installed is not null && installed != (string?)package["latestVersion"];
        Bind(api, (string)package["slug"]!, language, describe, back, token, null, showInstalled);
        StatusBack.Content = Text("ext.back_to_my_packages");
        AutomationProperties.SetAutomationId(StatusRetry, "action:market.own-retry");
        await ReadOwnAsync().ConfigureAwait(true);
    }

    /// <summary>Formats a count using the current locale.</summary>
    /// <param name="value">Package or action count.</param>
    /// <returns>Localized number.</returns>
    private static string Number(int value) => value.ToString(CultureInfo.CurrentCulture);

    /// <summary>Connects the shared detail and review controls to their request owner.</summary>
    /// <param name="api">Owning kernel.</param>
    /// <param name="slug">Package identity.</param>
    /// <param name="language">Settings language.</param>
    /// <param name="describe">Refusal formatter.</param>
    /// <param name="back">Returns to the owning list.</param>
    /// <param name="token">Window lifetime.</param>
    /// <param name="signIn">Opens account settings.</param>
    /// <param name="showInstalled">Navigates to a selected installed result.</param>
    private void Bind(
        StudioPageClient api,
        string slug,
        InterfaceLanguage language,
        Func<Exception, string> describe,
        Func<Task> back,
        CancellationToken token,
        Func<Task>? signIn,
        Func<string, string, Task>? showInstalled)
    {
        _api = api;
        _slug = slug;
        _describe = describe;
        _back = back;
        _showInstalled = showInstalled;
        _token = token;
        Translate(language);
        AutomationProperties.SetName(CloseReview, Text("ext.close_install_confirmation"));
        ToolTip.SetTip(CloseReview, Text("ext.close_install_confirmation"));
        Back.Click += async (_, _) => await ReturnAsync().ConfigureAwait(true);
        StatusBack.Click += async (_, _) => await ReturnAsync().ConfigureAwait(true);
        ViewInstalled.Click += async (_, _) =>
        {
            if (!_busy && _installedTarget is { } target && _showInstalled is { } open)
            {
                await open(target.Kind, target.Name).ConfigureAwait(true);
            }
        };
        StatusRetry.Click += async (_, _) => await (_ownPackage is not null ? ReadOwnAsync() : LoadAsync(true, signIn)).ConfigureAwait(true);
        CacheRetry.Click += async (_, _) => await LoadAsync(true, signIn).ConfigureAwait(true);
        Cancel.Click += async (_, _) => await CancelReviewAsync().ConfigureAwait(true);
        CloseReview.Click += async (_, _) => await CancelReviewAsync().ConfigureAwait(true);
        Inspect.Click += async (_, _) => await ReviewAsync().ConfigureAwait(true);
        Acknowledge.IsCheckedChanged += (_, _) => UpdateActions();
        Install.Click += async (_, _) => await InstallAsync().ConfigureAwait(true);
    }

    /// <summary>Reads the publisher's own plan without accepting a source from the UI.</summary>
    /// <returns>The owned preview.</returns>
    private async Task ReadOwnAsync()
    {
        if (_busy)
        {
            return;
        }

        BeginRead("ext.previewing_what_would_be_installed");
        try
        {
            var request = new JsonObject { ["slug"] = _slug, ["replace"] = _update };
            var result = await _api.MarketOwnPlanAsync(false, request, _token).ConfigureAwait(true);
            if ((bool?)result["ok"] != true || string.IsNullOrEmpty((string?)result["planId"]) || string.IsNullOrEmpty((string?)result["version"])
                || string.IsNullOrEmpty((string?)result["contentDigest"]))
            {
                ShowReadFailure("ext.could_not_preview_name", (string?)result["error"] ?? (string?)result["next"] ?? Text("ext.there_is_nothing_installable_at"));
                return;
            }

            _plan = result;
            DrawReview(result);
        }
        catch (Exception error)
        {
            if (!_token.IsCancellationRequested)
            {
                ShowReadFailure("ext.could_not_preview_name", _describe(error));
            }
        }
        finally
        {
            _busy = false;
            UpdateActions();
        }
    }

    /// <summary>Loads the detail and keeps a failed read distinct from a failed preview.</summary>
    /// <param name="refresh">Whether to bypass the cached detail.</param>
    /// <param name="signIn">Opens account settings.</param>
    /// <returns>The detail read.</returns>
    private async Task LoadAsync(bool refresh, Func<Task>? signIn)
    {
        if (_busy)
        {
            return;
        }

        BeginRead("common.loading");
        try
        {
            _detail = await _api.MarketDetailAsync(_slug, refresh, _token).ConfigureAwait(true);
            DrawListing(signIn);
        }
        catch (Exception error)
        {
            if (!_token.IsCancellationRequested)
            {
                ShowReadFailure("ext.could_not_read_name", _describe(error));
            }
        }
        finally
        {
            _busy = false;
            UpdateActions();
        }
    }

    /// <summary>Shows the shared status page before reading a listing or plan.</summary>
    /// <param name="loading">Localized progress message key.</param>
    private void BeginRead(string loading)
    {
        _plan = null;
        ListingFrame.IsVisible = ReviewFrame.IsVisible = false;
        StatusSurface.IsVisible = true;
        OutcomeItems.Children.Clear();
        StatusBanner.BorderThickness = default;
        StatusHeading.Text = Text(loading);
        StatusCopy.IsVisible = StatusRetry.IsVisible = ViewInstalled.IsVisible = false;
        _busy = true;
        UpdateActions();
    }

    /// <summary>Leaves a failed read on the status surface with its retry action.</summary>
    /// <param name="heading">Localized failure heading key.</param>
    /// <param name="message">Refusal from the host.</param>
    private void ShowReadFailure(string heading, string message)
    {
        StatusBanner.BorderThickness = new Thickness(2, 0, 0, 0);
        StatusHeading.Text = Text(heading, ("name", _slug));
        StatusCopy.Text = message;
        StatusCopy.IsVisible = StatusRetry.IsVisible = true;
    }

    /// <summary>Draws the listing from the host's detail and approved source.</summary>
    /// <param name="signIn">Opens account settings when requested.</param>
    private void DrawListing(Func<Task>? signIn)
    {
        var detail = _detail!;
        var package = detail["package"]?.AsObject() ?? new JsonObject();
        var installed = (string?)detail["installed"]?["version"];
        var latest = (string?)package["latestVersion"];
        var pinned = (bool?)detail["pinned"] == true;
        var kind = (string?)package["kind"] ?? string.Empty;
        var cached = detail["cache"] as JsonObject;
        _trust = !pinned;
        _update = installed is not null && installed != latest;
        var current = installed is not null && !_update;
        var stuck = _update && kind == "skill";
        ListingName.Text = (string?)package["name"] ?? _slug;
        KindLabel.Text = Text(kind switch
        {
            "skill" => "settings.skills",
            "plugin" => "ext.plugins",
            "mcp" => "persona.mcp_servers",
            "theme" => "ext.theme",
            _ => kind,
        });
        VerifiedBadge.IsVisible = (bool?)package["verified"] == true;
        ListingSummary.Text = (string?)package["summary"];
        ListingSummary.IsVisible = !string.IsNullOrEmpty(ListingSummary.Text);
        ListingDescription.Text = (string?)package["description"];
        ListingDescription.IsVisible = !string.IsNullOrEmpty(ListingDescription.Text);
        UnpinnedAlert.IsVisible = !pinned && !current;
        var publisher = (string?)package["handle"] ?? _slug.Split('/')[0];
        UnpinnedCopy.Text = Text("ext.no_content_digest_was_recorded", ("handle", publisher));
        Publisher.Text = "@" + publisher;
        var version = (string?)detail["approved"]?["version"] ?? latest;
        VersionLabel.Text = "v" + version + (pinned ? "  ·  " + Text("common.pinned") : string.Empty);
        Downloads.Text = Text("ext.n_installs", ("n", Number((int?)package["installCount"] ?? 0)));
        ListingSource.Text = (string?)detail["approved"]?["source"] ?? (string?)package["repoUrl"];
        ListingSource.IsVisible = !string.IsNullOrEmpty(ListingSource.Text);
        CacheAlert.IsVisible = cached is not null;
        if (cached is not null)
        {
            var date = (string?)cached["cachedAt"] ?? string.Empty;
            if (DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
            {
                date = time.ToLocalTime().ToString(Language == InterfaceLanguage.Zh ? "M月d日 HH:mm" : "MMM d, HH:mm", CultureInfo.InvariantCulture);
            }

            var cacheKey = (string?)cached["cause"] == "unreachable" ? "ext.the_community_market_could_not" : "ext.the_community_market_is_not";
            CacheCopy.Text = Text(cacheKey, ("when", date));
        }

        Inspect.IsVisible = !current && !stuck;
        AutomationProperties.SetAutomationId(Inspect, _trust ? "action:market.trust" : "action:market.inspect");
        var noteKey = (cached is not null, current, stuck) switch
        {
            (true, _, _) => "ext.showing_cached_data_installing_needs",
            (_, true, _) => "ext.installed_version",
            (_, _, true) => "ext.a_skill_is_not_overwritten",
            _ => "ext.everything_it_would_install_is",
        };
        ListingNote.Text = Text(noteKey, ("version", installed ?? string.Empty));
        var vote = new MarketVoteView();
        VoteHost.Content = vote;
        _ = vote.OpenAsync(_api, _slug, package, Language, _describe, signIn, _token);
        DetailFailure.IsVisible = false;
        ListingFrame.IsVisible = true;
        ReviewFrame.IsVisible = StatusSurface.IsVisible = false;
    }

    /// <summary>Requests the preview while leaving the listing visible until the host returns a valid plan.</summary>
    /// <returns>The reviewed plan, if accepted by the host.</returns>
    private async Task ReviewAsync()
    {
        if (_busy || !Inspect.IsVisible || !Inspect.IsEnabled || _detail?["cache"] is not null)
        {
            return;
        }

        DetailFailure.IsVisible = false;
        _busy = true;
        UpdateActions();
        try
        {
            var request = new JsonObject { ["slug"] = _slug, ["replace"] = _update };
            if (_trust)
            {
                request["trust"] = true;
            }

            var result = await _api.MarketPlanAsync(false, request, _token).ConfigureAwait(true);
            if ((bool?)result["ok"] != true || string.IsNullOrEmpty((string?)result["planId"]) || string.IsNullOrEmpty((string?)result["version"])
                || ((bool?)result["unreviewed"] == true && (!_trust || string.IsNullOrEmpty((string?)result["contentDigest"]))))
            {
                ShowDetailFailure((string?)result["error"] ?? (string?)result["next"] ?? Text("ext.there_is_nothing_installable_at"));
                return;
            }

            _plan = result;
            DrawReview(result);
        }
        catch (Exception error)
        {
            if (!_token.IsCancellationRequested)
            {
                ShowDetailFailure(_describe(error));
            }
        }
        finally
        {
            _busy = false;
            UpdateActions();
        }
    }

    /// <summary>Installs only the exact plan currently visible and acknowledged by the user.</summary>
    /// <returns>The kernel result.</returns>
    private async Task InstallAsync()
    {
        if (_busy || _plan is not { } plan || !ReviewFrame.IsVisible || (_many && Acknowledge.IsChecked != true))
        {
            return;
        }

        _busy = true;
        _applyingChanged?.Invoke(true);
        ReviewFailure.IsVisible = false;
        UpdateActions();
        try
        {
            var request = new JsonObject
            {
                ["slug"] = _slug,
                ["replace"] = _update,
                ["version"] = (string?)plan["version"],
                ["planId"] = (string?)plan["planId"],
            };
            if (_ownPackage is not null)
            {
                request["digest"] = (string?)plan["contentDigest"];
            }
            else if ((bool?)plan["unreviewed"] == true)
            {
                request["trust"] = true;
                request["digest"] = (string?)plan["contentDigest"];
            }

            var apply = _ownPackage is null ? _api.MarketPlanAsync(true, request, _token) : _api.MarketOwnPlanAsync(true, request, _token);
            var result = await apply.ConfigureAwait(true);
            _plan = null;
            DrawOutcome(result);
        }
        catch (Exception error)
        {
            if (!_token.IsCancellationRequested)
            {
                ReviewFailure.Text = _describe(error);
                ReviewFailure.IsVisible = true;
            }
        }
        finally
        {
            _busy = false;
            _applyingChanged?.Invoke(false);
            UpdateActions();
        }
    }

    /// <summary>Returns to the original package details without applying its current plan.</summary>
    /// <returns>The optional return to the publisher's list.</returns>
    private Task CancelReviewAsync()
    {
        if (_busy)
        {
            return Task.CompletedTask;
        }

        _plan = null;
        if (_ownPackage is not null)
        {
            return ReturnAsync();
        }

        ManyAlert.IsVisible = false;
        ReviewFrame.IsVisible = false;
        ListingFrame.IsVisible = true;
        UpdateActions();
        return Task.CompletedTask;
    }

    /// <summary>Returns to the catalog only when no operation is in flight.</summary>
    /// <returns>The catalog transition.</returns>
    private Task ReturnAsync() => _busy ? Task.CompletedTask : _back();

    /// <summary>Sets the availability and captions that follow the current operation.</summary>
    private void UpdateActions()
    {
        var inspectKey = (_busy && ListingFrame.IsVisible, _trust, _update) switch
        {
            (true, _, _) => "ext.reading",
            (_, true, _) => "ext.trust_and_install",
            (_, _, true) => "ext.see_what_the_update_installs",
            _ => "ext.see_what_it_installs",
        };
        Inspect.Content = Text(inspectKey);
        Inspect.IsEnabled = !_busy && _detail?["cache"] is null;
        CacheRetry.IsEnabled = StatusRetry.IsEnabled = Back.IsEnabled = StatusBack.IsEnabled = !_busy;
        CloseReview.IsEnabled = Cancel.IsEnabled = Acknowledge.IsEnabled = !_busy;
        Install.IsEnabled = !_busy && _plan is not null && (!_many || Acknowledge.IsChecked == true);
        Install.Content = Text(_busy && ReviewFrame.IsVisible ? "ext.installing" : "ext.install");
    }

    /// <summary>Shows a preview failure at the original package instead of leaving its page.</summary>
    /// <param name="message">Kernel response.</param>
    private void ShowDetailFailure(string message)
    {
        DetailFailure.Text = message;
        DetailFailure.IsVisible = true;
    }
}
