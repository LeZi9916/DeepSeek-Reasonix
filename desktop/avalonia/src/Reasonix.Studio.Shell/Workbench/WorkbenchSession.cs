using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Shell.Browser;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Owns the editor buffers and suspended workbench surfaces of retained runtimes.</summary>
internal sealed class WorkbenchSession
{
    /// <summary>Workspace visuals saved while another runtime is focused.</summary>
    private readonly Dictionary<SessionEngine, WorkbenchPresentation> _presentations = [];

    /// <summary>Unique editor namespaces and their workspace scopes.</summary>
    private readonly Dictionary<SessionEngine, (string Id, HashSet<string> Scopes)> _namespaces = [];

    /// <summary>Gets the files retained by workspace scope and canonical path.</summary>
    internal Dictionary<(string Root, string Path), FileBuffer> Buffers
    {
        get;
    } = [];

    /// <summary>Gets or sets the runtime whose workbench is mounted.</summary>
    internal SessionEngine? BoundEngine
    {
        get; set;
    }

    /// <summary>Gets or sets the active editor namespace.</summary>
    internal string BufferScope
    {
        get; set;
    } = string.Empty;

    /// <summary>Gets the editor namespace for a runtime and workspace.</summary>
    /// <param name="engine">Retained runtime.</param><param name="root">Canonical workspace.</param>
    /// <returns>A namespace unique to this workspace in the runtime.</returns>
    internal string ScopeFor(SessionEngine? engine, string root)
    {
        if (engine is null)
        {
            return root;
        }

        if (!_namespaces.TryGetValue(engine, out var space))
        {
            space = (Guid.NewGuid().ToString("N"), []);
            _namespaces.Add(engine, space);
        }

        var scope = JsonSerializer.Serialize(new[] { engine.RemoteHost, root, space.Id });
        space.Scopes.Add(scope);
        return scope;
    }

    /// <summary>Saves the currently mounted presentation.</summary>
    /// <param name="engine">Owning runtime.</param><param name="presentation">The view's state.</param>
    internal void Capture(SessionEngine engine, WorkbenchPresentation presentation) => _presentations[engine] = presentation;

    /// <summary>Looks up the state of a retained workbench.</summary>
    /// <param name="engine">Owning runtime.</param><param name="presentation">Saved presentation.</param>
    /// <returns>Whether a presentation is held.</returns>
    internal bool TryRead(SessionEngine engine, [NotNullWhen(true)] out WorkbenchPresentation? presentation) =>
        _presentations.TryGetValue(engine, out presentation);

    /// <summary>Gets the retained runtime identities for window teardown.</summary>
    /// <returns>Runtime identities with presentations or editor buffers.</returns>
    internal SessionEngine[] Runtimes() => _presentations.Keys.Concat(_namespaces.Keys).Distinct().ToArray();

    /// <summary>Gets the browser pages that can sleep while a runtime is parked.</summary>
    /// <returns>Retained pages.</returns>
    internal IEnumerable<ManualWebPreview> BrowserPages() => _presentations.Values.SelectMany(state => state.Browsers);

    /// <summary>Releases retained state after the owning runtime closes.</summary>
    /// <param name="engine">Closed runtime.</param><returns>Browser guests to release from the view.</returns>
    internal IReadOnlyList<ManualWebPreview> Release(SessionEngine engine)
    {
        if (_namespaces.Remove(engine, out var space))
        {
            foreach (var key in Buffers.Keys.Where(key => space.Scopes.Contains(key.Root)).ToArray())
            {
                Buffers.Remove(key);
            }
        }

        if (!_presentations.Remove(engine, out var presentation))
        {
            return [];
        }

        presentation.GitLifetime?.Cancel();
        presentation.GitLifetime?.Dispose();
        return presentation.Browsers;
    }
}
