using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.Rail;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Session;

/// <summary>Owns the window's runtime engines independently of which one is focused.</summary>
/// <param name="owner">The owning local host connection.</param>
internal sealed class WindowSessionCoordinator(SessionEngine owner) : IAsyncDisposable
{
    /// <summary>The live panes retained independently of focus.</summary>
    private readonly List<SessionPaneState> _panes = [];

    /// <summary>Only one open operation may own a canonical target at a time.</summary>
    private readonly Dictionary<SessionOpenTarget, Task<SessionPaneState>> _opening = [];

    /// <summary>Prevents two concurrent navigations from rebinding the same idle pane.</summary>
    private readonly HashSet<SessionPaneState> _rebinding = [];

    /// <summary>Idle panes eligible for retirement only after a replacement is actually mounted.</summary>
    private readonly Dictionary<SessionPaneState, SessionPaneState> _retireAfterRead = [];

    /// <summary>Raised when an explicit tab limit folds a settled pane.</summary>
    internal event Action<string>? Folded;

    /// <summary>Raised after the owning runtime acknowledges an explicit or idle close.</summary>
    internal event Action<SessionPaneState>? Closed;

    /// <summary>Gets or sets the pane protected from automatic idle folding.</summary>
    internal SessionPaneState? Focused
    {
        get; set;
    }

    /// <summary>Gets or sets the explicitly chosen idle tab limit, or zero.</summary>
    internal int Limit
    {
        get; set;
    }

