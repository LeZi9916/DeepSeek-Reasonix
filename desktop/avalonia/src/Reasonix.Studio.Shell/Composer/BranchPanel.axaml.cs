using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using Path = Avalonia.Controls.Shapes.Path;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>The composer's self-drawn local-branch palette.</summary>
public sealed partial class BranchPanel : UserControl
{
    /// <summary>The branch rows the last read supplied.</summary>
    private IReadOnlyList<BranchChoice> _branches = [];

    /// <summary>The branch the workspace is on.</summary>
    private string _current = string.Empty;

    /// <summary>The workspace the branch list belongs to.</summary>
    private string _workspaceRoot = string.Empty;

    /// <summary>The language the labels are drawn in.</summary>
    private InterfaceLanguage _language = InterfaceLanguage.Zh;

    /// <summary>The branch a switch is currently applying.</summary>
    private string? _switching;

    /// <summary>The ordinary branch rows retained while the worktree mode is showing.</summary>
    private IReadOnlyList<BranchChoice> _ordinary = [];

    /// <summary>Branches that may seed an isolated worktree, when the host offers them.</summary>
    private WorktreeSources? _sources;

    /// <summary>Whether the palette is offering isolated worktree sources.</summary>
    private bool _isolated;

    /// <summary>Initializes a new instance of the <see cref="BranchPanel"/> class.</summary>
    public BranchPanel()
    {
        InitializeComponent();
        Search.TextChanged += (_, _) => Draw();
        Close.Click += (_, _) => CloseRequested?.Invoke();
        Retry.Click += (_, _) => RetryRequested?.Invoke();
        CurrentFolder.Click += (_, _) => SetWorktreeMode(false);
        IsolatedMode.Click += (_, _) => SetWorktreeMode(true);
        CopyPath.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(_workspaceRoot);
            }
        };
    }

    /// <summary>Raised when the reader chose a branch.</summary>
    public event Action<string>? Picked;

    /// <summary>Raised when the reader asked to read the branches again.</summary>
    public event Action? RetryRequested;

    /// <summary>Raised when the reader chose a branch to seed an isolated worktree.</summary>
    public event Action<string>? IsolateRequested;

    /// <summary>Raised when the reader dismissed the palette.</summary>
    public event Action? CloseRequested;

    /// <summary>Shows the palette with a fresh branch list.</summary>
    /// <param name="branches">Local branches the kernel listed.</param>
    /// <param name="current">The branch the workspace is on.</param>
    /// <param name="workspaceRoot">The project folder the branches belong to.</param>
    /// <param name="language">Language for the labels.</param>
    /// <param name="sources">Worktree branches the host offers, when available.</param>
    public void Show(
        IReadOnlyList<BranchChoice> branches,
        string current,
        string workspaceRoot,
        InterfaceLanguage language,
        WorktreeSources? sources = null)
    {
        _branches = branches;
        _ordinary = branches;
        _sources = sources;
        _isolated = false;
        _current = current;
        _workspaceRoot = workspaceRoot;
        _language = language;
        _switching = null;
        Title.Text = MessageCatalog.Get("composer.configure_workspace", language);
        WorkspaceName.Text = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(workspaceRoot));
        WorkspacePath.Text = workspaceRoot;
        ToolTip.SetTip(WorkspaceName, workspaceRoot);
        ToolTip.SetTip(WorkspacePath, workspaceRoot);
        Description.Text = MessageCatalog.Get("session.work_directly_in_the_opened", language);
        Search.PlaceholderText = MessageCatalog.Get("composer.search_local_branches", language);
        Note.Text = MessageCatalog.Get("composer.switches_immediately_other_sessions_in", language);
        Retry.Content = MessageCatalog.Get("common.retry", language);
        CopyPath.Content = MessageCatalog.Get("common.copy", language);
        ToolTip.SetTip(CopyPath, MessageCatalog.Get("common.copy_path", language));
        AutomationProperties.SetName(Close, MessageCatalog.Get("composer.close_workspace_configuration", language));
        ErrorBox.IsVisible = false;
        RowsScroll.IsVisible = true;
        WorktreeModes.IsVisible = sources?.Available == true;
        CurrentFolder.Content = MessageCatalog.Get("composer.current_folder", language);
        IsolatedMode.Content = MessageCatalog.Get("common.isolated_worktree", language);
        CurrentFolder.Classes.Set("active", !_isolated);
        IsolatedMode.Classes.Set("active", _isolated);
        WorktreeBadge.IsVisible = false;
        WorktreeBadge.Text = MessageCatalog.Get("common.isolated_worktree", language);
        SourceLabel.IsVisible = false;
        SourceLabel.Text = MessageCatalog.Get("common.source_branch", language);
        Draw();
    }

    /// <summary>Marks the branch a switch is applying and disables the others.</summary>
    /// <param name="branch">Branch being switched to, or empty before a write.</param>
    public void SetPending(string? branch)
    {
        _switching = branch;
        Note.Text = string.IsNullOrEmpty(branch)
            ? MessageCatalog.Get("composer.switches_immediately_other_sessions_in", _language)
            : MessageCatalog.Get("composer.switching_to_branch", _language, ("branch", branch));
        Draw();
    }

    /// <summary>Shows a read or switch refusal where the choices would be.</summary>
    /// <param name="message">Text the kernel or transport supplied.</param>
    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBox.IsVisible = true;
        RowsScroll.IsVisible = false;
    }

    /// <summary>Switches between ordinary branches and worktree sources.</summary>
    /// <param name="isolated">Whether worktree sources should be listed.</param>
    private void SetWorktreeMode(bool isolated)
    {
        if (isolated && _sources?.Available != true)
        {
            return;
        }

        _isolated = isolated;
        CurrentFolder.Classes.Set("active", !isolated);
        IsolatedMode.Classes.Set("active", isolated);
        WorktreeBadge.IsVisible = isolated;
        WorktreeBadge.Text = MessageCatalog.Get("common.isolated_worktree", _language);
        SourceLabel.IsVisible = isolated;
        SourceLabel.Text = MessageCatalog.Get("common.source_branch", _language);
        _branches = isolated
            ? _sources!.Sources.Select(source => new BranchChoice(source.Name, source.Current, string.Empty, source.Remote)).ToList()
            : _ordinary;
        Description.Text = MessageCatalog.Get(
            isolated ? "composer.create_an_isolated_worktree_when" : "session.work_directly_in_the_opened",
            _language);
        Search.PlaceholderText = MessageCatalog.Get(
            isolated ? "composer.search_source_branches" : "composer.search_local_branches",
            _language);
        Draw();
    }

    /// <summary>Rebuilds rows for the current query and pending state.</summary>
    private void Draw()
    {
        Rows.Children.Clear();
        var query = Search.Text?.Trim() ?? string.Empty;
        var shown = _branches
            .Where(branch => query.Length == 0 || branch.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(branch => branch.Name == _current)
            .ToList();
        if (_isolated)
        {
            foreach (var remote in new[] { false, true })
            {
                var group = shown.Where(branch => branch.Remote == remote).ToList();
                if (group.Count == 0)
                {
                    continue;
                }

                var heading = new TextBlock
                {
                    Text = MessageCatalog.Get(remote ? "composer.remote_branches" : "composer.local_branches", _language),
                };
                heading.Classes.Add("branch-group");
                Rows.Children.Add(heading);
                foreach (var branch in group)
                {
                    Rows.Children.Add(Row(branch));
                }
            }
        }
        else
        {
            foreach (var branch in shown)
            {
                Rows.Children.Add(Row(branch));
            }
        }

        if (shown.Count == 0)
        {
            Rows.Children.Add(new TextBlock
            {
                Text = MessageCatalog.Get("composer.no_matching_branches", _language),
                Classes = { "palette-empty" },
            });
        }

        ErrorBox.IsVisible = false;
        RowsScroll.IsVisible = true;
    }

    /// <summary>Builds one branch row.</summary>
    /// <param name="branch">Branch to draw.</param>
    /// <returns>The row.</returns>
    private Button Row(BranchChoice branch)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("14,*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(new Path
        {
            Data = Geometry.Parse("M6 3v12 M6 15a3 3 0 1 0 0 6a3 3 0 1 0 0-6 M18 3a3 3 0 1 0 0 6a3 3 0 1 0 0-6 M18 9a9 9 0 0 1-9 9"),
            Stroke = Brush("FaintBrush"),
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = 13,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var name = new TextBlock { Text = branch.Name, VerticalAlignment = VerticalAlignment.Center };
        name.Classes.Add("branch-name");
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var state = branch.Worktree.Length > 0
            ? MessageCatalog.Get("composer.used_by_another_worktree", _language)
            : _isolated && branch.Current
                ? MessageCatalog.Get("composer.source", _language)
                : branch.Name == _current
                    ? MessageCatalog.Get("common.now", _language)
                    : string.Empty;
        if (state.Length > 0)
        {
            var label = new TextBlock { Text = state, VerticalAlignment = VerticalAlignment.Center };
            label.Classes.Add("branch-badge-text");
            var badge = new Border { Child = label, VerticalAlignment = VerticalAlignment.Center };
            badge.Classes.Add("branch-badge");
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        var button = new Button
        {
            Content = grid,
            Tag = branch.Name,
            IsEnabled = _switching is null && (_isolated || (branch.Name != _current && branch.Worktree.Length == 0)),
        };
        button.Classes.Add("branch-row");
        button.Classes.Set("current", branch.Name == _current);
        AutomationProperties.SetName(button, branch.Name);
        button.Click += (_, _) =>
        {
            if (_isolated)
            {
                var reference = _sources?.Sources.FirstOrDefault(source => source.Name == branch.Name)?.Ref ?? branch.Name;
                IsolateRequested?.Invoke(reference);
            }
            else
            {
                Picked?.Invoke(branch.Name);
            }
        };
        return button;
    }

    /// <summary>Reads a brush from the window's theme resources.</summary>
    /// <param name="key">Resource key.</param>
    /// <returns>The brush, or a muted fallback.</returns>
    private IBrush Brush(string key) =>
        this.TryFindResource(key, out var value) && value is IBrush brush ? brush : Brushes.Gray;
}
