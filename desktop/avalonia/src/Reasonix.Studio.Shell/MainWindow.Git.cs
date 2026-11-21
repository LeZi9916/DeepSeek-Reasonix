using Avalonia.Controls;
using Avalonia.Input.Platform;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FileEntry = Reasonix.Studio.Shell.Workbench.WorkspaceFileEntry;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Lifetime of widgets and pending responses for this workspace or runtime.</summary>
    private CancellationTokenSource? _gitLifetime;

    /// <summary>Runtime for which the Git widgets were mounted.</summary>
    private StudioPageClient? _gitApi;

    /// <summary>Discards late repository discovery results.</summary>
    private long _gitDiscovery;

    /// <summary>Refreshes Git status once on a turn's transition to idle.</summary>
    private bool _gitWasRunning;

    /// <summary>Last Git canvas in this workspace, available beside the file tabs.</summary>
    private (string Title, Control View)? _gitSurface;

    /// <summary>Connects the workbench surface launcher and file menu.</summary>
    private void SetUpWorkspaceMenus()
    {
        Label(FullWorkbench, "workbench.expand_workbench");
        Label(NewSurface, "workbench.new_tab");
        Label(FileMore, "common.more");
        var create = new MenuFlyout();
        var browse = new MenuItem { Header = MessageCatalog.Get(MessageKey.Workbench.Browser, Language) };
        browse.Click += (_, _) => OpenManualBrowser();
        var terminal = new MenuItem { Header = MessageCatalog.Get(MessageKey.Workbench.Terminal, Language) };
        terminal.Click += OnTerminals;
        create.Items.Add(browse);
        create.Items.Add(terminal);
        NewSurface.Flyout = create;
        FileMore.Click += (_, _) =>
        {
            if (_openFile is not { } file)
            {
                return;
            }

            var path = file.Original.Path;
            var root = file.Original.Root ?? _workspaceRoot;
            var menu = new MenuFlyout();
            var copy = new MenuItem { Header = MessageCatalog.Get("common.copy_path", Language) };
            copy.Click += async (_, _) =>
            {
                try
                {
                    if (Clipboard is not null)
                    {
                        await Clipboard.SetTextAsync(path).ConfigureAwait(true);
                    }
                }
                catch (Exception error)
                {
                    ReportWorkbenchError(error);
                }
            };
            menu.Items.Add(copy);
            if (_engine?.IsRemote != true)
            {
                var host = new Host.AvaloniaHost(this);
                var show = new MenuItem { Header = MessageCatalog.Get(OperatingSystem.IsMacOS() ? "workbench.reveal_in_finder" : OperatingSystem.IsWindows() ? "workbench.reveal_in_file_explorer" : "workbench.show_in_file_manager", Language) };
                show.Click += async (_, _) =>
                {
                    if (await host.RevealPathAsync(root, path).ConfigureAwait(true) is { } refusal)
                    {
                        GitError.Text = refusal.Error;
                        GitError.IsVisible = true;
                    }
                };
                menu.Items.Add(show);
            }

            menu.ShowAt(FileMore);
        };
    }

    /// <summary>Closes one workspace's Git widgets before a new runtime can receive input.</summary>
    private void ResetGit()
    {
        _gitDiscovery++;
        _gitLifetime?.Cancel();
        _gitLifetime?.Dispose();
        _gitLifetime = null;
        _gitApi = null;
        if (_gitSurface is { } surface && ReferenceEquals(surface.View, RenderedFile.Content))
        {
            ReleasePreview();
            FileCanvas.RowDefinitions[0].Height = new GridLength(40);
        }

        _gitSurface = null;
        foreach (var view in GitRepositories.Children.OfType<GitRepositoryView>())
        {
            view.Retire();
        }

        GitRepositories.Children.Clear();
        UpdateGitBadge();
        GitError.IsVisible = false;
    }

    /// <summary>Reads the local repository inventory, with a retry and workspace fallback on discovery failure.</summary>
    /// <returns>The visible sections' status and history reads.</returns>
    private async Task LoadGitAsync()
    {
        if (_engine is not { } engine || !WorkbenchPanel.IsVisible)
        {
            return;
        }

        var api = engine.Pages;
        if (!ReferenceEquals(_gitApi, api))
        {
            ResetGit();
            _gitApi = api;
            _gitLifetime = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        }

        var token = _gitLifetime!.Token;
        var ticket = ++_gitDiscovery;
        GitError.IsVisible = false;
        JsonObject[] repos;
        try
        {
            var inventory = await api.GitRepositoriesAsync(token).ConfigureAwait(true);
            repos = (inventory["repos"]?.AsArray().OfType<JsonObject>() ?? []).ToArray();
            if (ticket != _gitDiscovery || token.IsCancellationRequested)
            {
                return;
            }

            if ((bool?)inventory["truncated"] == true)
            {
                GitError.Text = MessageCatalog.Get("common.more", Language) + "…";
                GitError.IsVisible = true;
            }
        }
        catch (Exception error)
        {
            if (ticket != _gitDiscovery || token.IsCancellationRequested)
            {
                return;
            }

            GitError.Text = DescribeError(error);
            GitError.IsVisible = true;
            repos = [new JsonObject { ["dir"] = string.Empty }];
        }

        var previous = GitRepositories.Children.OfType<GitRepositoryView>().ToDictionary(view => view.Directory, StringComparer.Ordinal);
        foreach (var view in previous.Values.Where(view => !repos.Any(r => (string?)r["dir"] == view.Directory)))
        {
            view.Retire();
            GitRepositories.Children.Remove(view);
        }

        var several = repos.Length > 1 || repos.FirstOrDefault()?["dir"]?.ToString().Length > 0;
        foreach (var repo in repos)
        {
            if (ticket != _gitDiscovery || token.IsCancellationRequested)
            {
                return;
            }

            var name = (string?)repo["dir"] ?? string.Empty;
            if (previous.TryGetValue(name, out var existing))
            {
                existing.UpdateIdentity(repo, several);
                await existing.RefreshAsync().ConfigureAwait(true);
                continue;
            }

            var view = new GitRepositoryView();
            view.StatusChanged += () =>
            {
                UpdateGitBadge();
                DrawFileTree();
            };
            GitRepositories.Children.Add(view);
            view.Fit(WorkspaceBody.Bounds.Height);
            await view.OpenAsync(api, repo, several, engine.IsRemote, Language, ShowGitCanvas, path => OpenEntryAsync(new FileEntry(name.Length == 0 ? path : name + "/" + path, false)), ClaimGitCanvas, AfterGitWrite, DescribeError, token).ConfigureAwait(true);
        }

        UpdateGitBadge();

        async Task AfterGitWrite(string? restored)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (restored is not null && _fileBuffers.TryGetValue((_workspaceRoot, restored), out var buffer) && !buffer.Dirty && !buffer.Saving)
            {
                _fileBuffers.Remove((_workspaceRoot, restored));
                if (ReferenceEquals(buffer, _openFile))
                {
                    await OpenEntryAsync(new FileEntry(restored, false)).ConfigureAwait(true);
                }

                RebuildTabs();
            }

            await ListFilesAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Orders patch reads with regular file selections in the same window.</summary>
    /// <returns>A check for the still-current selection.</returns>
    private Func<bool> ClaimGitCanvas()
    {
        var ticket = ++_fileRead;
        return () => ticket == _fileRead;
    }

    /// <summary>Shows a Git view without destroying any underlying file drafts.</summary>
    /// <param name="title">Canvas title.</param>
    /// <param name="view">Git patch or changes board.</param>
    private void ShowGitCanvas(string title, Control view)
    {
        _fileRead++;
        HideBrowser();
        _openFile = null;
        SelectWorkspaceFile(null);
        _gitSurface = (title, view);
        if (view.Classes.Contains("git-board"))
        {
            ExplorerPane.IsVisible = false;
        }

        FileCanvas.RowDefinitions[0].Height = new GridLength(view.Classes.Contains("git-board") ? 0 : 40);
        FileMore.IsVisible = false;
        ShowPreview(view);
        WorkbenchTitle.Text = title;
        FileModes.IsVisible = false;
        SaveFile.IsVisible = false;
        RebuildTabs();
    }

    /// <summary>Locates the deepest repository that owns a workspace file.</summary>
    /// <param name="path">Path relative to the workspace.</param>
    /// <returns>Repository directory and path inside it.</returns>
    private (string Repo, string Path) LocateRepository(string path)
    {
        var repo = GitRepositories.Children.OfType<GitRepositoryView>().Select(view => view.Directory)
            .Where(dir => dir.Length > 0 && path.StartsWith(dir + "/", StringComparison.Ordinal)).OrderByDescending(dir => dir.Length).FirstOrDefault();
        return repo is null ? (string.Empty, path) : (repo, path[(repo.Length + 1)..]);
    }

    /// <summary>Displays the total number of changed files from the current repository reads.</summary>
    private void UpdateGitBadge()
    {
        var count = GitRepositories.Children.OfType<GitRepositoryView>().Sum(view => view.ChangeCount);
        GitCountBadge.IsVisible = count > 0;
        TotalGitCount.Text = count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    /// <summary>Refreshes repository state when the agent finishes writing.</summary>
    /// <param name="running">Whether the current turn is active.</param>
    private void RefreshGitAfterTurn(bool running)
    {
        if (_gitWasRunning && !running && WorkbenchPanel.IsVisible)
        {
            _ = LoadGitAsync();
        }

        _gitWasRunning = running;
    }
}
