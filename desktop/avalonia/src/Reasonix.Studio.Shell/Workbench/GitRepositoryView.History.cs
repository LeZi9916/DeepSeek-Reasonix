using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

public sealed partial class GitRepositoryView
{
    /// <summary>Draws one graph edge in its originating lane's color.</summary>
    /// <param name="graph">The current commit row.</param>
    /// <param name="x1">First horizontal coordinate.</param>
    /// <param name="y1">First vertical coordinate.</param>
    /// <param name="x2">Second horizontal coordinate.</param>
    /// <param name="y2">Second vertical coordinate.</param>
    /// <param name="lane">Stable graph lane index.</param>
    private static void Line(Canvas graph, double x1, double y1, double x2, double y2, int lane)
    {
        var line = new Avalonia.Controls.Shapes.Line { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2), StrokeThickness = 1.4 };
        line.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, graph.GetResourceObservable("Cat" + ((lane % 4) + 1).ToString(CultureInfo.InvariantCulture) + "Brush"));
        graph.Children.Add(line);
    }

    /// <summary>Renders graph lanes from the parent IDs of newest-first commits.</summary>
    /// <param name="history">Local history snapshot.</param>
    private void DrawHistory(JsonObject history)
    {
        History.Children.Clear();
        var waiting = new List<string?>();
        var rows = new List<(JsonObject Commit, int Lane, int[] From, int[] Through, int[] To)>();
        foreach (var commit in history["commits"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var id = (string?)commit["id"];
            var from = Enumerable.Range(0, waiting.Count).Where(i => waiting[i] == id).ToArray();
            var lane = from.Length > 0 ? from[0] : Free();
            var through = Enumerable.Range(0, waiting.Count).Where(i => waiting[i] is not null && waiting[i] != id).ToArray();
            foreach (var i in from)
            {
                waiting[i] = null;
            }

            var parents = commit["parents"]?.AsArray() ?? [];
            var to = new List<int>();
            foreach (var parent in parents.Select(p => (string?)p))
            {
                var existing = waiting.IndexOf(parent);
                var target = to.Count == 0 ? lane : existing >= 0 ? existing : Free();
                waiting[target] = parent;
                to.Add(target);
            }

            rows.Add((commit, lane, from, through, to.ToArray()));
        }

        foreach (var row in rows)
        {
            var id = (string)row.Commit["id"]!;
            var group = new StackPanel();
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 8, Height = 26 };
            var graph = new Canvas { Width = 16 + (12 * Math.Max(0, waiting.Count - 1)), Height = 26 };
            grid.Children.Add(graph);
            var x = 8 + (row.Lane * 12);
            foreach (var lane in row.Through)
            {
                Line(graph, 8 + (lane * 12), 0, 8 + (lane * 12), 26, lane);
            }

            foreach (var lane in row.From)
            {
                Line(graph, 8 + (lane * 12), 0, x, 13, lane);
            }

            foreach (var lane in row.To)
            {
                Line(graph, x, 13, 8 + (lane * 12), 26, lane);
            }

            var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6 };
            dot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, graph.GetResourceObservable("Cat" + ((row.Lane % 4) + 1).ToString(CultureInfo.InvariantCulture) + "Brush"));
            Canvas.SetLeft(dot, x - 3);
            Canvas.SetTop(dot, 10);
            graph.Children.Add(dot);
            var detail = new StackPanel { IsVisible = false, Margin = new Thickness(5, 2), Spacing = 3 };
            var pick = FlatAction(string.Empty, "workbench.commit", async () =>
            {
                detail.IsVisible = !detail.IsVisible;
                if (detail.IsVisible && detail.Children.Count == 0)
                {
                    await HistoryDetailAsync(id, detail).ConfigureAwait(true);
                }
            });
            pick.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            pick.HorizontalAlignment = HorizontalAlignment.Stretch;
            pick.Height = 26;
            var hash = new TextBlock { Text = id[..Math.Min(7, id.Length)] };
            hash.Classes.Add("git-history-hash");
            hash.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hash, 1);
            grid.Children.Add(hash);
            var refs = row.Commit["refs"]?.AsArray() ?? [];
            var subject = (string?)row.Commit["subject"] ?? string.Empty;
            var text = new TextBlock { Text = subject, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            text.Classes.Add("git-history-subject");
            Grid.SetColumn(text, 2);
            grid.Children.Add(text);
            var labels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            foreach (var reference in refs.Select(n => (string?)n ?? string.Empty))
            {
                var branch = !reference.StartsWith("tag: ", StringComparison.Ordinal);
                var label = new TextBlock { Text = reference, TextTrimming = TextTrimming.CharacterEllipsis, Height = 13.2, LineHeight = 13.2, UseLayoutRounding = false };
                label.Classes.Add("git-history-ref");
                label.Classes.Set("branch", branch);
                var badge = new Border { MaxWidth = 66, CornerRadius = new CornerRadius(12), Padding = new Thickness(6, 2), BorderThickness = new Thickness(1), Child = label, VerticalAlignment = VerticalAlignment.Center };
                badge.Bind(Border.BorderBrushProperty, badge.GetResourceObservable(branch ? "AccentBrush" : "HairBrush"));
                ToolTip.SetTip(badge, reference);
                labels.Children.Add(badge);
            }

            Grid.SetColumn(labels, 3);
            grid.Children.Add(labels);

            ToolTip.SetTip(pick, string.Join(" · ", id, (string?)row.Commit["author"], subject, string.Join(", ", refs.Select(n => (string?)n))));
            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds((long?)row.Commit["at"] ?? 0);
            var time = new TextBlock { Text = age.TotalDays >= 1 ? Text("workbench.history_day", ("n", ((int)age.TotalDays).ToString(CultureInfo.CurrentCulture))) : age.TotalHours >= 1 ? Text("workbench.history_hour", ("n", ((int)age.TotalHours).ToString(CultureInfo.CurrentCulture))) : age.TotalMinutes >= 1 ? Text("workbench.history_min", ("n", ((int)age.TotalMinutes).ToString(CultureInfo.CurrentCulture))) : Text("workbench.history_now") };
            time.Classes.Add("git-history-time");
            time.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(time, 4);
            grid.Children.Add(time);
            pick.Padding = new Thickness(12, 0);
            if (refs.Count == 0)
            {
                grid.ColumnDefinitions.RemoveAt(3);
                Grid.SetColumn(time, 3);
            }

            pick.Content = grid;
            AutomationProperties.SetName(pick, id + " " + subject);
            group.Children.Add(pick);
            group.Children.Add(detail);
            History.Children.Add(group);
        }

        if (rows.Count == 0 || (bool?)history["truncated"] == true)
        {
            History.Children.Add(Caption(Text(rows.Count == 0 ? "workbench.no_commits_yet" : "workbench.only_the_most_recent_commits")));
        }

        int Free()
        {
            var slot = waiting.IndexOf(null);
            if (slot >= 0)
            {
                return slot;
            }

            waiting.Add(null);
            return waiting.Count - 1;
        }
    }

    /// <summary>Reads the files in a selected commit and supports opening each exact historical patch.</summary>
    /// <param name="id">Stable kernel identifier.</param>
    /// <param name="detail">Inline commit detail container.</param>
    /// <returns>The file list in the selected commit.</returns>
    private async Task HistoryDetailAsync(string id, StackPanel detail)
    {
        try
        {
            var result = await _api.GitCommitDetailAsync(_repo, id, null, _token).ConfigureAwait(true);
            if (_token.IsCancellationRequested)
            {
                return;
            }

            detail.Children.Clear();
            foreach (var file in result["files"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                var path = (string)file["path"]!;
                var patch = new ContentControl { IsVisible = false };
                var button = FlatAction((string?)file["status"] + "  " + path + "  " + Counts(file), "workbench.commit-file", async () =>
                {
                    patch.IsVisible = !patch.IsVisible;
                    if (!patch.IsVisible || patch.Content is not null)
                    {
                        return;
                    }

                    try
                    {
                        var diff = await _api.GitCommitDetailAsync(_repo, id, path, _token).ConfigureAwait(true);
                        patch.Content = Patch(diff);
                    }
                    catch (Exception error)
                    {
                        if (!_token.IsCancellationRequested)
                        {
                            patch.IsVisible = false;
                            Report(error);
                        }
                    }
                });
                ToolTip.SetTip(button, (string?)file["oldPath"] is { } old ? old + " → " + path : path);
                detail.Children.Add(button);
                detail.Children.Add(patch);
            }

            if ((bool?)result["truncated"] == true)
            {
                detail.Children.Add(Caption(Text("workbench.the_staged_diff_is_long")));
            }
        }
        catch (Exception error)
        {
            if (!_token.IsCancellationRequested)
            {
                detail.IsVisible = false;
                Report(error);
            }
        }
    }

    /// <summary>Opens all changed files in the larger workbench canvas.</summary>
    private void ShowBoard()
    {
        CloseReview();
        _board ??= new GitChangesView();
        _board.UpdateTabCount(_changes.Length > 0 ? _changes.Length.ToString(CultureInfo.CurrentCulture) : null);
        _boardReview = _board.Review;
        _board.Classes.Add("git-board");
        DrawBoard(_board);
        _show(Text("common.changes"), _board);
    }

    /// <summary>Rebuilds the board with staged state from the latest read.</summary>
    /// <param name="board">The expanded changes canvas.</param>
    private void DrawBoard(GitChangesView board)
    {
        var prior = board.Files.Children.OfType<GitChangeRow>().ToDictionary(row => (string)row.Tag!, StringComparer.Ordinal);
        var desired = new List<Control>();
        var many = _changes.Length > 8;
        var untracked = _changes.Count(change => (string?)change["status"] == "??");
        var counts = Text(
            "workbench.modified_untracked",
            ("m", (_changes.Length - untracked).ToString(CultureInfo.CurrentCulture)),
            ("u", untracked.ToString(CultureInfo.CurrentCulture)));
        var summary = many ? $"{Count.Text} {counts}" : Count.Text ?? string.Empty;
        board.Summarize(
            Branch.Content as string,
            summary,
            "+" + _changes.Sum(change => (int?)change["insertions"] ?? 0).ToString(CultureInfo.CurrentCulture),
            "−" + _changes.Sum(change => (int?)change["deletions"] ?? 0).ToString(CultureInfo.CurrentCulture),
            _changes.Length == 0 ? Text("workbench.no_changes") : null);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var all = Action(Text(FullyStaged(_changes) ? "workbench.unstage_all" : "workbench.stage_all"), "workbench.stage-all", () => StageAsync(null));
        all.IsEnabled = _changes.Length > 0;
        actions.Children.Add(all);
        actions.Children.Add(Action(Text("workbench.commit"), "commit.draft", () => LoadReviewAsync(true, _boardReview)));
        if (!_remote)
        {
            actions.Children.Add(Action(Text("workbench.push_2"), "push.preview", () => LoadReviewAsync(false, _boardReview)));
        }

        foreach (var button in actions.Children.OfType<Button>())
        {
            button.Classes.Add("git-board-command");
        }

        board.Commands.Content = actions;
        _boardNotice = board.Notice;
        _boardNotice.Text = Failure.IsVisible ? Failure.Text : Outcome.Text;
        _boardNotice.IsVisible = Failure.IsVisible || Outcome.IsVisible;
        foreach (var change in _changes)
        {
            var path = (string)change["path"]!;
            var oldPath = (string?)change["oldPath"];
            var status = (string?)change["status"];
            var startsFolded = status is "A" or "??";
            var existing = prior.Remove(path, out var row);
            row ??= new GitChangeRow { Tag = path };
            var stage = row.IndexControl ?? StageBox([change], path, "workbench.stage", () => _changes.Where(current => (string?)current["path"] == path).ToArray());
            stage.IsChecked = FullyStaged([change]) ? true : (bool?)change["staged"] == true ? null : false;
            row.ShowFile(
                path,
                oldPath is null ? path : oldPath + " → " + path,
                string.IsNullOrEmpty(status) ? "M" : status,
                stage,
                (int?)change["insertions"] is int added ? "+" + added.ToString(CultureInfo.CurrentCulture) : null,
                (int?)change["deletions"] is int removed ? "−" + removed.ToString(CultureInfo.CurrentCulture) : null,
                startsFolded && many ? Text(status == "??" ? "workbench.tag_untracked" : "workbench.tag_new") : null,
                startsFolded && !many ? Text(status == "??" ? "workbench.untracked_open_to_see" : "workbench.new_file_open_to_see") : null);
            if (!existing)
            {
                row.Expanded = _expanded.TryGetValue(path, out var open) ? open : !startsFolded;
                row.OnOpen(Text("workbench.open_file"), () => _open(path));
                row.OnToggle(async expand =>
                {
                    row.Expanded = expand;
                    _expanded[path] = expand;
                    if (expand)
                    {
                        await row.LoadPatchAsync(ReadPatch, Caption(Text("common.loading"))).ConfigureAwait(true);
                    }
                });
            }

            row.OnDiscard(
                Text("workbench.discard"),
                Text("workbench.confirm_discard"),
                Text("workbench.discard_the_change_to_path", ("path", path)),
                Discardable(change) ? () => WriteAsync(() => _api.GitDiscardAsync(_repo, path, _token), path) : null);
            desired.Add(row);
            if (row.Expanded)
            {
                _ = row.LoadPatchAsync(ReadPatch, Caption(Text("common.loading")), refresh: true);
            }
            else
            {
                row.InvalidatePatch();
            }

            async Task<Control?> ReadPatch()
            {
                try
                {
                    var reply = await _api.GitDiffAsync(_repo, path, _token).ConfigureAwait(true);
                    return _token.IsCancellationRequested ? null : Patch(reply, path);
                }
                catch (Exception error)
                {
                    return _token.IsCancellationRequested ? null : Caption(_describe(error));
                }
            }
        }

        if (_changes.Length > 0)
        {
            var hint = Caption(Text("workbench.changes_page_hint"));
            hint.FontSize = 11;
            hint.Margin = new Thickness(12, 10);
            hint.FontWeight = FontWeight.Medium;
            hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("FaintBrush"));
            desired.Add(hint);
        }

        var retained = desired.ToHashSet();
        for (var index = board.Files.Children.Count - 1; index >= 0; index--)
        {
            if (!retained.Contains(board.Files.Children[index]))
            {
                board.Files.Children.RemoveAt(index);
            }
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var oldIndex = board.Files.Children.IndexOf(desired[index]);
            if (oldIndex < 0)
            {
                board.Files.Children.Insert(index, desired[index]);
            }
            else if (oldIndex != index)
            {
                board.Files.Children.Move(oldIndex, index);
            }
        }
    }
}
