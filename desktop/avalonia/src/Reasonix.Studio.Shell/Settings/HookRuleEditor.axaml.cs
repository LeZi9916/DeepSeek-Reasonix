using Avalonia.Automation;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Edits one rule with the selected event's capabilities from the kernel.</summary>
public sealed partial class HookRuleEditor : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="HookRuleEditor"/> class.</summary>
    public HookRuleEditor() => InitializeComponent();

    /// <summary>Binds the displayed fields to an editable rule.</summary>
    /// <param name="rule">The local draft.</param>
    /// <param name="events">The supported event options.</param>
    /// <param name="language">The display language.</param>
    /// <param name="label">Formats a known hook event.</param>
    /// <param name="tryOnce">Runs the chosen draft explicitly.</param>
    /// <param name="remove">Removes the rule from its scope.</param>
    internal void Open(JsonObject rule, JsonObject[] events, InterfaceLanguage language, Func<string, string> label, Func<JsonObject, Task<JsonObject?>> tryOnce, Func<Task> remove)
    {
        Translate(language);
        var current = (string?)rule["event"] ?? string.Empty;
        var choices = events.Select(item => (string?)item["name"]).OfType<string>().Append(current).Distinct()
            .Select(name => new Option(name, label(name))).ToArray();
        EventChoice.ItemsSource = choices;
        EventChoice.SelectedItem = choices.FirstOrDefault(choice => choice.Id == current);
        AutomationProperties.SetName(EventChoice, Text("ext.before_a_tool_runs"));
        Matcher.Text = (string?)rule["match"];
        Command.Text = (string?)rule["command"];
        Issues.Text = string.Join(Environment.NewLine, (rule["issues"] as JsonArray ?? []).Select(issue => (string?)issue));
        Issues.IsVisible = Issues.Text.Length > 0;
        UpdateEvent();
        EventChoice.SelectionChanged += (_, _) => UpdateEvent();
        Command.TextChanged += (_, _) =>
        {
            rule["command"] = Command.Text ?? string.Empty;
            Trial.IsVisible = false;
        };
        Matcher.TextChanged += (_, _) =>
        {
            rule["match"] = Matcher.Text ?? string.Empty;
            Trial.IsVisible = false;
        };
        Remove.Click += async (_, _) => await remove().ConfigureAwait(true);
        Try.Click += async (_, _) =>
        {
            Trial.IsVisible = false;
            Try.Content = Text("ext.running");
            try
            {
                if (await tryOnce(rule).ConfigureAwait(true) is { } result)
                {
                    ShowTrial(result);
                }
            }
            finally
            {
                Try.Content = Text("ext.dry_run_once");
            }
        };

        void UpdateEvent()
        {
            if (EventChoice.SelectedItem is Option choice)
            {
                rule["event"] = choice.Id;
                var info = events.FirstOrDefault(item => (string?)item["name"] == choice.Id);
                Matcher.IsVisible = (bool?)info?["usesMatch"] == true;
                Blocking.IsVisible = (bool?)info?["blocking"] == true;
                Trial.IsVisible = false;
            }
        }
    }

    /// <summary>Shows the one-shot result returned by the active kernel.</summary>
    /// <param name="result">The dry-run response.</param>
    private void ShowTrial(JsonObject result)
    {
        var decision = (string?)result["decision"];
        var timedOut = (bool?)result["timedOut"] == true;
        var blocks = (bool?)result["blocks"] == true;
        Verdict.Text = (timedOut, blocks, decision, Blocking.IsVisible) switch
        {
            (true, _, _, _) => Text("ext.timed_out_this_step_counts"),
            (_, true, _, _) => VerdictLabel("这一次它会挡住这一步", "This run would block the step"),
            (_, _, "block", false) => VerdictLabel("这个事件挡不住东西，只会提醒你", "This event can warn but cannot block"),
            (_, _, "warn", _) => VerdictLabel("会作为提醒显示出来", "This run would show a warning"),
            _ => VerdictLabel("将放行", "This run would allow the step"),
        };
        Verdict.Classes.Set("blocks", blocks);
        Timing.Text = $"exit {(int?)result["exitCode"]} · {(int?)result["durationMs"]}ms";
        var output = (string?)result["stdout"];
        if (string.IsNullOrEmpty(output))
        {
            output = (string?)result["stderr"] ?? string.Empty;
        }

        Output.Text = output[..Math.Min(160, output.Length)];
        TrialOutput.IsVisible = Output.Text.Length > 0;
        Trial.IsVisible = true;

        string VerdictLabel(string zh, string en) => Language == InterfaceLanguage.Zh ? zh : en;
    }
}
