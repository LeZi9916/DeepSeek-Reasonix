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
        ChangedFiles.Click += async (_, _) =>
        {
            await OpenWorkbenchAsync().ConfigureAwait(true);
            GitRepositories.Children.OfType<GitRepositoryView>().FirstOrDefault(view => view.ChangeCount > 0)?.OpenChanges();
        };
        Label(FullWorkbench, "workbench.expand_workbench");
        Label(NewSurface, "workbench.new_tab");
        Label(FileMore, "common.more");
        GitRetry.Content = MessageCatalog.Get("common.read_again", Language);
        GitRetry.Click += async (_, _) => await LoadGitAsync().ConfigureAwait(true);
        FileMore.Click += (_, _) =>
        {
            if (_openFile is not { } file)
            {
                return;
            }

            var menu = BuildWorkspaceFileMenu(new FileEntry(file.Original.Path, false));
            FileMore.Flyout = menu;
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
        GitRetry.IsVisible = false;
    }

    /// <summary>Reads the local repository inventory, with a retry and workspace fallback on discovery failure.</summary>
    /// <returns>The visible sections' status and history reads.</returns>
    private async Task LoadGitAsync()
    {
        if (_engine is not { } engine)
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
        GitRetry.IsVisible = false;
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
            GitRetry.IsVisible = true;
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
            await view.OpenAsync(
                api,
                repo,
                several,
                engine.IsRemote,
                Language,
                (title, surface) =>
                {
                    if (ReferenceEquals(_engine, engine))
                    {
                        ShowGitCanvas(title, surface);
                    }
                },
                path => ReferenceEquals(_engine, engine) ? OpenEntryAsync(new FileEntry(name.Length == 0 ? path : name + "/" + path, false)) : Task.CompletedTask,
                () => ReferenceEquals(_engine, engine) ? ClaimGitCanvas() : () => false,
                AfterGitWrite,
                DescribeError,
                token).ConfigureAwait(true);
        }

        UpdateGitBadge();

        async Task AfterGitWrite(string? restored)
        {
            if (token.IsCancellationRequested || !ReferenceEquals(_engine, engine))
            {
                return;
            }

            if (restored is not null && _fileBuffers.TryGetValue((WorkspaceBufferScope, restored), out var buffer) && !buffer.Dirty && !buffer.Saving)
            {
                _fileBuffers.Remove((WorkspaceBufferScope, restored));
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
        SetWorkbenchLauncher(false);
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
        SetFileBarTitle(title, path: false);
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
        ChangedFiles.IsVisible = count > 0;
        ChangedFilesLabel.Text = MessageCatalog.Get("composer.n_files_changed", Language, ("n", TotalGitCount.Text));
        ChangedAdded.Text = "+" + GitRepositories.Children.OfType<GitRepositoryView>().Sum(view => view.LineChanges.Added).ToString(System.Globalization.CultureInfo.CurrentCulture);
        ChangedRemoved.Text = "−" + GitRepositories.Children.OfType<GitRepositoryView>().Sum(view => view.LineChanges.Removed).ToString(System.Globalization.CultureInfo.CurrentCulture);
        ComposeBranch.IsVisible = GitRepositories.Children.Count > 0;
        ComposeBranchLabel.Text = GitRepositories.Children.OfType<GitRepositoryView>().FirstOrDefault()?.FindControl<Button>("Branch")?.Content?.ToString() ?? string.Empty;
        LabelComposeLayout();
    }

    /// <summary>Refreshes repository state when the agent finishes writing.</summary>
    /// <param name="running">Whether the current turn is active.</param>
    private void RefreshGitAfterTurn(bool running)
    {
        if (_gitWasRunning && !running)
        {
            _ = LoadGitAsync();
            _ = LoadComposeTelemetryAsync();
        }

        _gitWasRunning = running;
    }
}
