using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Reviews the actual changes for a package before applying its pinned installation plan.</summary>
public sealed partial class MarketInstall : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="MarketInstall"/> class.</summary>
    public MarketInstall() => InitializeComponent();

    /// <summary>Opens the current kernel detail for a listed package.</summary>
    /// <param name="api">Owning kernel.</param>
    /// <param name="slug">The selected package.</param>
    /// <param name="language">The reader's language.</param>
    /// <param name="describe">Localizes refusals.</param>
    /// <param name="back">Returns to a refreshed marketplace list.</param>
    /// <param name="token">Window lifetime.</param>
    /// <returns>The first detail read.</returns>
    internal async Task OpenAsync(StudioPageClient api, string slug, InterfaceLanguage language, Func<Exception, string> describe, Func<Task> back, CancellationToken token)
    {
        Translate(language);
        Heading.Text = slug;
        JsonObject? plan = null;
        JsonObject? detail = null;
        var update = false;
        var trust = false;
        Back.Click += async (_, _) => await back().ConfigureAwait(true);
        Retry.Click += async (_, _) => await RunAsync(() => LoadAsync(true)).ConfigureAwait(true);
        Cancel.Click += (_, _) => ShowDetail();
        Inspect.Click += async (_, _) => await RunAsync(async () =>
        {
            var request = new JsonObject { ["slug"] = slug, ["replace"] = update };
            if (trust)
            {
                request["trust"] = true;
            }

            var result = await api.MarketPlanAsync(false, request, token).ConfigureAwait(true);
            if ((bool?)result["ok"] != true || string.IsNullOrEmpty((string?)result["planId"]) || string.IsNullOrEmpty((string?)result["version"]) ||
                ((bool?)result["unreviewed"] == true && (!trust || string.IsNullOrEmpty((string?)result["contentDigest"]))))
            {
                Feedback.Text = (string?)result["error"] ?? (string?)result["next"] ?? Text("ext.there_is_nothing_installable_at");
                return;
            }

            plan = result;
            Inspect.IsVisible = false;
            Cancel.IsVisible = Install.IsVisible = true;
            var actions = result["actions"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
            var total = Math.Max((int?)result["kinds"]?["skill"] ?? 0, actions.Count(a => (string?)a["kind"] == "skill"));
            Heading.Text = Text("ext.name_version_will_install_the", ("name", slug), ("version", (string)result["version"]!));
            Description.Text = (bool?)result["unreviewed"] == true ? Text("ext.you_chose_to_trust_this") : Text("ext.checked_by_content_digest_identical");
            Notice.Text = (string?)result["contentDigest"];
            Actions.Children.Clear();
            foreach (var a in actions)
            {
                Actions.Children.Add(new Expander
                {
                    Header = string.Join(" · ", new[] { (string?)a["riskLevel"], (string?)a["action"], (string?)a["kind"], (string?)a["name"] }.Where(v => !string.IsNullOrEmpty(v))),
                    Content = new SelectableTextBlock { Text = a.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), TextWrapping = TextWrapping.Wrap },
                });
            }

            foreach (var warning in result["warnings"]?.AsArray() ?? [])
            {
                Actions.Children.Add(Caption(warning?.ToString() ?? string.Empty));
            }

            if ((int?)result["hiddenActions"] is > 0 and var hidden)
            {
                Actions.Children.Add(Caption(Text("ext.n_more_steps_are_not", ("n", hidden.ToString(CultureInfo.CurrentCulture)))));
            }

            if ((bool?)result["previewTruncated"] == true)
            {
                Actions.Children.Add(Caption(Text("ext.some_text_is_too_long")));
            }

            Acknowledge.Content = Text("ext.i_have_read_these_n", ("n", total.ToString(CultureInfo.CurrentCulture)));
            Acknowledge.IsVisible = total > 1;
            Acknowledge.IsChecked = false;
            Install.IsEnabled = total <= 1;
        }).ConfigureAwait(true);
        Acknowledge.IsCheckedChanged += (_, _) => Install.IsEnabled = !Acknowledge.IsVisible || Acknowledge.IsChecked == true;
        Install.Click += async (_, _) =>
        {
            if (plan is null || !Install.IsEnabled)
            {
                return;
            }

            await RunAsync(async () =>
            {
                var request = new JsonObject { ["slug"] = slug, ["replace"] = update, ["version"] = (string?)plan["version"], ["planId"] = (string?)plan["planId"] };
                if ((bool?)plan["unreviewed"] == true)
                {
                    request["trust"] = true;
                    request["digest"] = (string?)plan["contentDigest"];
                }

                var result = await api.MarketPlanAsync(true, request, token).ConfigureAwait(true);
                ShowDetail();
                Inspect.IsVisible = false;
                var applied = (bool?)result["applied"] == true;
                Notice.Text = applied ? Text("ext.installed") : (string?)result["error"] ?? Text("ext.there_is_nothing_installable_at");
                Description.Text = string.Join(Environment.NewLine, (result["actions"]?.AsArray() ?? []).Select(a => string.Join(" · ", (string?)a?["name"], (string?)a?["status"], (string?)a?["error"])));
                Feedback.Text = (string?)result["next"];
                Retry.IsVisible = !applied;
            }).ConfigureAwait(true);
        };
        await RunAsync(() => LoadAsync(false)).ConfigureAwait(true);

        async Task RunAsync(Func<Task> action)
        {
            if (!IsEnabled)
            {
                return;
            }

            IsEnabled = false;
            Feedback.Text = string.Empty;
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    Feedback.Text = describe(error);
                    Retry.IsVisible = true;
                }
            }
            finally
            {
                IsEnabled = true;
            }
        }

        async Task LoadAsync(bool refresh)
        {
            detail = await api.MarketDetailAsync(slug, refresh, token).ConfigureAwait(true);
            ShowDetail();
        }

        void ShowDetail()
        {
            plan = null;
            Actions.Children.Clear();
            Cancel.IsVisible = Install.IsVisible = Acknowledge.IsVisible = false;
            Feedback.Text = string.Empty;
            var package = detail?["package"];
            Heading.Text = (string?)package?["name"] ?? slug;
            Description.Text = (string?)package?["description"] ?? (string?)package?["summary"];
            var installed = (string?)detail?["installed"]?["version"];
            var latest = (string?)package?["latestVersion"];
            update = installed is not null && installed != latest;
            trust = (bool?)detail?["pinned"] != true;
            var blocked = detail?["cache"] is not null || (update && (string?)package?["kind"] == "skill");
            Retry.IsVisible = detail?["cache"] is not null;
            Inspect.IsVisible = !blocked && (installed is null || update);
            Inspect.Content = Text(trust ? "ext.trust_and_install" : update ? "ext.see_what_the_update_installs" : "ext.see_what_it_installs");
            AutomationProperties.SetAutomationId(Inspect, trust ? "action:market.trust" : "action:market.inspect");
            Notice.Text = string.Join(" · ", new[] { slug, latest, trust ? Text("ext.content_not_pinned_by_review") : null, installed is not null ? Text("ext.installed") + " " + installed : null }.Where(x => !string.IsNullOrEmpty(x)));
        }
    }
}
