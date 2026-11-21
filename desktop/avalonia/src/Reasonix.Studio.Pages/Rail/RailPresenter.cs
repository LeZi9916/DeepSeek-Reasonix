using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages.Rail;

/// <summary>
/// Turns the kernel's tree into the rows a sidebar draws.
/// </summary>
/// <remarks>
/// The order is the one a reader expects from a list they scan by recency:
/// pinned conversations first, then everything else newest-first, with archived
/// rows last so they stay reachable without crowding the work in progress.
///
/// Copies and versions never become rows of their own. A save that kept
/// conflicting writes leaves one file per turn, and every edit leaves the
/// conversation as it stood; unfolded, one conversation would fill the sidebar
/// with rows the person never made. The row says how many stand behind it
/// instead, and the menu is where they are reached.
/// </remarks>
public static class RailPresenter
{
    /// <summary>
    /// Builds the rows for one tree.
    /// </summary>
    /// <param name="workspaces">The projects the kernel listed.</param>
    /// <param name="current">The transcript open in this window, or empty.</param>
    /// <param name="language">The language to say the labels in.</param>
    /// <returns>The rows, in drawing order.</returns>
    public static IReadOnlyList<RailRow> Rows(
        IReadOnlyList<TreeWorkspace>? workspaces,
        string? current,
        InterfaceLanguage language)
    {
        var rows = new List<RailRow>();
        if (workspaces is null)
        {
            return rows;
        }

        foreach (var workspace in workspaces)
        {
            var sessions = Sorted(workspace.Sessions);
            rows.Add(new RailRow(
                RailRowKind.Project,
                workspace.Root,
                workspace.Name,
                ProjectSubtitle(workspace, language),
                Indent: 0,
                Unread: ContainsUnread(sessions),
                Pinned: false,
                Archived: false,
                Running: workspace.Open,
                Missing: workspace.Missing,
                Collapsed: 0));

            foreach (var session in sessions)
            {
                rows.Add(new RailRow(
                    RailRowKind.Session,
                    session.Path,
                    TitleOf(session),
                    SessionSubtitle(session, language),
                    Indent: 1,
                    Unread: session.Unread,
                    Pinned: session.Pinned,
                    Archived: session.Archived,
                    Running: !string.IsNullOrEmpty(session.RuntimeId) || Same(session.Path, current),
                    Missing: false,
                    Collapsed: session.Copies.Count + session.Versions.Count));
            }
        }

        return rows;
    }

    /// <summary>
    /// Orders one project's conversations for the sidebar.
    /// </summary>
    /// <param name="sessions">The conversations the project listed.</param>
    /// <returns>Pinned rows first, preserving the order supplied by the tree within each group.</returns>
    public static IReadOnlyList<TreeSession> Sorted(IReadOnlyList<TreeSession>? sessions)
    {
        if (sessions is null || sessions.Count == 0)
        {
            return Array.Empty<TreeSession>();
        }

        return sessions.OrderByDescending(session => session.Pinned).ToArray();
    }

    /// <summary>
    /// Reads the line a conversation's row shows under its title.
    /// </summary>
    /// <param name="session">The conversation.</param>
    /// <param name="language">The language to say it in.</param>
    /// <returns>The subtitle, or empty when the kernel has not counted it yet.</returns>
    public static string SessionSubtitle(TreeSession session, InterfaceLanguage language)
    {
        if (session.Turns <= 0)
        {
            return string.Empty;
        }

        return MessageCatalog.Get(
            MessageKey.Usage.NTurns,
            language,
            ("n", Format.Count(session.Turns, language)));
    }

    /// <summary>
    /// Reports whether two paths name one transcript.
    /// </summary>
    /// <param name="left">One path.</param>
    /// <param name="right">The other, or null.</param>
    /// <returns>True when both are non-empty and equal, case-blind.</returns>
    public static bool Same(string? left, string? right)
    {
        return !string.IsNullOrEmpty(left)
            && !string.IsNullOrEmpty(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a project's subtitle: its session count, or that the folder is gone.
    /// </summary>
    /// <param name="workspace">The project.</param>
    /// <param name="language">The language to say it in.</param>
    /// <returns>The subtitle, or empty when the project has nothing to add.</returns>
    private static string ProjectSubtitle(TreeWorkspace workspace, InterfaceLanguage language)
    {
        if (workspace.Missing)
        {
            return MessageCatalog.Get(MessageKey.Session.FolderIsGone, language);
        }

        return workspace.Total <= 0 ? string.Empty : Format.Count(workspace.Total, language);
    }

    /// <summary>
    /// Reads the title a conversation's row shows.
    /// </summary>
    /// <param name="session">The conversation.</param>
    /// <returns>The title it gave itself, then its name.</returns>
    private static string TitleOf(TreeSession session)
    {
        return string.IsNullOrWhiteSpace(session.Title) ? session.Name : session.Title;
    }

    /// <summary>
    /// Reports whether any of a project's conversations has an unread turn.
    /// </summary>
    /// <param name="sessions">The project's conversations.</param>
    /// <returns>True when at least one row is unread.</returns>
    private static bool ContainsUnread(IReadOnlyList<TreeSession> sessions)
    {
        foreach (var session in sessions)
        {
            if (session.Unread)
            {
                return true;
            }
        }

        return false;
    }
}
