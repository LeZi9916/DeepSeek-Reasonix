using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell.Session;

/// <summary>Native tabs that focus retained panes and expose explicit close operations.</summary>
internal sealed partial class SessionTabs : UserControl
{
    /// <summary>Retains the current projection while its labels and states remain unchanged.</summary>
    private string _snapshot = string.Empty;

    /// <summary>The visual group that owns the active tab.</summary>
    private StackPanel? _activeGroup;

    /// <summary>Initializes a new instance of the <see cref="SessionTabs"/> class.</summary>
    public SessionTabs()
    {
        InitializeComponent();
        Tabs.LayoutUpdated += (_, _) => PositionMark();
    }

    /// <summary>Raised to focus the selected retained pane.</summary>
    internal event Action<SessionPaneState>? FocusRequested;

    /// <summary>Raised to explicitly close the selected panes.</summary>
    internal event Action<SessionPaneState[]>? CloseRequested;

    /// <summary>Raised to rename a local pane's canonical conversation.</summary>
    internal event Action<SessionPaneState>? RenameRequested;

    /// <summary>Raised to open a peer thread written by this session.</summary>
    internal event Action<string>? PeerOpenRequested;

    /// <summary>Projects canonical pane identities and activity without recreating their engines.</summary>
    /// <param name="panes">Retained panes.</param><param name="active">Focused pane.</param>
    /// <param name="language">Interface language.</param>
    /// <param name="peerMarks">Peer-thread state by canonical session path.</param>
    internal void Show(IReadOnlyList<SessionPaneState> panes, SessionPaneState? active, InterfaceLanguage language, IReadOnlyDictionary<string, PeerTabMark>? peerMarks = null)
    {
        peerMarks ??= new Dictionary<string, PeerTabMark>();
        IsVisible = panes.Count > 1;
        var snapshot = string.Join("\n", panes.Select(p => $"{p.Engine.GetHashCode()}:{p.Status.Label}:{p.Engine.State.Running}:{p.Unread}:{ReferenceEquals(p, active)}:{peerMarks.GetValueOrDefault(p.Status.SessionPath)?.State}"));
        if (_snapshot == snapshot)
        {
            return;
        }

        _snapshot = snapshot;
        Tabs.Children.Clear();
        _activeGroup = null;
        var showRoot = panes.Select(p => p.Status.WorkspaceRoot)
            .Where(root => !string.IsNullOrEmpty(root)).Distinct(StringComparer.Ordinal).Count() > 1;
        foreach (var pane in panes)
        {
            var running = pane.Engine.State.Running;
            var pip = new Border { Width = 6, Height = 6, CornerRadius = new Avalonia.CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
            pip.Classes.Add("pane-pip");
            pip.Classes.Set("running", running);
            pip.Classes.Set("unread", !running && pane.Unread);
            pip.Bind(Border.BackgroundProperty, pip.GetResourceObservable(running ? "NetBrush" : pane.Unread ? "AccentInkBrush" : "GhostBrush"));
            var glow = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new Avalonia.CornerRadius(5),
                BorderThickness = new Avalonia.Thickness(2),
                Opacity = 0.28,
                IsVisible = !running && pane.Unread,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(-2),
            };
            glow.Bind(Border.BorderBrushProperty, glow.GetResourceObservable("AccentInkBrush"));
            var pipHolder = new Grid { Width = 6, Height = 6 };
            pipHolder.Children.Add(glow);
            pipHolder.Children.Add(pip);
            var title = new TextBlock
            {
                Text = pane.Status.Label.Length > 0 ? pane.Status.Label : MessageCatalog.Get("common.new_session", language),
                MaxWidth = 180,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 7 };
            content.Children.Add(pipHolder);
            content.Children.Add(title);
            Grid.SetColumn(title, 1);
            if (showRoot && !string.IsNullOrEmpty(pane.Status.WorkspaceRoot))
            {
                var workspace = new TextBlock
                {
                    Text = RootName(pane.Status.WorkspaceRoot),
                    MaxWidth = 70,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                workspace.Classes.Add("pane-workspace");
                content.Children.Add(workspace);
                Grid.SetColumn(workspace, 2);
            }

            if (!string.IsNullOrEmpty(pane.Host))
            {
                var host = new TextBlock
                {
                    Text = pane.Host,
                    MaxWidth = 80,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                host.Classes.Add("pane-host");
                content.Children.Add(host);
                Grid.SetColumn(host, 3);
            }

            if (peerMarks.TryGetValue(pane.Status.SessionPath, out var peer))
            {
                var mark = new Button { Classes = { "pane-peer" } };
                mark.Classes.Add(peer.State);
                mark.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
                var glyph = new Avalonia.Controls.Shapes.Path
                {
                    Data = Avalonia.Media.Geometry.Parse(PeerGlyph(peer.State)),
                    Width = 13,
                    Height = 13,
                    Stretch = Avalonia.Media.Stretch.Uniform,
                    StrokeThickness = 1.8,
                    StrokeLineCap = Avalonia.Media.PenLineCap.Round,
                    StrokeJoin = Avalonia.Media.PenLineJoin.Round,
                };
                glyph.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, glyph.GetResourceObservable(PeerBrush(peer.State)));
                mark.Content = glyph;
                var tip = MessageCatalog.Get(PeerCopyKey(peer.State), language, ("title", peer.Title), ("project", peer.Project));
                ToolTip.SetTip(mark, tip);
                AutomationProperties.SetAutomationId(mark, "action:peer.thread-open");
                AutomationProperties.SetName(mark, tip);
                mark.Click += (_, e) =>
                {
                    e.Handled = true;
                    PeerOpenRequested?.Invoke(pane.Status.SessionPath);
                };
                content.Children.Add(mark);
                Grid.SetColumn(mark, 4);
            }

            var button = new Button { Classes = { "pane-tab" }, Content = content };
            button.Classes.Set("current", ReferenceEquals(pane, active));
            AutomationProperties.SetAutomationId(button, "action:pane.activate");
            AutomationProperties.SetName(button, pane.Status.Label);
            ToolTip.SetTip(button, pane.Host.Length > 0 ? pane.Host + " · " + pane.Status.WorkspaceRoot : pane.Status.WorkspaceRoot);
            button.Click += (_, _) => FocusRequested?.Invoke(pane);
            if (pane.Host.Length == 0)
            {
                button.DoubleTapped += (_, _) => RenameRequested?.Invoke(pane);
            }

            var menu = new MenuFlyout();
            if (pane.Host.Length == 0)
            {
                Add("common.renamed", () => RenameRequested?.Invoke(pane));
            }

            Add("workbench.close_tab", () => CloseRequested?.Invoke([pane]));
            Add("app.close_the_others_n", () => CloseRequested?.Invoke(panes.Where(p => p != pane).ToArray()), panes.Count - 1);
            Add("app.close_all_n", () => CloseRequested?.Invoke(panes.ToArray()), panes.Count);
            button.ContextFlyout = menu;
            var close = new Button { Content = "×" };
            close.Classes.Add("pane-close");
            close.Classes.Set("current", ReferenceEquals(pane, active));
            AutomationProperties.SetAutomationId(close, "action:pane.close");
            AutomationProperties.SetName(close, MessageCatalog.Get("workbench.close_tab", language));
            close.Click += (_, _) => CloseRequested?.Invoke([pane]);
            var group = new StackPanel { Orientation = Orientation.Horizontal, Classes = { "pane-tab-group" }, Children = { button, close } };
            Tabs.Children.Add(group);
            if (ReferenceEquals(pane, active))
            {
                _activeGroup = group;
            }

            button.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End))
                {
                    return;
                }

                var index = panes.ToList().IndexOf(pane);
                index = e.Key == Key.Home ? 0 : e.Key == Key.End ? panes.Count - 1 : (index + (e.Key == Key.Left ? -1 : 1) + panes.Count) % panes.Count;
                FocusRequested?.Invoke(panes[index]);
                e.Handled = true;
            };

