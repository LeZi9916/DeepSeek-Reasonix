using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

public sealed partial class GitRepositoryView
{
    /// <summary>The pending read of a commit proposal or push plan.</summary>
    private CancellationTokenSource? _pendingReview;

    /// <summary>Host for the active review when opened from the full changes canvas.</summary>
    private StackPanel? _reviewHost;

    /// <summary>Gets the surface which initiated the review.</summary>
    private StackPanel ReviewArea => _reviewHost ?? Review;

    /// <summary>Cancels the read and restores the repository toolbar.</summary>
    private void CloseReview()
    {
        _reviewRead++;
        _pendingReview?.Cancel();
        ReviewArea.Children.Clear();
        ReviewArea.IsVisible = false;
        _reviewHost = null;
        Fit(_bodyHeight);
        Draft.IsEnabled = Push.IsEnabled = true;
    }

    /// <summary>Reads a new proposal. Only a subsequent confirmation can commit it.</summary>
    /// <returns>The index review.</returns>
    private Task ReviewCommitAsync() => LoadReviewAsync(true);

    /// <summary>Reads the local push plan. Previewing does not contact the remote.</summary>
    /// <returns>The local destination review.</returns>
    private Task ReviewPushAsync() => LoadReviewAsync(false);

    /// <summary>Loads a cancellable review, rejecting late responses.</summary>
    /// <param name="commit">True for index review, false for push review.</param>
    /// <returns>The completed read.</returns>
    /// <param name="canvas">Optional full changes-page review host.</param>
    private async Task LoadReviewAsync(bool commit, StackPanel? canvas = null)
    {
        CloseReview();
        _reviewHost = canvas;
        var ticket = _reviewRead;
        using var read = CancellationTokenSource.CreateLinkedTokenSource(_token);
        _pendingReview = read;
        ReviewArea.IsVisible = true;
        Fit(_bodyHeight);
        Draft.IsEnabled = Push.IsEnabled = false;
        Failure.IsVisible = Outcome.IsVisible = false;
        var close = Action(Text("common.cancel"), commit ? "commit.close" : "push.close", () =>
        {
            CloseReview();
            return Task.CompletedTask;
        });
        ReviewArea.Children.Add(Caption(Text(commit ? "workbench.reading_the_staged_changes_and" : "common.loading")));
        ReviewArea.Children.Add(close);
        try
        {
            var plan = commit ? await _api.GitProposalAsync(_repo, read.Token).ConfigureAwait(true) : await _api.GitPushPreviewAsync(_repo, read.Token).ConfigureAwait(true);
            if (ticket != _reviewRead || read.IsCancellationRequested)
            {
                return;
            }

            ReviewArea.Children.Clear();
            var actions = commit ? CommitReview(plan) : PushReview(plan);
            actions.Children.Add(close);
            ReviewArea.Children.Add(actions);
            ReviewArea.BringIntoView();
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (ticket == _reviewRead && !_token.IsCancellationRequested)
            {
                CloseReview();
                Report(error);
            }
        }
        finally
        {
            if (ReferenceEquals(_pendingReview, read))
            {
                _pendingReview = null;
            }
        }
    }

