using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>The changes, staging controls, review cards and commit graph for exactly one repository.</summary>
public sealed partial class GitRepositoryView : SettingsFormView
{
    /// <summary>Folders the user folded in the changes tree.</summary>
    private readonly HashSet<string> _folded = new(StringComparer.Ordinal);

    /// <summary>Expanded patch state survives authoritative status refreshes.</summary>
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);

    /// <summary>Owning session's kernel client.</summary>
    private StudioPageClient _api = null!;

    /// <summary>Repository identity relative to that workspace.</summary>
    private string _repo = string.Empty;

    /// <summary>Cancellation for this workspace binding.</summary>
    private CancellationToken _token;

    /// <summary>Releases pending reads when a repository disappears from the inventory.</summary>
    private CancellationTokenSource? _lifetime;

    /// <summary>Authoritative index and worktree rows.</summary>
    private JsonObject[] _changes = [];

    /// <summary>Ignores older reads when the user refreshes or changes repository.</summary>
    private long _generation;

    /// <summary>Available height for resizing review cards.</summary>
    private double _bodyHeight;

    /// <summary>Claims a canvas read across Git, ordinary files and repository sections.</summary>
    private Func<Func<bool>> _claim = null!;

    /// <summary>Identity for a pending commit/push review.</summary>
    private long _reviewRead;

    /// <summary>Prevents overlapping repository mutations.</summary>
    private bool _writing;

    /// <summary>Number of tree rows currently requested by the user.</summary>
    private int _rowLimit = 500;

    /// <summary>Whether a remote pane only exposes the supported change operations.</summary>
    private bool _remote;

    /// <summary>Shows a patch or a full changes page in the native workbench canvas.</summary>
    private Action<string, Control> _show = null!;

    /// <summary>Opens a repository-relative source file in the editor.</summary>
    private Func<string, Task> _open = null!;

    /// <summary>Refreshes the file list and safely invalidates a restored file's saved revision.</summary>
    private Func<string?, Task> _changed = null!;

    /// <summary>Localizes a kernel refusal.</summary>
    private Func<Exception, string> _describe = null!;

    /// <summary>View currently displaying the expanded changes board.</summary>
    private GitChangesView? _board;

    /// <summary>Review container retained across changes-board refreshes.</summary>
    private StackPanel? _boardReview;

    /// <summary>Visible feedback in the expanded changes view.</summary>
    private TextBlock? _boardNotice;

    /// <summary>Initializes a new instance of the <see cref="GitRepositoryView"/> class.</summary>
    public GitRepositoryView() => InitializeComponent();

    /// <summary>Notifies the workbench badge after an authoritative status read.</summary>
    internal event Action? StatusChanged;

    /// <summary>Gets the changed-file count for this repository.</summary>
    internal int ChangeCount => _changes.Length;

    /// <summary>Gets repository-relative paths and statuses for the workspace file tree.</summary>
    internal System.Collections.Generic.IEnumerable<(string Path, string Status)> FileStatuses => _changes.Select(c => ((string)c["path"]!, (string?)c["status"] ?? "M"));

    /// <summary>Gets the authoritative line totals for the composer summary.</summary>
    internal (int Added, int Removed) LineChanges => (_changes.Sum(row => (int?)row["insertions"] ?? 0), _changes.Sum(row => (int?)row["deletions"] ?? 0));

    /// <summary>Gets the directory to use on all reads and writes from this section.</summary>
    internal string Directory => _repo;

    /// <summary>Opens this repository's changes from the composer summary.</summary>
    internal void OpenChanges() => Board.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>Connects this new view instance to one repository.</summary>
    /// <param name="api">Runtime transport.</param>
    /// <param name="repo">Entry from the repository inventory.</param>
    /// <param name="several">Whether to display the repository heading.</param>
    /// <param name="remote">Whether this pane runs remotely.</param>
    /// <param name="language">Reader's language.</param>
    /// <param name="show">Opens content in the canvas.</param>
    /// <param name="open">Opens a repository-relative file.</param>
    /// <param name="claim">Starts a new canvas selection and returns its freshness check.</param>
    /// <param name="changed">Refreshes the rest of the workspace after a write.</param>
    /// <param name="describe">Localizes errors.</param>
    /// <param name="token">View lifetime.</param>
    /// <returns>The initial change list and history.</returns>
    internal async Task OpenAsync(StudioPageClient api, JsonObject repo, bool several, bool remote, InterfaceLanguage language, Action<string, Control> show, Func<string, Task> open, Func<Func<bool>> claim, Func<string?, Task> changed, Func<Exception, string> describe, CancellationToken token)
    {
        _api = api;
        _repo = (string?)repo["dir"] ?? string.Empty;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        _token = _lifetime.Token;
        _show = show;
        _open = open;
        _claim = claim;
        _changed = changed;
        _describe = describe;
        _remote = remote;
        Translate(language);
        var changesTitle = new TextBlock { Text = Text("common.changes"), VerticalAlignment = VerticalAlignment.Center };
        changesTitle.Bind(TextBlock.LineHeightProperty, Board.GetObservable(Button.FontSizeProperty));
        Board.Content = changesTitle;
        UpdateIdentity(repo, several);
        RepoToggle.Click += async (_, _) =>
        {
            Sections.IsVisible = !Sections.IsVisible;
            if (Sections.IsVisible)
            {
                await RefreshAsync().ConfigureAwait(true);
            }
        };
        Tag = _repo;
        Push.Content = Text("workbench.push") + "…";
        HistoryLabel.Text = Text("workbench.commit_history");
        AutomationProperties.SetName(HistoryTitle, HistoryLabel.Text);
        Branch.Content = (string?)repo["branch"];
        Branch.IsEnabled = _repo.Length == 0;
        Branch.Click += async (_, _) => await ReadBranchesAsync().ConfigureAwait(true);
        HistoryPanel.IsVisible = Push.IsVisible = !remote;
        Fold.Click += (_, _) =>
        {
            Changes.IsVisible = !Changes.IsVisible;
            ChangesFoldIcon.RenderTransform = new RotateTransform(Changes.IsVisible ? 0 : -90);
        };
        Board.Click += (_, _) => ShowBoard();
        StageAll.Click += async (_, _) => await StageAsync(null).ConfigureAwait(true);
        Draft.Click += async (_, _) => await ReviewCommitAsync().ConfigureAwait(true);
        Push.Click += async (_, _) => await ReviewPushAsync().ConfigureAwait(true);
        HistoryTitle.Click += async (_, _) =>
        {
            HistoryScroll.IsVisible = !HistoryScroll.IsVisible;
            HistoryFoldIcon.RenderTransform = new RotateTransform(HistoryScroll.IsVisible ? 0 : -90);
            if (HistoryScroll.IsVisible)
            {
                await RefreshAsync().ConfigureAwait(true);
            }
        };
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Updates the title independently of an outstanding review.</summary>
    /// <param name="repo">Current inventory record.</param>
    /// <param name="several">Whether the repository needs its own header.</param>
    internal void UpdateIdentity(JsonObject repo, bool several)
    {
        RepoToggle.Content = "⌄  " + ((string?)repo["name"] ?? _repo) + "   " + (string?)repo["branch"];
        RepoToggle.IsVisible = several;
        Branch.Content = (string?)repo["branch"];
    }

    /// <summary>Retires pending reads and dialogs when removing this repository.</summary>
    internal void Retire()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _board = null;
        StatusChanged = null;
    }

    /// <summary>Applies the explorer's share of the available height.</summary>
    /// <param name="height">Measured workbench body height.</param>
    internal void Fit(double height)
    {
        if (height > 0)
        {
            var fraction = Review.IsVisible ? 0.8 : 0.34;
            ChangeScroll.MaxHeight = Math.Floor(height * fraction * 64) / 64;
            var desktop = TopLevel.GetTopLevel(this)?.ClientSize.Width > 900;
            ChangeContent.Margin = new Thickness(0, 0, desktop ? 10 : 0, 0);
            _bodyHeight = height;
        }
    }

    /// <summary>Re-reads status and the visible history without discarding text being reviewed.</summary>
    /// <returns>The reads.</returns>
    internal async Task RefreshAsync()
    {
        var ticket = ++_generation;
        if (!Sections.IsVisible)
        {
            return;
        }

        try
        {
            var listing = await _api.GitChangesAsync(_repo, _token).ConfigureAwait(true);
            if (!Current(ticket))
            {
                return;
            }

            _changes = listing["changes"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
            Count.Text = Text("common.n_files", ("n", _changes.Length.ToString(CultureInfo.CurrentCulture)));
            StageAll.Content = Text(FullyStaged(_changes) ? "workbench.unstage_all" : "workbench.stage_all");
            StageAll.IsEnabled = Board.IsEnabled = _changes.Length > 0;
            DrawTree();
            StatusChanged?.Invoke();
            _board?.UpdateTabCount(_changes.Length > 0 ? _changes.Length.ToString(CultureInfo.CurrentCulture) : null);
            if (_board?.Parent is not null)
            {
                DrawBoard(_board);
            }

            if (!_remote && HistoryScroll.IsVisible)
            {
                var history = await _api.GitHistoryAsync(_repo, _token).ConfigureAwait(true);
                if (Current(ticket))
                {
                    DrawHistory(history);
                }
            }
        }
        catch (Exception error)
        {
            if (Current(ticket))
            {
                Report(error);
            }
        }
    }

    /// <summary>Returns whether all these paths are fully staged.</summary>
    /// <param name="changes">Paths under the control.</param>
    /// <returns>True only when no worktree changes remain in the set.</returns>
    private static bool FullyStaged(JsonObject[] changes) => changes.Length > 0 && changes.All(c => (bool?)c["staged"] == true && (bool?)c["unstaged"] != true);

    /// <summary>Checks eligibility using the same porcelain statuses as Electron.</summary>
    /// <param name="change">Changed-file entry.</param>
    /// <returns>Whether restoring this tracked path is supported.</returns>
    private static bool Discardable(JsonObject change) => string.IsNullOrEmpty((string?)change["oldPath"]) && ((string?)change["status"] ?? "M") is { Length: > 0 } status && status.All(c => c is 'M' or 'D' or 'T');

    /// <summary>Builds a keyboard-accessible action with a stable identity.</summary>
    /// <param name="label">Visible label.</param>
    /// <param name="id">Stable kernel identifier.</param>
    /// <param name="run">Runs the action selected by the reader.</param>
    /// <param name="flat">Use the borderless tree-row appearance.</param>
    /// <returns>The labeled control.</returns>
    private static Button Action(string label, string id, Func<Task> run, bool flat = false)
    {
        var button = new Button { Content = label };
        button.Classes.Add(flat ? "git-flat" : "git-tool");
        AutomationProperties.SetAutomationId(button, "action:" + id);
        button.Click += async (_, _) => await run().ConfigureAwait(true);
        return button;
    }

    /// <summary>Builds a borderless tree or history action.</summary>
    /// <param name="label">Visible label.</param>
    /// <param name="id">Reference action name.</param>
    /// <param name="run">Action selected by the user.</param>
    /// <returns>The interactive button.</returns>
    private static Button FlatAction(string label, string id, Func<Task> run) => Action(label, id, run, true);

    /// <summary>Restores changed-state counts without inventing values for binary files.</summary>
    /// <param name="c">The changed file.</param>
    /// <returns>Available insertion and deletion counts.</returns>
    private static string Counts(JsonObject c) => string.Join(" ", new[]
    {
        (int?)c["insertions"] is { } added ? "+" + added.ToString(CultureInfo.CurrentCulture) : null,
        (int?)c["deletions"] is { } removed ? "−" + removed.ToString(CultureInfo.CurrentCulture) : null,
    }.Where(v => v is not null));

    /// <summary>Creates the shared sidebar and changes-board staging control.</summary>
    /// <param name="changes">Authoritative paths represented by the control.</param>
    /// <param name="path">Accessible path label.</param>
    /// <param name="id">Canonical action.</param>
    /// <returns>The host-driven three-state checkbox.</returns>
    private GitStageBox StageBox(JsonObject[] changes, string path, string id)
    {
        var box = new GitStageBox { IsChecked = FullyStaged(changes) ? true : changes.Any(c => (bool?)c["staged"] == true) ? null : false };
        AutomationProperties.SetAutomationId(box, "action:" + id);
        AutomationProperties.SetName(box, Text("workbench.stage_path", ("path", path)));
        box.Click += async (_, _) => await StageAsync(changes).ConfigureAwait(true);
        return box;
    }

    /// <summary>Checks whether the read still belongs to this mounted generation.</summary>
    /// <param name="ticket">The generation which issued this read.</param>
    /// <returns>Whether the read is still current.</returns>
    private bool Current(long ticket) => !_token.IsCancellationRequested && ticket == _generation;

    /// <summary>Reports a refusal inside the repository that produced it.</summary>
    /// <param name="error">The refusal from the kernel.</param>
    private void Report(Exception error)
    {
        Failure.Text = _describe(error);
        Failure.IsVisible = true;
        if (_boardNotice is not null)
        {
            _boardNotice.Text = Failure.Text;
            _boardNotice.IsVisible = true;
        }
    }

    /// <summary>Runs a mutation once and rereads authoritative state on success.</summary>
    /// <param name="write">The confirmed repository mutation.</param>
    /// <param name="restored">A restored path whose editor revision should be invalidated.</param>
    /// <returns>The mutation and subsequent readback.</returns>
    private async Task WriteAsync(Func<Task> write, string? restored = null)
    {
        if (_writing || _token.IsCancellationRequested)
        {
            return;
        }

        _writing = true;
        Sections.IsEnabled = false;
        if (_board is not null)
        {
            _board.IsEnabled = false;
        }

        Failure.IsVisible = false;
        try
        {
            await write().ConfigureAwait(true);
            if (!_token.IsCancellationRequested)
            {
                await RefreshAsync().ConfigureAwait(true);
                await _changed(restored is null || _repo.Length == 0 ? restored : _repo + "/" + restored).ConfigureAwait(true);
            }
        }
        catch (Exception error)
        {
            if (!_token.IsCancellationRequested)
            {
                Report(error);
            }
        }
        finally
        {
            _writing = false;
            Sections.IsEnabled = true;
            if (_board is not null)
            {
                _board.IsEnabled = true;
            }
        }
    }

    /// <summary>Stages the selected paths, or unstages a set already wholly in the index.</summary>
    /// <param name="selected">Selected paths, or null for the entire list.</param>
    /// <returns>The index update.</returns>
    private Task StageAsync(JsonObject[]? selected)
    {
        var rows = selected ?? _changes;
        var paths = selected?.Select(c => (string)c["path"]!).ToArray();
        return WriteAsync(() => _api.GitStageAsync(_repo, paths, FullyStaged(rows), _token));
    }

    /// <summary>Renders compact folders first, then files, preserving individual index and worktree states.</summary>
    private void DrawTree()
    {
        Changes.Children.Clear();
        var drawn = 0;
        var more = false;
        Walk(_changes, string.Empty, 0);
        if (more)
        {
            Changes.Children.Add(Action(Text("workbench.show_n_more", ("n", "500")), "workbench.more-changes", () =>
            {
                _rowLimit += 500;
                DrawTree();
                return Task.CompletedTask;
            }));
        }

        void Walk(JsonObject[] entries, string prefix, int depth)
        {
            var parts = entries.GroupBy(c => ((string)c["path"]!)[prefix.Length..].Split('/')[0]).ToArray();
            foreach (var part in parts.OrderByDescending(p => p.Any(c => (string)c["path"]! != prefix + p.Key)).ThenBy(p => p.Key, StringComparer.Ordinal))
            {
                if (drawn >= _rowLimit)
                {
                    more = true;
                    return;
                }

                drawn++;
                var list = part.ToArray();
                var path = prefix + part.Key;
                var folder = list.Any(c => (string)c["path"]! != path);
                var label = part.Key;
                if (folder)
                {
                    while (true)
                    {
                        var children = list.Select(c => ((string)c["path"]!)[(path.Length + 1)..]).ToArray();
                        var next = children[0].Split('/')[0];
                        if (!children.All(p => p.StartsWith(next + "/", StringComparison.Ordinal)))
                        {
                            break;
                        }

                        path += "/" + next;
                        label += "/" + next;
                    }
                }

                var row = new Grid { Height = 26, ColumnDefinitions = new ColumnDefinitions("24,*,Auto") };
                row.Classes.Add("git-change-item");
                var stage = StageBox(list, path, folder ? "workbench.stage-folder" : "workbench.stage");
                stage.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(stage);
                var name = FlatAction(folder ? (_folded.Contains(path) ? "› " : "⌄ ") + label : (string?)list[0]["status"] + " " + label, folder ? "workbench.change-folder" : "workbench.diff", async () =>
                {
                    if (folder)
                    {
                        if (!_folded.Add(path))
                        {
                            _folded.Remove(path);
                        }

                        DrawTree();
                    }
                    else
                    {
                        await OpenPatchAsync(path).ConfigureAwait(true);
                    }
                });
                name.HorizontalAlignment = HorizontalAlignment.Stretch;
                name.Classes.Add("git-change-name");
                name.HorizontalContentAlignment = HorizontalAlignment.Left;
                name.Height = 26;
                name.Padding = new Thickness(6 + (depth * 12), 0, 10, 0);
                var copy = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 7 };
                Control leading;
                if (folder)
                {
                    var shape = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(_folded.Contains(path) ? "m9 6 6 6-6 6" : "M6 9l6 6 6-6"), StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
                    shape.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, shape.GetResourceObservable("FaintBrush"));
                    var canvas = new Canvas { Width = 24, Height = 24 };
                    canvas.Children.Add(shape);
                    leading = new Viewbox { Width = 12, Height = 12, Child = canvas };
                }
                else
                {
                    var status = (string?)list[0]["status"] ?? "M";
                    var marker = new TextBlock { Text = status, Width = 15, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    marker.Classes.Add(status is "A" or "??" ? "git-added" : status == "D" ? "git-removed" : "git-status");
                    leading = marker;
                }

                copy.Children.Add(leading);
                var title = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                title.Classes.Add("git-path");
                Grid.SetColumn(title, 1);
                copy.Children.Add(title);
                var counts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
                if (folder)
                {
                    var count = new TextBlock { Text = list.Length.ToString(CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center };
                    count.Classes.Add("git-folder-count");
                    counts.Children.Add(count);
                }

                foreach (var (field, symbol, role) in new[] { ("insertions", "+", "git-added"), ("deletions", "-", "git-removed") })
                {
                    var measured = list.Where(c => c[field] is not null).Select(c => (int)c[field]!).ToArray();
                    if (measured.Length > 0)
                    {
                        var number = new TextBlock { Text = symbol + measured.Sum().ToString(CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center };
                        number.Classes.Add(role);
                        counts.Children.Add(number);
                    }
                }

                Grid.SetColumn(counts, 2);
                copy.Children.Add(counts);
                name.Content = copy;
                ToolTip.SetTip(name, path);
                AutomationProperties.SetName(name, path);
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var item = new StackPanel();
                item.Children.Add(row);
                if (!folder && Discardable(list[0]))
                {
                    var discard = FlatAction("↶", "workbench.discard", () =>
                    {
                        AskDiscard(item, path);
                        return Task.CompletedTask;
                    });
                    AutomationProperties.SetName(discard, Text("workbench.discard") + " " + path);
                    discard.Classes.Add("git-discard");
                    discard.Width = discard.Height = 22;
                    discard.Margin = new Thickness(0, 0, 6, 0);
                    Grid.SetColumn(discard, 2);
                    row.Children.Add(discard);
                }

                Changes.Children.Add(item);
                if (folder && !_folded.Contains(path))
                {
                    Walk(list, path + "/", depth + 1);
                }
            }
        }
    }

    /// <summary>Requires a second explicit click before restoring a file from HEAD.</summary>
    /// <param name="item">The row containing the file.</param>
    /// <param name="path">Workspace-relative file path.</param>
    private void AskDiscard(StackPanel item, string path)
    {
        if (item.Children.OfType<Border>().Any())
        {
            return;
        }

        var body = new StackPanel { Spacing = 4 };
        var confirm = new Border { Child = body, Padding = new Thickness(4) };
        body.Children.Add(Caption(Text("workbench.discard") + " · " + path));
        body.Children.Add(Action(Text("workbench.discard"), "workbench.discard-confirm", () => WriteAsync(() => _api.GitDiscardAsync(_repo, path, _token), path)));
        body.Children.Add(Action(Text("common.cancel"), "workbench.discard-cancel", () =>
        {
            item.Children.Remove(confirm);
            return Task.CompletedTask;
        }));
        item.Children.Add(confirm);
    }

    /// <summary>Opens a working-tree patch in the canvas.</summary>
    /// <param name="path">Workspace-relative file path.</param>
    /// <returns>The diff read.</returns>
    private async Task OpenPatchAsync(string path)
    {
        var current = _claim();
        try
        {
            var data = await _api.GitDiffAsync(_repo, path, _token).ConfigureAwait(true);
            if (current() && !_token.IsCancellationRequested)
            {
                _show(path, Patch(data, path));
            }
        }
        catch (Exception error) when (!_token.IsCancellationRequested)
        {
            Report(error);
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
        }
    }

    /// <summary>Displays the supplied patch and any truncation notice.</summary>
    /// <returns>The completed operation.</returns>
    /// <param name="data">The kernel patch and truncation flag.</param>
    /// <param name="path">Optional working file to open from a line number.</param>
    private Control Patch(JsonObject data, string? path = null)
    {
        var view = new GitPatchView();
        view.Show(
            (string?)data["diff"] ?? string.Empty,
            path ?? (string?)data["path"],
            Text("workbench.no_changes"),
            (bool?)data["truncated"] == true ? Text("workbench.diff_cut_short") : null,
            path is null ? null : () => _open(path),
            showMetadata: path is null);
        return view;
    }
}
