using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Settings;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Mounts the approvals, confinement, and web sections independently.</summary>
    /// <param name="column">The settings page.</param>
    /// <param name="api">The active kernel.</param>
    /// <returns>The page's initial reads.</returns>
    private Task SafetySettingsAsync(StackPanel column, StudioPageClient api)
    {
        var approvals = SafetyApprovalsAsync(column, api);
        var execution = Card(column, "settings.sandbox_and_execution");
        var sandbox = new SafetySandboxView { Tag = "settings.sandbox" };
        var runtime = new SafetyExecutionView();
        execution.Children.Add(sandbox);
        execution.Children.Add(runtime);
        var web = new SafetyWebView();
        Card(column, "safety.web").Children.Add(web);
        return Task.WhenAll(
            approvals,
            sandbox.OpenAsync(api, Language, directory => new Host.AvaloniaHost(this).PickFolderAsync(directory), DescribeError, _closing.Token),
            runtime.OpenAsync(api, Language, DescribeError, _closing.Token),
            web.OpenAsync(api, Language, DescribeError, _closing.Token));
    }

    /// <summary>Displays the three approval-related parts of the security settings.</summary>
    /// <param name="column">The settings page.</param>
    /// <param name="api">The active kernel.</param>
    /// <returns>The permission and tool reads.</returns>
    private Task SafetyApprovalsAsync(StackPanel column, StudioPageClient api)
    {
        var card = Card(column, "settings.approval");
        var warning = Detail(string.Empty);
        warning.Name = "ApprovalWarning";
        warning.Classes.Add("approval-warning");
        SetWarning(_composerStatus.ToolApprovalMode);
        var engine = _engine!;
        SegmentChoice(
            card,
            "settings.tool_approvals",
            "tool-approval.mode",
            [
                new("dontAsk", Message("safety.do_not_ask")),
                new("ask", Message("safety.ask")),
                new("auto", Message("safety.auto_approve")),
                new("yolo", Message("safety.allow_all")),
            ],
            _composerStatus.ToolApprovalMode,
            async mode =>
            {
                var applied = await ApplyApprovalAsync(engine, mode).ConfigureAwait(true);
                SetWarning(applied?.ToolApprovalMode ?? mode);
            },
            Message("safety.ask_before_writing_or_running"));
        card.Children.Add(warning);
        var rules = new PermissionSettingsView { Tag = "settings.rules_you_spell_out" };
        var tools = new SafetyToolInventory { Tag = "ext.tools_the_model_can_use" };
        card.Children.Add(rules);
        card.Children.Add(tools);
        return Task.WhenAll(
            rules.OpenAsync(api, Language, DescribeError, _closing.Token),
            tools.OpenAsync(api, Language, DescribeError, _closing.Token));

        string Message(string key) => MessageCatalog.Get(key, Language);

        void SetWarning(string mode)
        {
            warning.IsVisible = mode == "yolo";
            warning.Text = Message("safety.stops_asking_only_for_a");
        }
    }
}