            void Add(string key, Action action, int count = -1)
            {
                var item = new MenuItem { Header = count < 0 ? MessageCatalog.Get(key, language) : MessageCatalog.Get(key, language, ("n", count.ToString(System.Globalization.CultureInfo.CurrentCulture))), IsEnabled = count != 0 };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
        }
    }

    /// <summary>Places the accent marker under the active tab group.</summary>
    /// <summary>Returns the stroke resource for a peer-thread state.</summary>
    /// <param name="state">Peer-thread state.</param>
    /// <returns>Resource key.</returns>
    private static string PeerBrush(string state) => state switch
    {
        "replied" => "AccentBrush",
        "declined" => "ErrInkBrush",
        _ => "MutedBrush",
    };

    /// <summary>Returns the localized tooltip key for a peer-thread state.</summary>
    /// <param name="state">Peer-thread state.</param>
    /// <returns>Message key.</returns>
    private static string PeerCopyKey(string state) => state switch
    {
        "waiting" => "remote.sent_to_title_project_waiting",
        "working" => "remote.title_project_accepted_it_and",
        "replied" => "remote.title_project_replied_to_you",
        _ => "remote.the_user_of_title_project",
    };

    /// <summary>Returns the icon path for a peer-thread state.</summary>
    /// <param name="state">Peer-thread state.</param>
    /// <returns>Path data.</returns>
    private static string PeerGlyph(string state) => state switch
    {
        "waiting" => "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18 M12 7v5l3 2",
        "working" => "M20 12a8 8 0 1 1-2.3-5.6 M20 4v5h-5",
        "replied" => "M4 5h16v11H9l-5 4z",
        _ => "M6 6l12 12 M18 6L6 18",
    };

    /// <summary>Returns the final path segment used as the tab's workspace label.</summary>
    /// <param name="root">Workspace root path.</param>
    /// <returns>The root's display name.</returns>
    private static string RootName(string root)
    {
        var trimmed = root.TrimEnd('/', '\\');
        var separator = trimmed.LastIndexOfAny(['/', '\\']);
        return separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
    }

    /// <summary>Places the accent marker under the active tab group.</summary>
    private void PositionMark()
    {
        if (_activeGroup is null)
        {
            TabMark.IsVisible = false;
            return;
        }

        if (_activeGroup.TranslatePoint(default, TabMarkLayer) is not { } at)
        {
            return;
        }

        TabMark.Width = _activeGroup.Bounds.Width;
        Canvas.SetLeft(TabMark, at.X);
        TabMark.IsVisible = true;
    }
}
