using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages.Rail;

public sealed partial class ProjectRail
{
    /// <summary>Canonical transcript owners whose recovery and earlier-version rows are expanded.</summary>
    private readonly HashSet<string> _spread = new(StringComparer.Ordinal);

    /// <summary>Draws preserved transcripts only after the owner explicitly expands them.</summary>
    /// <param name="rows">The target row collection.</param>
    /// <param name="root">Owning project root.</param><param name="owner">Canonical transcript with preserved versions.</param>
    private void AddKeptRows(ICollection<Control> rows, string root, TreeSession owner)
    {
        if (!_spread.Contains(owner.Path))
        {
            return;
        }

        foreach (var (session, version) in owner.Versions.Select(session => (session, true)).Concat(owner.Copies.Select(session => (session, false))))
        {
            var target = new SessionOpenTarget(string.Empty, session.WorkspaceRoot.Length > 0 ? session.WorkspaceRoot : root, session.Path);
            var label = version ? MessageCatalog.Get("session.earlier_version_n_turns", Language, ("n", Format.Count(session.Turns, Language))) : MessageCatalog.Get("session.recovery", Language);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24"), Height = 30, Margin = new Thickness(14, 1, 0, 0) };
            var open = new Button { Content = label, Classes = { "rail-button", "session-copy" }, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(22, 0, 8, 0) };
            AutomationProperties.SetAutomationId(open, "action:session.open");
            AutomationProperties.SetName(open, label);
            ToolTip.SetTip(open, session.Title.Length > 0 ? session.Title : session.Name);
            open.Click += (_, _) => Navigate(target);
            open.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Delete or Key.Back)
                {
                    RequestAction(new WorkspaceAction("session.delete", target, session));
                    e.Handled = true;
                }
            };
            var delete = new Button { Content = "×", Classes = { "rail-button", "rail-icon" }, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(delete, "action:session.delete");
            AutomationProperties.SetName(delete, MessageCatalog.Get("session.delete_this_session", Language));
            delete.Click += (_, _) => RequestAction(new WorkspaceAction("session.delete", target, session));
            Grid.SetColumn(delete, 1);
            row.Children.Add(open);
            row.Children.Add(delete);
            rows.Add(row);
        }
    }
}
