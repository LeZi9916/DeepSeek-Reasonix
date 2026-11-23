using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Displays the community tally and applies only an explicit user's vote.</summary>
internal sealed partial class MarketVoteView : UserControl
{
    /// <summary>The generation of the listing currently shown.</summary>
    private int _read;

    /// <summary>Initializes a new instance of the <see cref="MarketVoteView"/> class.</summary>
    public MarketVoteView() => InitializeComponent();

    /// <summary>Shows one listing's tally and loads its account-specific state.</summary>
    /// <param name="api">The owning host.</param>
    /// <param name="slug">Marketplace identity.</param>
    /// <param name="package">Listing totals before the vote endpoint answers.</param>
    /// <param name="language">Settings language.</param>
    /// <param name="describe">Formats a refusal.</param>
    /// <param name="signIn">Opens account settings.</param>
    /// <param name="token">Window request lifetime.</param>
    /// <returns>The initial vote read.</returns>
    internal async Task OpenAsync(
        StudioPageClient api,
        string slug,
        JsonObject package,
        InterfaceLanguage language,
        Func<Exception, string> describe,
        Func<Task>? signIn,
        CancellationToken token)
    {
        var read = ++_read;
        var busy = false;
        var unavailable = false;
        JsonObject? vote = null;
        SignInVote.Content = Text("ext.sign_in_to_vote");
        RetryVote.Content = Text("common.retry");
        if (signIn is not null)
        {
            SignInVote.Click += async (_, _) => await signIn().ConfigureAwait(true);
        }

        RetryVote.Click += async (_, _) => await ReadAsync().ConfigureAwait(true);
        Up.Click += async (_, _) => await CastAsync(1).ConfigureAwait(true);
        Down.Click += async (_, _) => await CastAsync(-1).ConfigureAwait(true);
        Draw();
        await ReadAsync().ConfigureAwait(true);

        async Task ReadAsync()
        {
            if (busy || read != _read)
            {
                return;
            }

            busy = true;
            RetryVote.IsVisible = VoteFailure.IsVisible = false;
            Draw();
            try
            {
                vote = await api.MarketVoteAsync(slug, token).ConfigureAwait(true);
                unavailable = vote is null;
            }
            catch (KernelRefusal error) when (error.Code is "account.signin_disabled" or "market.signed_out")
            {
                unavailable = error.Code == "account.signin_disabled";
                vote = new JsonObject { ["signedIn"] = false, ["value"] = 0 };
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    VoteFailure.Text = describe(error);
                    VoteFailure.IsVisible = RetryVote.IsVisible = true;
                }
            }
            finally
            {
                busy = false;
                if (read == _read && !token.IsCancellationRequested)
                {
                    Draw();
                }
            }
        }

        async Task CastAsync(int value)
        {
            if (busy || read != _read || (bool?)vote?["signedIn"] != true || (bool?)vote?["canVote"] == false)
            {
                return;
            }

            busy = true;
            VoteFailure.IsVisible = false;
            Draw();
            try
            {
                vote = await api.SetMarketVoteAsync(slug, (int?)vote?["value"] == value ? 0 : value, token).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    if (error is KernelRefusal { Code: "market.signed_out" } && vote is not null)
                    {
                        vote["signedIn"] = false;
                        vote["value"] = 0;
                    }

                    VoteFailure.Text = describe(error);
                    VoteFailure.IsVisible = true;
                }
            }
            finally
            {
                busy = false;
                if (read == _read && !token.IsCancellationRequested)
                {
                    Draw();
                }
            }
        }

        void Draw()
        {
            var up = (int?)vote?["upCount"] ?? (int?)package["upCount"] ?? 0;
            var down = (int?)vote?["downCount"] ?? (int?)package["downCount"] ?? 0;
            var rate = vote?.ContainsKey("approvalRate") == true ? (double?)vote["approvalRate"] : (double?)package["approvalRate"];
            var mine = (int?)vote?["value"] ?? 0;
            var signedIn = (bool?)vote?["signedIn"] == true;
            RateTrack.IsVisible = rate is not null;
            RateFill.Width = Math.Clamp(rate ?? 0, 0, 1) * 120;
            RateLabel.Text = rate is double share
                ? MessageCatalog.Get("ext.pct_positive_n_votes", language, ("pct", Number((int)Math.Round(100 * share))), ("n", Number(up + down)))
                : Text("ext.no_votes_yet");
            UpText.Text = Text("ext.upvote") + "  " + Number(up);
            DownText.Text = Text("ext.downvote") + "  " + Number(down);
            Up.Classes.Set("chosen", mine == 1);
            Down.Classes.Set("chosen", mine == -1);
            Up.IsEnabled = Down.IsEnabled = !busy && signedIn && (bool?)vote?["canVote"] != false;
            VoteButtons.IsVisible = !unavailable;
            var upLabel = Text(mine == 1 ? "ext.withdraw_upvote" : "ext.upvote");
            var downLabel = Text(mine == -1 ? "ext.withdraw_downvote" : "ext.downvote");
            AutomationProperties.SetName(Up, upLabel);
            AutomationProperties.SetName(Down, downLabel);
            ToolTip.SetTip(Up, upLabel);
            ToolTip.SetTip(Down, downLabel);
            var emailUnverified = signedIn && (bool?)vote?["emailVerified"] == false;
            VoteNote.Text = (unavailable, (bool?)vote?["own"] == true, emailUnverified) switch
            {
                (true, _, _) => Text("ext.voting_is_not_available_on"),
                (_, true, _) => Text("ext.you_cannot_vote_on_a"),
                (_, _, true) => Text("ext.verify_your_account_email_to"),
                _ => string.Empty,
            };
            VoteNote.IsVisible = !string.IsNullOrEmpty(VoteNote.Text);
            SignInVote.IsVisible = vote is not null && !signedIn && !unavailable && signIn is not null;
        }

        string Text(string key) => MessageCatalog.Get(key, language);

        static string Number(int count) => count.ToString(CultureInfo.CurrentCulture);
    }
}
