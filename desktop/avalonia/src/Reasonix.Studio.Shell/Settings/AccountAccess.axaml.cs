using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The active session host's account, read and changed through its device grant.</summary>
internal sealed partial class AccountAccess : UserControl
{
    /// <summary>The authenticated runtime client that owns this account.</summary>
    private readonly StudioPageClient _api;

    /// <summary>The display language for account feedback.</summary>
    private readonly InterfaceLanguage _language;

    /// <summary>Opens verification in the system browser.</summary>
    private readonly Func<string, Task> _openBrowser;

    /// <summary>Translates the host's refusal.</summary>
    private readonly Func<Exception, string> _describe;

    /// <summary>Stops work when its settings page leaves the screen.</summary>
    private readonly CancellationToken _lifetime;

    /// <summary>The device request the reader can cancel.</summary>
    private CancellationTokenSource? _login;

    /// <summary>Whether the reader has asked to sign out once.</summary>
    private bool _confirm;

    /// <summary>Initializes a new instance of the <see cref="AccountAccess"/> class.</summary>
    /// <param name="api">The owning runtime.</param><param name="language">The display language.</param>
    /// <param name="openBrowser">Opens the verified HTTP(S) URL.</param><param name="describe">Translates host errors.</param>
    /// <param name="lifetime">Ends reads and device polling on departure.</param>
    internal AccountAccess(StudioPageClient api, InterfaceLanguage language, Func<string, Task> openBrowser, Func<Exception, string> describe, CancellationToken lifetime)
    {
        _api = api;
        _language = language;
        _openBrowser = openBrowser;
        _describe = describe;
        _lifetime = lifetime;
        InitializeComponent();
        Checking.Text = Text("settings.checking_sign_in_status");
        UnreadTitle.Text = Text("settings.sign_in_status_could_not");
        Retry.Content = RetryOffline.Content = Text("common.retry");
        SignIn.Content = Text("common.sign_in");
        SignOut.Content = Text("settings.sign_out");
        Cancel.Content = Text("common.cancel");
        SignedOutNote.Text = Text("settings.used_for_posting_to_the");
        SignedInNote.Text = Text("settings.clears_the_local_credential_only");
        ExpiredNote.Text = Text("settings.the_previous_sign_in_has");
        Retry.Click += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        RetryOffline.Click += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        SignIn.Click += async (_, _) => await StartSignInAsync().ConfigureAwait(true);
        SignOut.Click += async (_, _) => await EndSignInAsync().ConfigureAwait(true);
        SignOut.PointerExited += (_, _) => Confirm(false);
        Cancel.Click += (_, _) => _login?.Cancel();
    }

