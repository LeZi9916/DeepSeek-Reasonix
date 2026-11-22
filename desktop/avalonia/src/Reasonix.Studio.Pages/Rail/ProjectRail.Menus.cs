using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages.Rail;

public sealed partial class ProjectRail
{
    /// <summary>Raised for navigation with complete machine and workspace identity.</summary>
    public event Action<SessionOpenTarget>? NavigateRequested;

    /// <summary>Raised for additional canonical project and session operations.</summary>
    public event Action<WorkspaceAction>? ActionRequested;

    /// <summary>Gets the acknowledged local projects used by the canonical move destination picker.</summary>
    public IReadOnlyList<TreeWorkspace> LocalProjects => _tree;

    /// <summary>Gets all loaded conversation identities for global search.</summary>
    public IReadOnlyList<(SessionOpenTarget Target, TreeSession Session, string Project)> SearchEntries =>
        _tree.SelectMany(w => w.Sessions.Select(s => (new SessionOpenTarget(string.Empty, s.WorkspaceRoot.Length > 0 ? s.WorkspaceRoot : w.Root, s.Path), s, w.Name)))
        .Concat(_remotes.SelectMany(h => h.Projects.SelectMany(w => w.Sessions.Select(s => (new SessionOpenTarget(h.Name, s.WorkspaceRoot.Length > 0 ? s.WorkspaceRoot : w.Root, s.Path), s, w.Name))))).ToArray();

    /// <summary>Dispatches the same row operation from an alternate native surface.</summary>
    /// <param name="action">The canonical row operation.</param>
    public void RequestAction(WorkspaceAction action) => ActionRequested?.Invoke(action);

    /// <summary>Dispatches navigation through the shared coordinator when one is attached.</summary>
    /// <param name="target">Complete navigation identity.</param>
    public void Navigate(SessionOpenTarget target)
    {
        if (NavigateRequested is not null)
        {
            NavigateRequested.Invoke(target);
        }
        else if (target.Host.Length == 0)
        {
            Opened?.Invoke(target.SessionPath);
        }
        else
        {
            RemoteOpened?.Invoke(target.Host, target.WorkspaceRoot, target.SessionPath);
        }
    }

    /// <summary>Builds the project actions using its complete navigation identity.</summary>
    /// <param name="target">Machine and project.</param><returns>The anchored project menu.</returns>
    private MenuFlyout BuildProjectMenu(SessionOpenTarget target)
    {
        if (target.Host.Length > 0)
        {
            return BuildRemoteProjectMenu(target);
        }

        var workspace = _tree.FirstOrDefault(workspace => workspace.Root == target.WorkspaceRoot);
        var remembered = _tree.Where(workspace => workspace.Remembered).ToArray();
        var position = Array.FindIndex(remembered, workspace => workspace.Root == target.WorkspaceRoot);
        var menu = new MenuFlyout();
        menu.Items.Add(MenuHeader(target.WorkspaceRoot.TrimEnd('/', '\\').Split('/', '\\').Last(), string.Join(" · ", new[] { target.Host, target.WorkspaceRoot }.Where(part => part.Length > 0))));
        foreach (var (action, label) in new[]
        {
            ("workspace.copy-path", "common.copy_path"), ("workspace.move-up", "common.move_up"),
            ("workspace.reveal", "session.show_in_file_manager"),
            ("workspace.move-down", "common.move_down"), ("workspace.remove", "common.remove_from_the_list"),
        })
        {
            if (workspace?.Remembered != true && action is "workspace.move-up" or "workspace.move-down")
            {
                continue;
            }

            var item = MenuActionItem(action, MessageCatalog.Get(label, Language));
            item.IsEnabled = action switch
            {
                "workspace.reveal" => workspace?.Missing != true,
                "workspace.move-up" => position > 0,
                "workspace.move-down" => position >= 0 && position < remembered.Length - 1,
                _ => true,
            };
            AutomationProperties.SetAutomationId(item, "action:" + action);
            item.Click += (_, _) =>
            {
                if (action == "session.new")
                {
                    Navigate(target);
                }
                else if (action == "workspace.refresh")
                {
                    RemoteRefreshRequested?.Invoke();
                }
                else
                {
                    RequestAction(new WorkspaceAction(action, target));
                }
            };
            menu.Items.Add(item);
        }

        var select = MenuActionItem("session.selection", MessageCatalog.Get("session.select_local_sessions", Language));
        select.IsEnabled = _tree.Any(workspace => workspace.Sessions.Any(session => InScope(string.Empty, session))) && _selection is null;
        AutomationProperties.SetAutomationId(select, "action:session.selection");
        select.Click += (_, _) => BeginSelection();
        menu.Items.Insert(menu.Items.Count - 1, select);
        DiscoverProjectEditors(menu, target, workspace?.Missing == true);

        return menu;
    }

