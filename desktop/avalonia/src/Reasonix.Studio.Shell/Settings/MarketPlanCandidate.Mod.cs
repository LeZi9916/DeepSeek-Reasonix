using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell.Settings;

internal sealed partial class MarketPlanCandidate
{
    /// <summary>Displays the host's structured module analysis and its limits.</summary>
    /// <param name="mod">The static module preview returned by the host.</param>
    /// <param name="language">The settings language.</param>
    private void ShowMod(JsonObject mod, InterfaceLanguage language)
    {
        Add("ext.claude_code_mod_full_trust", Text("ext.its_code_runs_on_this"));
        RiskRows.Children.Add(new TextBlock { Text = Text("ext.what_follows_is_a_static"), Classes = { "market-detail-hint" } });
        if ((bool?)mod["listed"] != true)
        {
            var reason = (string?)mod["reason"] switch
            {
                "typescript" => "ext.it_is_typescript_which_this",
                "too_large" => "ext.the_file_is_too_large",
                "unparseable" => "ext.it_could_not_be_parsed",
                "timeout" => "ext.parsing_timed_out",
                "unsupported_module" => "ext.it_is_not_a_module",
                string other => other,
                _ => string.Empty,
            };
            Add("ext.not_previewed", Join(Text(reason), Text("ext.that_does_not_mean_it")));
        }
        else
        {
            var events = mod["events"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
            var literal = events.Where(item => (bool?)item["dynamic"] != true).Select(item => (string?)item["name"]).OfType<string>().ToArray();
            var dynamicEvents = events.Count(item => (bool?)item["dynamic"] == true);
            Add("ext.events_it_hooks", literal.Length > 0 ? string.Join(", ", literal) : Text("ext.none_listed"));
            if (dynamicEvents > 0)
            {
                Add("ext.events_whose_names_cannot_be", dynamicEvents.ToString(CultureInfo.CurrentCulture));
            }

            var calls = mod["api"]?.AsArray().OfType<JsonObject>().Select(item => (bool?)item["dynamic"] == true
                ? "$.…"
                : string.Join(".", new[] { "$", (string?)item["namespace"], (string?)item["method"] }.Where(value => !string.IsNullOrEmpty(value)))) ?? [];
            Add("ext.api_it_uses", string.Join(", ", calls));
            var session = mod["session"]?.AsArray().Select(item => (string?)item).ToArray() ?? [];
            Add("ext.reads_your_conversation", string.Join(", ", session.Where(item => item is "messages" or "usage")));
            if (session.Contains("submit", StringComparer.Ordinal))
            {
                Add("ext.queues_prompts_in_front_of", Text("ext.under_its_own_name_never"));
            }

            var imports = mod["imports"]?.AsArray().OfType<JsonObject>().Select(item =>
            {
                var kind = (string?)item["kind"] switch
                {
                    "static" => "import",
                    "dynamic" => "import()",
                    string other => other,
                    _ => string.Empty,
                };
                return kind + " " + (string?)item["spec"];
            }) ?? [];
            Add("ext.code_it_pulls_in_not", string.Join(", ", imports));
            var findings = mod["findings"]?.AsArray().OfType<JsonObject>().Select(item => Text((string?)item["kind"] switch
            {
                "eval" => "ext.uses_eval",
                "function_ctor" => "ext.uses_new_function",
                "computed_member" => "ext.reaches_the_api_by_a",
                "api_escapes" => "ext.hands_the_api_object_elsewhere",
                "on_escapes" => "ext.hands_the_registering_function_elsewhere",
                "destructured_api" => "ext.destructures_the_api_object",
                "no_register" => "ext.no_exported_register_was_found",
                string other => other,
                _ => string.Empty,
            })) ?? [];
            Add("ext.constructs_that_leave_the_listing", string.Join("、", findings));
            Add("ext.events_this_app_never_raises", string.Join(", ", mod["unserved"]?.AsArray().Select(item => (string?)item) ?? []));
        }

        var sha = (string?)mod["sha256"] ?? string.Empty;
        Add("ext.module", Join((string?)mod["module"], sha[..Math.Min(12, sha.Length)]));

        string Text(string key) => MessageCatalog.Get(key, language);

        void Add(string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                RiskRows.Children.Add(Fact(Text(key), value));
            }
        }
    }
}