    /// <summary>Gets the window's retained panes in their UI order.</summary>
    internal IReadOnlyList<SessionPaneState> Panes => _panes;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var pane in _panes.Where(p => !ReferenceEquals(p.Engine, owner)))
        {
            await pane.Engine.DisposeAsync().ConfigureAwait(false);
        }

        _panes.Clear();
        await owner.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Captures the acknowledged status without replacing the retained engine.</summary>
    /// <param name="engine">The pane's engine.</param><param name="status">Canonical session status.</param>
    /// <returns>The retained pane.</returns>
    internal SessionPaneState Capture(SessionEngine engine, SessionStatus status)
    {
        var pane = _panes.FirstOrDefault(p => ReferenceEquals(p.Engine, engine));
        if (pane is null)
        {
            pane = new SessionPaneState(engine, status);
            _panes.Add(pane);
        }
        else
        {
            pane.Status = status;
        }

        return pane;
    }

    /// <summary>Opens or focuses a target while retaining every other pane.</summary>
    /// <param name="target">Canonical navigation target.</param><param name="register">Whether to register a folder.</param>
    /// <param name="preset">Explicit composition for a new task.</param><param name="token">Cancels opening.</param>
    /// <returns>The existing or acknowledged new pane.</returns>
    internal Task<SessionPaneState> OpenAsync(SessionOpenTarget target, bool register, string? preset, CancellationToken token)
    {
        if (target.SessionPath.Length > 0)
        {
            var existing = _panes.FirstOrDefault(p => p.Host == target.Host && p.Status.SessionPath == target.SessionPath);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            if (_opening.TryGetValue(target, out var pending))
            {
                return pending;
            }
        }

        var read = Open();
        if (target.SessionPath.Length > 0 && !read.IsCompleted)
        {
            _opening[target] = read;
        }

        return read;

        async Task<SessionPaneState> Open()
        {
            try
            {
                var blank = _panes.FirstOrDefault(p => p.Host.Length == 0 && p.Status.SessionPath.Length == 0 && !p.Engine.State.Running && !p.Status.Running && !_rebinding.Contains(p));
                if (target.Host.Length == 0 && blank is not null && blank.Status.WorkspaceRoot == target.WorkspaceRoot)
                {
                    if (target.SessionPath.Length == 0)
                    {
                        if (!string.IsNullOrEmpty(preset))
                        {
                            await blank.Engine.Pages.SelectCompositionAsync(preset, token).ConfigureAwait(true);
                        }

                        return blank;
                    }

                    return await Rebind(blank).ConfigureAwait(true);
                }

                var idle = _panes.Where(p => p != Focused && !p.Engine.State.Running && !p.Status.Running && !_rebinding.Contains(p)).OrderBy(p => p.WatchedAt).ToArray();
                var atCapacity = Limit > 0 && _panes.Count >= Limit;
                var reusable = atCapacity && target.Host.Length == 0 && target.SessionPath.Length > 0 ? idle.FirstOrDefault(p => p.Host.Length == 0 && p.Status.WorkspaceRoot == target.WorkspaceRoot) : null;
                if (reusable is not null)
                {
                    var title = reusable.Status.Label;
                    var result = await Rebind(reusable).ConfigureAwait(true);
                    Folded?.Invoke(title);
                    return result;
                }

                var engine = target.Host.Length == 0
                    ? await owner.OpenProjectAsync(target.WorkspaceRoot, token, register, target.SessionPath, preset).ConfigureAwait(true)
                    : await owner.OpenRemoteProjectAsync(target.Host, target.WorkspaceRoot, token, target.SessionPath, preset).ConfigureAwait(true);
                var known = _panes.FirstOrDefault(p => p.Engine.Runtime?.Id is { Length: > 0 } id && id == engine.Runtime?.Id);
                if (known is not null)
                {
                    await engine.DisposeAsync().ConfigureAwait(true);
                    return known;
                }

                var pane = Capture(engine, new SessionStatus { WorkspaceRoot = engine.Runtime?.Root ?? target.WorkspaceRoot, SessionPath = engine.Runtime?.SessionPath ?? target.SessionPath, Label = target.SessionPath });
                var retire = blank ?? (atCapacity ? idle.FirstOrDefault() : null);
                if (retire is not null && retire != pane)
                {
                    _retireAfterRead[pane] = retire;
                }

                return pane;
            }
            finally
            {
                _opening.Remove(target);
            }

            async Task<SessionPaneState> Rebind(SessionPaneState pane)
            {
                _rebinding.Add(pane);
                try
                {
                    await pane.Engine.ResumeAsync(target.SessionPath, token).ConfigureAwait(true);
                    pane.Status = pane.Status with
                    {
                        SessionPath = target.SessionPath,
                        Label = target.SessionPath,
                        Running = false,
                    };
                    pane.ScrollOffset = 0;
                    pane.WorkbenchOpen = false;
                    pane.FilePath = null;
                    pane.Unread = false;
                    return pane;
                }
                finally
                {
                    _rebinding.Remove(pane);
                }
            }
        }
    }

    /// <summary>Retires an idle predecessor after its replacement's history and draft are mounted.</summary>
    /// <param name="pane">The acknowledged focused replacement.</param><param name="token">Cancels retirement.</param>
    /// <returns>The optional retirement acknowledgment.</returns>
    internal async Task AcknowledgeReadAsync(SessionPaneState pane, CancellationToken token)
    {
        if (!_retireAfterRead.Remove(pane, out var previous) || Focused != pane || previous == Focused || !_panes.Contains(previous) || previous.Engine.State.Running || previous.Status.Running)
        {
            return;
        }

        await CloseAsync(previous, token).ConfigureAwait(true);
        if (previous.Status.SessionPath.Length > 0)
        {
            Folded?.Invoke(previous.Status.Label);
        }
    }

    /// <summary>Closes the explicitly selected runtime through its owning hub.</summary>
    /// <param name="pane">The selected pane.</param><param name="token">Cancels closing.</param>
    /// <returns>The acknowledged close.</returns>
    internal async Task CloseAsync(SessionPaneState pane, CancellationToken token)
    {
        if (pane.Engine.Runtime is { } runtime)
        {
            await owner.Pages.CloseRuntimeAsync(runtime.Id, token).ConfigureAwait(true);
        }

        _panes.Remove(pane);
        if (ReferenceEquals(pane.Engine, owner))
        {
            await owner.StopWatchingAsync().ConfigureAwait(true);
        }
        else
        {
            await pane.Engine.DisposeAsync().ConfigureAwait(true);
        }

        Closed?.Invoke(pane);
    }
}