    /// <summary>Displays staged files, editable message and mandatory acknowledgement when flagged.</summary>
    /// <param name="proposal">The exact staged index snapshot.</param>
    /// <returns>The confirmation and redraft buttons.</returns>
    private WrapPanel CommitReview(JsonObject proposal)
    {
        var files = proposal["files"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
        var fingerprint = (string?)proposal["fingerprint"];
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock { Text = Text("workbench.staged_files"), FontWeight = FontWeight.SemiBold, FontSize = 13 });
        var count = Caption(Text("workbench.n_files_staged", ("n", files.Length.ToString(CultureInfo.CurrentCulture))));
        Grid.SetColumn(count, 1);
        header.Children.Add(count);
        ReviewArea.Children.Add(header);
        foreach (var file in files)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var path = new TextBlock { Text = (string?)file["status"] + "  " + (string?)file["path"], FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            path.Bind(TextBlock.FontFamilyProperty, path.GetResourceObservable("DataPathFamily"));
            row.Children.Add(path);
            var totals = Caption(Counts(file));
            Grid.SetColumn(totals, 1);
            row.Children.Add(totals);
            ReviewArea.Children.Add(row);
        }

        if ((bool?)proposal["truncated"] == true)
        {
            ReviewArea.Children.Add(Caption(Text("workbench.the_staged_diff_is_long")));
        }

        var sensitive = files.Where(f => (bool?)f["sensitive"] == true).Select(f => (string?)f["path"]).ToArray();
        var contentSecrets = (bool?)proposal["contentSecrets"] == true;
        if (sensitive.Length > 0)
        {
            ReviewArea.Children.Add(Caption(Text("workbench.may_contain_secrets_files", ("files", string.Join(", ", sensitive)))));
        }

        if (contentSecrets)
        {
            ReviewArea.Children.Add(Caption(Text("workbench.added_lines_contain_values_shaped")));
        }

        var warned = contentSecrets || sensitive.Length > 0;
        var acknowledge = new CheckBox { Content = Caption(Text("workbench.i_confirm_these_can_be")), IsVisible = warned };
        AutomationProperties.SetAutomationId(acknowledge, "action:commit.acknowledge");
        ReviewArea.Children.Add(acknowledge);
        var message = new TextBox { Text = (string?)proposal["message"], AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 124, FontSize = 12, FontWeight = FontWeight.Normal, PlaceholderText = Text("workbench.commit_message") };
        message.Bind(TextBox.FontFamilyProperty, message.GetResourceObservable("DataPathFamily"));
        AutomationProperties.SetAutomationId(message, "action:commit.edit");
        ReviewArea.Children.Add(message);
        var apply = Action(Text("workbench.commit_locally"), "commit.confirm", async () =>
        {
            if (!Valid())
            {
                return;
            }

            await WriteAsync(async () =>
            {
                var result = await _api.GitCommitAsync(_repo, fingerprint!, message.Text!, acknowledge.IsChecked == true, _token).ConfigureAwait(true);
                CloseReview();
                Outcome.Text = string.Join(" · ", (string?)result["hash"], (string?)result["subject"]);
                Outcome.IsVisible = true;
            }).ConfigureAwait(true);
        });
        apply.IsEnabled = Valid();
        message.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                apply.IsEnabled = Valid();
            }
        };
        acknowledge.IsCheckedChanged += (_, _) => apply.IsEnabled = Valid();
        ReviewArea.Children.Add(Caption(Text("workbench.recorded_locally_only_nothing_is")));
        var buttons = new WrapPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, ItemSpacing = 5, LineSpacing = 4 };
        apply.Classes.Add("primary");
        buttons.Children.Add(apply);
        buttons.Children.Add(Action(Text("workbench.redraft"), "commit.redraft", () => LoadReviewAsync(true, _reviewHost)));
        return buttons;

        bool Valid() => files.Length > 0 && !string.IsNullOrEmpty(fingerprint) && !string.IsNullOrWhiteSpace(message.Text) && (!warned || acknowledge.IsChecked == true);
    }

    /// <summary>Displays the exact branch, head and destination before confirming a push.</summary>
    /// <param name="plan">Kernel preview, retained unchanged for confirmation.</param>
    /// <returns>The push and refresh buttons.</returns>
    private WrapPanel PushReview(JsonObject plan)
    {
        var host = (string?)plan["host"] ?? string.Empty;
        ReviewArea.Children.Add(Caption(Text("workbench.push_to_host", ("host", host))));
        ReviewArea.Children.Add(Caption((string?)plan["branch"] + " → " + (string?)plan["remote"] + "/" + (string?)plan["upstreamBranch"]));
        ReviewArea.Children.Add(new SelectableTextBlock { Text = (string?)plan["url"], TextWrapping = TextWrapping.Wrap });
        ReviewArea.Children.Add(Caption((string?)plan["head"] ?? string.Empty));
        var ahead = (int?)plan["ahead"] ?? 0;
        var behind = (int?)plan["behind"] ?? 0;
        ReviewArea.Children.Add(Caption("↑ " + ahead.ToString(CultureInfo.CurrentCulture) + "   ↓ " + behind.ToString(CultureInfo.CurrentCulture)));
        if (behind > 0)
        {
            ReviewArea.Children.Add(Caption(Text("workbench.the_remote_has_n_commits", ("n", behind.ToString(CultureInfo.CurrentCulture)))));
        }

        if (ahead == 0)
        {
            ReviewArea.Children.Add(Caption(Text("workbench.the_remote_already_has_every")));
        }

        var apply = Action(Text("workbench.push_to_host", ("host", host)), "push.confirm", () => WriteAsync(async () =>
        {
            var result = await _api.GitPushAsync(_repo, plan, _token).ConfigureAwait(true);
            CloseReview();
            Outcome.Text = Text("workbench.push_to_host", ("host", host)) + " · " + (string?)result["plan"]?["head"];
            Outcome.IsVisible = true;
        }));
        apply.IsEnabled = ahead > 0;
        var buttons = new WrapPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, ItemSpacing = 5, LineSpacing = 4 };
        apply.Classes.Add("primary");
        buttons.Children.Add(apply);
        buttons.Children.Add(Action(Text("common.read_again"), "push.recheck", () => LoadReviewAsync(false, _reviewHost)));
        return buttons;
    }

    /// <summary>Lists root-repository branches, allowing only a catalog choice to be checked out.</summary>
    /// <returns>The local branch catalog.</returns>
    private async Task ReadBranchesAsync()
    {
        if (Branches.IsVisible)
        {
            Branches.IsVisible = false;
            return;
        }

        try
        {
            var list = await _api.GitBranchesAsync(_token).ConfigureAwait(true);
            if (_token.IsCancellationRequested)
            {
                return;
            }

            Branches.Children.Clear();
            foreach (var item in list["branches"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                var name = (string)item["name"]!;
                var button = Action(name, "workbench.switch-branch", () => WriteAsync(async () =>
                {
                    var state = await _api.GitSwitchBranchAsync(name, _token).ConfigureAwait(true);
                    Branch.Content = (string?)state["branch"];
                    Branches.IsVisible = false;
                }));
                button.IsEnabled = (bool?)item["current"] != true;
                Branches.Children.Add(button);
            }

            Branches.IsVisible = true;
        }
        catch (Exception error)
        {
            if (!_token.IsCancellationRequested)
            {
                Report(error);
            }
        }
    }
}
