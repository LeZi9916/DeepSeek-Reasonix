using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages.Rail;

public sealed partial class ProjectRail
{
    /// <summary>Local rows currently displayed, matching the reference's selectable population.</summary>
    private readonly List<TreeSession> _displayed = [];

    /// <summary>Selection belongs to displayed canonical paths and survives refused writes.</summary>
    private HashSet<string>? _selection;

    /// <summary>The current acknowledged batch operation.</summary>
    private Task? _batch;

    /// <summary>Raised to run existing single-session operations and return paths that were refused.</summary>
    public event Func<string, IReadOnlyList<TreeSession>, Task<IReadOnlyList<string>>>? BatchRequested;

    /// <summary>Connects the persistent batch controls once.</summary>
    private void InitializeBatch()
    {
        BatchEnd.Click += (_, _) =>
        {
            _selection = null;
            Rebuild();
        };
        BatchAll.Click += (_, _) =>
        {
            if (_selection is null)
            {
                return;
            }

            var all = _displayed.All(row => _selection.Contains(row.Path));
            foreach (var row in _displayed)
            {
                if (all)
                {
                    _selection.Remove(row.Path);
                }
                else
                {
                    _selection.Add(row.Path);
                }
            }

            Rebuild();
        };
        BatchArchive.Click += async (_, _) => await RunBatchAsync(_displayed.Where(row => _selection?.Contains(row.Path) == true).All(row => row.Archived) ? "restore" : "archive").ConfigureAwait(true);
        BatchDelete.Click += async (_, _) => await RunBatchAsync("delete").ConfigureAwait(true);
    }

    /// <summary>Enters the same local-row selection mode as the reference filter menu.</summary>
    private void BeginSelection()
    {
        _selection ??= new HashSet<string>(StringComparer.Ordinal);
        Rebuild();
        BatchEnd.Focus();
    }

    /// <summary>Toggles a displayed row without navigating away from the conversation.</summary>
    /// <param name="path">Canonical transcript.</param>
    private void ToggleSelected(string path)
    {
        if (_selection is null || _batch is { IsCompleted: false })
        {
            return;
        }

        if (!_selection.Remove(path))
        {
            _selection.Add(path);
        }

        Rebuild();
    }

    /// <summary>Projects only displayed selections, preserving failed paths through readback.</summary>
    private void UpdateBatch()
    {
        _selection?.IntersectWith(_displayed.Select(row => row.Path));
        var selected = _displayed.Where(row => _selection?.Contains(row.Path) == true).ToArray();
        var busy = _batch is { IsCompleted: false };
        var live = selected.Any(row => IsLive(new SessionOpenTarget(string.Empty, row.WorkspaceRoot, row.Path)));
        var all = _displayed.Count > 0 && _displayed.All(row => _selection?.Contains(row.Path) == true);
        BatchArea.IsVisible = _selection is not null;
        BatchCount.Text = MessageCatalog.Get("session.n_sessions_selected", Language, ("n", Format.Count(selected.Length, Language)));
        BatchEnd.Content = MessageCatalog.Get("session.done_selecting", Language);
        BatchAll.Content = MessageCatalog.Get(all ? "session.deselect_all" : "session.select_visible", Language);
        BatchArchive.Content = MessageCatalog.Get(selected.Length > 0 && selected.All(row => row.Archived) ? "common.unarchive" : "common.archived_2", Language);
        BatchDelete.Content = MessageCatalog.Get("common.delete", Language);
        BatchEnd.IsEnabled = BatchAll.IsEnabled = !busy;
        BatchArchive.IsEnabled = BatchDelete.IsEnabled = !busy && !live && selected.Length > 0;
        BatchNotice.IsVisible = live || busy;
        BatchNotice.Text = MessageCatalog.Get(live ? "session.selected_sessions_are_running_stop" : "session.processing_selected_sessions", Language);
        TreeScroll.IsEnabled = Toolbar.IsEnabled = !busy;
    }

    /// <summary>Retains failed selections after one acknowledged sequence of canonical writes.</summary>
    /// <param name="action">Archive, restore or delete.</param><returns>The completed batch.</returns>
    private async Task RunBatchAsync(string action)
    {
        var rows = _displayed.Where(row => _selection?.Contains(row.Path) == true).ToArray();
        if (BatchRequested is null || rows.Length == 0 || _batch is { IsCompleted: false } || rows.Any(row => IsLive(new SessionOpenTarget(string.Empty, row.WorkspaceRoot, row.Path))))
        {
            return;
        }

        var request = BatchRequested.Invoke(action, rows);
        _batch = request;
        UpdateBatch();
        try
        {
            _selection = new HashSet<string>(await request.ConfigureAwait(true), StringComparer.Ordinal);
        }
        finally
        {
            _batch = null;
            Rebuild();
        }
    }
}
