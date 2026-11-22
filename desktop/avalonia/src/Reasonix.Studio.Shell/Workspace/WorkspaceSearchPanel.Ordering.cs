using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell.Workspace;

public sealed partial class WorkspaceSearchPanel
{
    /// <summary>Projects the reference's five-per-kind, three-recent and session-first best-match rules.</summary>
    /// <param name="query">Normalized search words.</param><returns>Headings and selectable canonical hits.</returns>
    private WorkspaceSearchHit[] OrderedHits(string query)
    {
        var matching = _entries.Where(entry => (_scope.Length == 0 || entry.Kind == _scope)
            && (query.Length == 0 || (entry.Label + " " + entry.Detail + " " + entry.Keywords).Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        var actions = matching.Where(entry => entry.Kind == "actions").Take(5).ToList();
        var sessions = matching.Where(entry => entry.Kind == "sessions").Take(5).ToList();
        var settings = matching.Where(entry => entry.Kind == "settings" && (query.Length > 0 || _scope == "settings")).Take(5).ToList();
        var groups = new List<(string Label, List<WorkspaceSearchHit> Hits)>();
        if (query.Length == 0 && _scope.Length == 0)
        {
            var pool = actions.Concat(sessions).ToArray();
            var recent = Recent.Take(3).Select(key => pool.FirstOrDefault(hit => hit.CanonicalKey == key)).OfType<WorkspaceSearchHit>().ToList();
            var used = recent.Select(hit => hit.CanonicalKey).ToHashSet(StringComparer.Ordinal);
            groups.Add(("app.recently_used", recent));
            groups.Add(("app.quick_actions", actions.Where(hit => !used.Contains(hit.CanonicalKey)).ToList()));
            groups.Add(("common.session", sessions.Where(hit => !used.Contains(hit.CanonicalKey)).ToList()));
        }
        else
        {
            groups.Add(("common.session", sessions));
            groups.Add(("common.settings", settings));
            groups.Add(("app.quick_actions", actions));
            if (query.Length > 0 && _scope.Length == 0)
            {
                var best = groups.FirstOrDefault(group => group.Hits.Count > 0).Hits;
                if (best is { Count: > 0 })
                {
                    var hit = best[0];
                    best.RemoveAt(0);
                    groups.Insert(0, ("app.best_match", new List<WorkspaceSearchHit> { hit }));
                }
            }
        }

        return groups.Where(group => group.Hits.Count > 0).SelectMany(group => new[] { new WorkspaceSearchHit("heading", MessageCatalog.Get(group.Label, _language), query.Length > 0 && group.Label != "app.best_match" ? group.Hits.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty, () => { }) }.Concat(group.Hits)).ToArray();
    }
}
