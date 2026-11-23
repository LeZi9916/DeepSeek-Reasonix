using Reasonix.Studio.Shell.Host;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>Provides the same set of actions to the rail, sidebar menu, and settings.</summary>
internal static class WorkspaceDestinations
{
    /// <summary>Gets the reference destinations in their default order.</summary>
    internal static IReadOnlyList<Destination> All
    {
        get;
    } =
    [
        new("usage", "app.usage", "Glyph.usage-rail"),
        new("files", "common.files", "Glyph.file-text"),
        new("tools", "app.tools_and_permissions", "Glyph.tools"),
        new("ext", "ext.extension", "Glyph.ext"),
        new("memory", "data.memory", "Glyph.model"),
        new("remote", "settings.remote", "Glyph.remote-rail"),
    ];

    /// <summary>Applies saved ordering and optionally hides marked entries.</summary>
    /// <param name="preferences">The current device settings.</param>
    /// <param name="includeHidden">Whether to show entries being edited.</param>
    /// <returns>Known destinations, without duplicates.</returns>
    internal static IReadOnlyList<Destination> Order(WorkspacePreferences preferences, bool includeHidden = false)
    {
        var rank = (preferences.NavigationOrder ?? []).Distinct(StringComparer.Ordinal).ToArray();
        return All.Where(item => includeHidden || !(preferences.HiddenNavigation ?? []).Contains(item.Id, StringComparer.Ordinal))
            .OrderBy(item => Array.IndexOf(rank, item.Id) is >= 0 and var index ? index : int.MaxValue)
            .ToArray();
    }

    /// <summary>One destination shown in the workspace.</summary>
    /// <param name="Id">The persistent identity.</param>
    /// <param name="Label">The localized label key.</param>
    /// <param name="Glyph">The shared icon resource.</param>
    internal sealed record Destination(string Id, string Label, string Glyph);
}