    /// <summary>Builds the canonical local or remote conversation actions.</summary>
    /// <param name="target">Complete row identity.</param><param name="session">Canonical row metadata.</param>
    /// <returns>The anchored menu.</returns>
    private MenuFlyout BuildSessionMenu(SessionOpenTarget target, TreeSession session)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(MenuHeader(session.Title.Length > 0 ? session.Title : session.Name, target.Host.Length > 0 ? target.Host : MessageCatalog.Get("session.local_workspace", Language)));
        void Add(string action, string label, Action run, bool enabled = true)
        {
            var header = MessageCatalog.Get(label, Language) + (action == "session.export-md" ? " · Markdown" : action == "session.export" ? " · JSON" : string.Empty);
            var item = MenuActionItem(action, header);
            item.IsEnabled = enabled;
            AutomationProperties.SetAutomationId(item, "action:" + action);
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }

        void Write(string action, bool value)
        {
            if (target.Host.Length > 0)
            {
                RemoteWritten?.Invoke(target.Host, target.SessionPath, action, value);
            }
            else if (action == "pin")
            {
                Pinned?.Invoke(target.SessionPath, value);
            }
            else if (action == "archive")
            {
                Archived?.Invoke(target.SessionPath, value);
            }
            else
            {
                MarkedViewed?.Invoke(target.SessionPath);
            }
        }

        var live = IsLive(target);
        Add("session.pin", session.Pinned ? "common.unpin" : "common.pin_session", () => Write("pin", !session.Pinned));
        if (session.Unread)
        {
            Add("session.mark-read", "common.mark_as_read", () => Write("viewed", true));
        }

        if (target.Host.Length == 0)
        {
            foreach (var (action, label) in new[]
            {
                ("session.move-up", "common.move_up"), ("session.move-down", "common.move_down"),
                ("session.rename-start", "common.renamed"), ("session.auto-name", "session.auto_name"),
                ("session.copy-info", "session.copy_session_information"), ("session.export", "session.export_session"),
                ("session.export-md", "session.export_session"),
                ("session.move-to-start", "session.move_to_a_project"),
            })
            {
                var enabled = action switch
                {
                    "session.move-up" => CanMoveSession(target, session, true),
                    "session.move-down" => CanMoveSession(target, session, false),
                    "session.move-to-start" => session.RuntimeId.Length == 0,
                    _ => true,
                };
                Add(action, label, () => ActionRequested?.Invoke(new WorkspaceAction(action, target, session)), enabled);
            }
        }

        Add("session.archive", session.Archived ? "common.unarchive" : "common.archive_session", () => Write("archive", !session.Archived), !live);
        if (session.RuntimeId.Length > 0)
        {
            if (target.Host.Length == 0)
            {
                Add("session.branch", "session.branch_from_the_latest_reply", () => ActionRequested?.Invoke(new WorkspaceAction("session.branch", target, session)));
            }

            if (live)
            {
                Add("session.pause", "session.pause_run", () => ActionRequested?.Invoke(new WorkspaceAction("session.pause", target, session)));
            }

            Add("session.close", "session.close_conversation", () => ActionRequested?.Invoke(new WorkspaceAction("session.close", target, session)), !live);
        }