    /// <summary>Reads the account without mistaking a failed read for a signed-out user.</summary>
    /// <returns>The current read.</returns>
    internal async Task ReloadAsync()
    {
        Retry.IsEnabled = RetryOffline.IsEnabled = false;
        Confirm(false);
        SetFailure(null);
        try
        {
            var state = await _api.AccountAsync(_lifetime).ConfigureAwait(true);
            if (!_lifetime.IsCancellationRequested)
            {
                Draw(state);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Checking.IsVisible = SignedOut.IsVisible = SignedIn.IsVisible = false;
            UnreadReason.Text = _describe(error);
            Unread.IsVisible = true;
        }
        finally
        {
            Retry.IsEnabled = RetryOffline.IsEnabled = true;
        }
    }

    /// <summary>Shows signed-in, expired and inactive states from the host.</summary>
    /// <param name="state">The authoritative account response.</param>
    private void Draw(JsonObject state)
    {
        Checking.IsVisible = Unread.IsVisible = false;
        SignedIn.IsVisible = (bool?)state["signedIn"] == true;
        SignedOut.IsVisible = !SignedIn.IsVisible;
        ExpiredNote.IsVisible = (bool?)state["expired"] == true;
        var user = state["user"];
        DisplayName.Text = new[] { (string?)user?["label"], (string?)user?["handle"], (string?)user?["email"] }
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? Text("settings.signed_in");
        Email.Text = (string?)user?["email"];
        Email.IsVisible = !string.IsNullOrWhiteSpace(Email.Text);
        var offline = (string?)state["error"];
        Offline.IsVisible = !string.IsNullOrWhiteSpace(offline);
        OfflineReason.Text = Offline.IsVisible ? Text("settings.cannot_reach_the_identity_service") + "：" + offline : null;
    }

    /// <summary>Shows the user code and polls only while the grant and page remain active.</summary>
    /// <returns>The current sign-in attempt.</returns>
    private async Task StartSignInAsync()
    {
        if (_login is not null)
        {
            return;
        }

        SetFailure(null);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        using var expiry = new CancellationTokenSource();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token, expiry.Token);
        _login = attempt;
        SignIn.Content = Text("settings.opening");
        SignIn.IsEnabled = false;
        try
        {
            var grant = await _api.BeginAccountLoginAsync(request.Token).ConfigureAwait(true);
            request.Token.ThrowIfCancellationRequested();
            var complete = (string?)grant["verificationUriComplete"];
            var address = !string.IsNullOrEmpty(complete) ? complete : (string?)grant["verificationUri"];
            var uri = Uri.TryCreate(address, UriKind.Absolute, out var verified) && verified.Scheme is "https" or "http" ? verified : null;
            if (string.IsNullOrWhiteSpace((string?)grant["deviceCode"]) || string.IsNullOrWhiteSpace((string?)grant["userCode"]) || uri is null)
            {
                SetFailure(Text("settings.this_sign_in_request_expired"));
                return;
            }

            expiry.CancelAfter(TimeSpan.FromSeconds(Math.Max(0, (int?)grant["expiresIn"] ?? 0)));
            UserCode.Text = (string?)grant["userCode"];
            DeviceHint.Text = MessageCatalog.Get("settings.type_this_code_into_the", _language, ("uri", uri.AbsoluteUri));
            SignIn.IsVisible = false;
            Device.IsVisible = true;
            await _openBrowser(uri.AbsoluteUri).ConfigureAwait(true);
            var interval = TimeSpan.FromSeconds(Math.Max(1, (int?)grant["interval"] ?? 1));
            while (!request.IsCancellationRequested)
            {
                await Task.Delay(interval, request.Token).ConfigureAwait(true);
                var result = await _api.PollAccountLoginAsync((string)grant["deviceCode"]!, request.Token).ConfigureAwait(true);
                request.Token.ThrowIfCancellationRequested();
                if ((string?)result["status"] == "complete")
                {
                    await ReloadAsync().ConfigureAwait(true);
                    return;
                }

                if ((bool?)result["slowDown"] == true)
                {
                    interval += TimeSpan.FromSeconds(5);
                }
            }

            request.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            if (expiry.IsCancellationRequested && !attempt.IsCancellationRequested)
            {
                SetFailure(Text("settings.this_sign_in_request_expired"));
            }
        }
        catch (Exception error)
        {
            if (!attempt.IsCancellationRequested)
            {
                SetFailure(_describe(error));
            }
        }
        finally
        {
            _login = null;
            UserCode.Text = DeviceHint.Text = null;
            Device.IsVisible = false;
            SignIn.IsVisible = SignIn.IsEnabled = true;
            SignIn.Content = Text("common.sign_in");
        }
    }

    /// <summary>Signs out only after a second explicit activation.</summary>
    /// <returns>The current sign-out attempt.</returns>
    private async Task EndSignInAsync()
    {
        if (!_confirm)
        {
            Confirm(true);
            return;
        }

        Confirm(false);
        SetFailure(null);
        SignOut.IsEnabled = false;
        try
        {
            await _api.AccountLogoutAsync(_lifetime).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            SetFailure(_describe(error));
        }
        finally
        {
            SignOut.IsEnabled = true;
        }
    }

    /// <summary>Updates the two-step sign-out button caption.</summary>
    /// <param name="confirm">Whether to request the second activation.</param>
    private void Confirm(bool confirm)
    {
        _confirm = confirm;
        SignOut.Content = Text(confirm ? "settings.confirm_sign_out" : "settings.sign_out");
    }

    /// <summary>Shows a retryable account action failure.</summary>
    /// <param name="message">The translated error, or null for a fresh attempt.</param>
    private void SetFailure(string? message)
    {
        Failure.Text = message;
        Failure.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    /// <summary>Translates an account message.</summary>
    /// <param name="key">The message key.</param><returns>The localized text.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);
}
