using Avalonia;
using Avalonia.Controls;
using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell.Settings;

public sealed partial class MarketInstall
{
    /// <summary>Uses the review's recognized groups without dropping unrecognized levels.</summary>
    /// <param name="action">Reviewed action.</param>
    /// <returns>Presentation group.</returns>
    private static string Risk(JsonObject action) => (string?)action["riskLevel"] switch
    {
        "high" => "high",
        "medium" => "medium",
        _ => "low",
    };

    /// <summary>Creates wrapping note text in an action list.</summary>
    /// <param name="message">Displayed message.</param>
    /// <returns>The note control.</returns>
    private static TextBlock Note(string message) => new() { Text = message, Classes = { "market-detail-hint" } };

    /// <summary>Displays a plan grouped by declared risk and its acknowledgement when applicable.</summary>
    /// <param name="result">The plan issued by the host.</param>
    private void DrawReview(JsonObject result)
    {
        var actions = result["actions"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
        var skills = actions.Where(item => (string?)item["kind"] == "skill").ToArray();
        var total = Math.Max((int?)result["kinds"]?["skill"] ?? 0, skills.Length);
        var unreviewed = _ownPackage is not null || (bool?)result["unreviewed"] == true;
        var digest = (string?)result["contentDigest"] ?? string.Empty;
        _many = total > 1;
        ReviewTitle.Text = Text("ext.name_version_will_install_the", ("name", _slug), ("version", (string)result["version"]!));
        TrustAlert.IsVisible = unreviewed;
        TrustHeading.Text = Text(_ownPackage is null ? "ext.you_chose_to_trust_this" : "ext.unreviewed_visible_only_to_you");
        TrustCopy.Text = Text(_ownPackage is null ? "ext.this_is_what_the_source" : "ext.this_is_your_own_version");
        VerifiedDigest.IsVisible = !unreviewed;
        DigestBanner.Classes.Set("unreviewed", unreviewed);
        UnreviewedDigestLabel.Text = Text("ext.content_digest_digest", ("digest", string.Empty)).TrimEnd();
        UnreviewedDigestLabel.IsVisible = unreviewed;
        Digest.Text = digest;
        Digest.IsVisible = unreviewed || digest.Length > 0;
        McpNote.IsVisible = actions.Any(item => (string?)item["kind"] == "mcp");

        ManyAlert.IsVisible = _many;
        ManyTitle.Text = Text("ext.this_source_holds_n_skills", ("n", Number(total)));
        SkillNames.Children.Clear();
        foreach (var skill in skills)
        {
            var label = new TextBlock { Text = "•  " + (string?)skill["name"], FontSize = 13 };
            label.Bind(TextBlock.FontFamilyProperty, label.GetResourceObservable("WorkbenchMonoFamily"));
            SkillNames.Children.Add(label);
        }

        if (total > skills.Length)
        {
            SkillNames.Children.Add(new TextBlock { Text = "•  " + Text("ext.n_more_skills_are_not", ("n", Number(total - skills.Length))), FontSize = 13 });
        }

        Acknowledge.Content = Text("ext.i_have_read_these_n", ("n", Number(total)));
        Acknowledge.IsChecked = false;
        ReviewActions.Children.Clear();
        foreach (var level in new[] { "high", "medium", "low" })
        {
            var group = actions.Where(action => Risk(action) == level).ToArray();
            if (group.Length == 0)
            {
                continue;
            }

            var titleKey = level switch
            {
                "high" => "ext.read_each_of_these_before",
                "medium" => "ext.these_change_what_is_available",
                _ => "ext.these_only_add_files",
            };
            var heading = new TextBlock { Text = Text(titleKey), Classes = { "market-risk-heading" } };
            heading.Classes.Set("high", level == "high");
            var entries = new StackPanel { Children = { heading } };
            foreach (var action in group)
            {
                var candidate = new MarketPlanCandidate();
                candidate.Open(action, Language);
                entries.Children.Add(candidate);
            }

            ReviewActions.Children.Add(entries);
        }

        if ((int?)result["hiddenActions"] is > 0 and int hidden)
        {
            ReviewActions.Children.Add(Note(Text("ext.n_more_steps_are_not", ("n", Number(hidden)))));
        }

        if ((bool?)result["previewTruncated"] == true)
        {
            ReviewActions.Children.Add(Note(Text("ext.some_text_is_too_long")));
        }

        foreach (var warning in result["warnings"]?.AsArray() ?? [])
        {
            ReviewActions.Children.Add(Note((string?)warning ?? string.Empty));
        }

        ReviewFailure.IsVisible = false;
        ListingFrame.IsVisible = StatusSurface.IsVisible = false;
        ReviewFrame.IsVisible = true;
    }

    /// <summary>Shows written and pending runtime changes separately in the result.</summary>
    /// <param name="result">Install result from the owning kernel.</param>
    private void DrawOutcome(JsonObject result)
    {
        var ok = (bool?)result["ok"] == true;
        var actions = result["actions"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
        var failed = actions.Where(action => (string?)action["status"] == "failed").ToArray();
        var reload = (string?)result["reloadError"];
        StatusHeading.Text = (string?)actions.FirstOrDefault()?["name"] ?? Text("ext.install");
        var success = string.IsNullOrEmpty(reload) ? Text("ext.installed_the_next_turn_can") : Text("ext.installed_but_the_runtime_did", ("reason", reload));
        var failure = (string?)result["error"] ?? (failed.Length > 0 ? Text("ext.not_all_items_were_installed") : (string?)result["next"] ?? Text("ext.not_installed"));
        StatusCopy.Text = ok ? success : failure;
        StatusBanner.BorderThickness = ok ? default : new Thickness(2, 0, 0, 0);
        StatusCopy.IsVisible = true;
        StatusRetry.IsVisible = !ok;
        var packageKind = (string?)(_ownPackage ?? _detail?["package"])?["kind"];
        var target = (bool?)result["applied"] == true
            ? actions.FirstOrDefault(item => (string?)item["status"] == "done" && !string.IsNullOrEmpty((string?)item["name"])
                && (packageKind is not ("plugin" or "theme") || (string?)item["kind"] == "plugin"))
            : null;
        _installedTarget = target is null ? null : ((string?)target["kind"] ?? string.Empty, (string)target["name"]!);
        ViewInstalled.IsVisible = _installedTarget is not null && _showInstalled is not null;
        OutcomeItems.Children.Clear();
        foreach (var item in actions)
        {
            var state = (string?)item["status"];
            var name = (string?)item["name"] ?? (string?)item["kind"];
            if (state == "failed")
            {
                var error = (string?)item["error"];
                var next = (string?)item["next"];
                OutcomeItems.Children.Add(Note(name + ": " + (error ?? next ?? Text("ext.not_installed"))));
                if (error is not null && next is not null)
                {
                    OutcomeItems.Children.Add(Note(next));
                }
            }
            else if (state == "done" && !string.IsNullOrEmpty(name))
            {
                OutcomeItems.Children.Add(Note(name));
            }
        }

        if (!ok && !string.IsNullOrEmpty(reload))
        {
            OutcomeItems.Children.Add(Note(Text("ext.the_runtime_did_not_reload", ("reason", reload))));
        }

        ListingFrame.IsVisible = ReviewFrame.IsVisible = false;
        StatusSurface.IsVisible = true;
    }
}