        if (target.Host.Length == 0 && session.Versions.Count + session.Copies.Count > 0)
        {
            Add("session.kept", "session.earlier_versions_n", () =>
            {
                if (!_spread.Remove(session.Path))
                {
                    _spread.Add(session.Path);
                }

                Rebuild();
            });
            menu.Items.OfType<MenuItem>().Last().Header = MessageCatalog.Get("session.earlier_versions_n", Language, ("n", Format.Count(session.Versions.Count + session.Copies.Count, Language)));
        }

        Add("session.delete", "common.delete_session", () => ActionRequested?.Invoke(new WorkspaceAction("session.delete", target, session)));
        return menu;
    }

    /// <summary>Draws menu actions with the reference row rhythm and native vector marks.</summary>
    /// <param name="action">Stable action identity.</param><param name="header">Localized action.</param>
    /// <returns>The styled native menu item.</returns>
    private MenuItem MenuActionItem(string action, string header)
    {
        var data = action switch
        {
            "session.rename-start" => "M4 16 16 4l4 4L8 20H4z M13 7l4 4",
            "session.pin" => "M8 3h8v6l3 3v3H5v-3l3-3z M12 15v7",
            "session.branch" => "M6 3v12a4 4 0 0 0 4 4h8 M18 3v5a4 4 0 0 1-4 4H6 M3 3h6 M15 3h6 M18 16l3 3-3 3",
            "session.kept" => "M3 12a9 9 0 1 0 3-6 M3 3v6h6 M12 7v5l3 2",
            "session.export" or "session.export-md" => "M12 3v12 m-5-5 5 5 5-5 M4 16v5h16v-5",
            "session.delete" or "workspace.remove" => "M3 6h18 M9 6V3h6v3 M5 6l1 15h12l1-15 M10 10v7 M14 10v7",
            "session.pause" => "M8 4v16 M16 4v16",
            "session.close" => "m5 5 14 14 M19 5 5 19",
            "session.copy-info" or "workspace.copy-path" => "M8 8h13v13H8z M16 8V3H3v13h5",
            "session.new" => "M12 4v16 M4 12h16",
            "workspace.reveal" or "session.move-to-start" => "M3 6h7l2 2h9v12H3z",
            "session.archive" => "M3 3h18v5H3z M5 8v13h14V8 M9 12h6",
            "session.move-up" or "workspace.move-up" => "M12 21V3 m-7 7 7-7 7 7",
            "session.move-down" or "workspace.move-down" => "M12 3v18 m-7-7 7 7 7-7",
            "session.mark-read" => "m4 12 5 5 11-11",
            _ => "M20 7v5h-5 M20 12a8 8 0 1 0-2.5 6",
        };
        var path = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(data), StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
        path.Bind(Shape.StrokeProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("MutedBrush"));
        return new MenuItem
        {
            Header = header,
            Theme = this.FindResource("WorkspaceMenuItemTheme") as ControlTheme,
            Icon = new Viewbox { Width = 14, Height = 14, Child = new Canvas { Width = 24, Height = 24, Children = { path } } },
        };
    }

    /// <summary>Names the canonical object before its available operations.</summary>
    /// <param name="title">Canonical display title.</param><param name="detail">Workspace or machine context.</param>
    /// <returns>The noninteractive menu heading.</returns>
    private MenuItem MenuHeader(string title, string detail)
    {
        var heading = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 };
        heading.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("TextBrush"));
        var note = new TextBlock { Text = detail, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 };
        note.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FaintBrush"));
        return new MenuItem
        {
            Theme = this.FindResource("WorkspaceMenuHeaderTheme") as ControlTheme,
            Header = new StackPanel { Spacing = 3, Children = { heading, note } },
        };
    }
}
