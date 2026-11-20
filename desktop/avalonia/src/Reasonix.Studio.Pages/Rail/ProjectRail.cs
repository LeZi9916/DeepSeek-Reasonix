using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Pages.Rail;

/// <summary>
/// The left column: every project the kernel knows, and the conversations saved
/// under each.
/// </summary>
/// <remarks>
/// A conversation row carries what a reader scans for — an unread dot, a pin, the
/// running mark — and its menu carries what changes it. Copies and versions do
/// not get rows: they are reached from the row that owns them, so one
/// conversation cannot fill the column with files the person never made.
///
/// Nothing here declares an automation identity. The captured baseline records
/// the rail's rows as a gap the shell has not drawn yet, and a control that both
/// claims a recorded gap and is counted against it fails the coverage gate; the
/// rows are scaffolding until that step of the plan replaces the record.
/// </remarks>
public sealed class ProjectRail : UserControl
{
    /// <summary>
    /// The column's rows.
    /// </summary>
    private readonly StackPanel _rows;

    /// <summary>
    /// The projects the reader folded shut, by root.
    /// </summary>
    private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The tree as it stands.
    /// </summary>
    private IReadOnlyList<TreeWorkspace> _tree = Array.Empty<TreeWorkspace>();

    /// <summary>
    /// The transcript this window has open, or null.
    /// </summary>
    private string? _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectRail"/> class.
    /// </summary>
    public ProjectRail()
    {
        _rows = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 1,
            Margin = new Thickness(8, 4, 8, 8),
        };

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _rows,
        };
    }

    /// <summary>
    /// Raised when the person asks to open a conversation.
    /// </summary>
    public event Action<string>? Opened;

    /// <summary>
    /// Raised when the person pins or unpins a conversation.
    /// </summary>
    public event Action<string, bool>? Pinned;

    /// <summary>
    /// Raised when the person archives or unarchives a conversation.
    /// </summary>
    public event Action<string, bool>? Archived;

    /// <summary>
    /// Raised when the person says a conversation has been read.
    /// </summary>
    public event Action<string>? MarkedViewed;

    /// <summary>
    /// Raised when the person asks for a conversation with nothing in it.
    /// </summary>
    public event Action? NewRequested;

    /// <summary>
    /// Gets or sets the language the rail's own words are drawn in.
    /// </summary>
    /// <remarks>
    /// Conversation titles are drawn as the kernel stored them; only the labels
    /// this control adds are translated.
    /// </remarks>
    public InterfaceLanguage Language
    {
        get; set;
    } = InterfaceLanguage.Zh;

    /// <summary>
    /// Draws one tree.
    /// </summary>
    /// <param name="tree">The projects the kernel listed, or null for none.</param>
    /// <param name="current">The transcript this window has open, or null.</param>
    public void Show(IReadOnlyList<TreeWorkspace>? tree, string? current)
    {
        _tree = tree ?? Array.Empty<TreeWorkspace>();
        _current = current;
        Rebuild();
    }

    /// <summary>
    /// Rebuilds every row from the tree as it stands.
    /// </summary>
    private void Rebuild()
    {
        _rows.Children.Clear();
        _rows.Children.Add(NewSessionButton());

        if (_tree.Count == 0)
        {
            _rows.Children.Add(EmptyState());
            return;
        }

        foreach (var workspace in _tree)
        {
            _rows.Children.Add(ProjectRow(workspace));
            if (_collapsed.Contains(workspace.Root))
            {
                continue;
            }

            foreach (var session in RailPresenter.Sorted(workspace.Sessions))
            {
                _rows.Children.Add(SessionRow(session));
            }
        }
    }

    /// <summary>
    /// Builds the column's way to start a conversation with nothing in it.
    /// </summary>
    /// <returns>The button.</returns>
    private Control NewSessionButton()
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = MessageCatalog.Get(MessageKey.Common.NewSession, Language),
                FontSize = 12,
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 6, 6, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Opacity = 0.85,
        };
        button.Click += (_, _) => NewRequested?.Invoke();
        return button;
    }

    /// <summary>
    /// Builds what the column says when the kernel lists no project at all.
    /// </summary>
    /// <returns>The empty state.</returns>
    /// <remarks>
    /// An empty column and a column that has not been read look the same, and the
    /// difference matters: one is a machine with nothing to show, the other is a
    /// read that has not answered yet. This says the first.
    /// </remarks>
    private Control EmptyState()
    {
        return new TextBlock
        {
            Text = MessageCatalog.Get(MessageKey.Session.AddAProject, Language),
            FontSize = 11,
            Opacity = 0.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(6, 8, 6, 0),
        };
    }

    /// <summary>
    /// Builds one project's row.
    /// </summary>
    /// <param name="workspace">The project.</param>
    /// <returns>The row, whose press folds the project.</returns>
    private Control ProjectRow(TreeWorkspace workspace)
    {
        var folded = _collapsed.Contains(workspace.Root);
        var text = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        text.Children.Add(new TextBlock
        {
            Text = folded ? "▸" : "▾",
            FontSize = 10,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
        });
        text.Children.Add(new TextBlock
        {
            Text = workspace.Name,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Opacity = workspace.Missing ? 0.5 : 0.9,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var row = RowButton(text, workspace.Missing);
        row.Click += (_, _) =>
        {
            if (!_collapsed.Remove(workspace.Root))
            {
                _collapsed.Add(workspace.Root);
            }

            Rebuild();
        };
        return row;
    }

    /// <summary>
    /// Builds one conversation's row.
    /// </summary>
    /// <param name="session">The conversation.</param>
    /// <returns>The row, with its marks and its menu.</returns>
    private Control SessionRow(TreeSession session)
    {
        var current = RailPresenter.Same(session.Path, _current);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Height = 30,
            Margin = new Thickness(12, 0, 0, 0),
        };

        var marks = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 26,
        };
        if (session.Unread)
        {
            marks.Children.Add(new TextBlock
            {
                Text = "●",
                FontSize = 8,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        if (session.Pinned)
        {
            marks.Children.Add(new TextBlock
            {
                Text = "★",
                FontSize = 9,
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        if (session.RuntimeId is { Length: > 0 } || current)
        {
            marks.Children.Add(new TextBlock
            {
                Text = "▸",
                FontSize = 9,
                Opacity = 0.5,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var title = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(session.Title) ? session.Name : session.Title,
            FontSize = 12,
            FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal,
            Opacity = session.Archived ? 0.45 : 0.92,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var subtitle = RailPresenter.SessionSubtitle(session, Language);
        if (session.Archived)
        {
            subtitle = subtitle.Length == 0
                ? MessageCatalog.Get(MessageKey.Common.Archived2, Language)
                : MessageCatalog.Get(MessageKey.Common.Archived2, Language) + " · " + subtitle;
        }

        if (subtitle.Length > 0)
        {
            title.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 10,
                Opacity = 0.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var open = new Button
        {
            Content = title,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        open.Click += (_, _) => Opened?.Invoke(session.Path);

        var menu = new Button
        {
            Content = new TextBlock { Text = "⋯", FontSize = 12, Opacity = 0.6 },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = SessionMenu(session),
        };

        grid.Children.Add(marks);
        Grid.SetColumn(open, 1);
        grid.Children.Add(open);
        Grid.SetColumn(menu, 2);
        grid.Children.Add(menu);
        return grid;
    }

    /// <summary>
    /// Builds the menu one conversation offers.
    /// </summary>
    /// <param name="session">The conversation.</param>
    /// <returns>The menu, with only the actions that make sense for this row.</returns>
    private MenuFlyout SessionMenu(TreeSession session)
    {
        var menu = new MenuFlyout();

        var open = new MenuItem { Header = MessageCatalog.Get(MessageKey.Common.Open, Language) };
        open.Click += (_, _) => Opened?.Invoke(session.Path);
        menu.Items.Add(open);

        var pin = new MenuItem
        {
            Header = MessageCatalog.Get(
                session.Pinned ? MessageKey.Common.Unpin : MessageKey.Common.PinSession,
                Language),
        };
        pin.Click += (_, _) => Pinned?.Invoke(session.Path, !session.Pinned);
        menu.Items.Add(pin);

        var archive = new MenuItem
        {
            Header = MessageCatalog.Get(
                session.Archived ? MessageKey.Common.Unarchive : MessageKey.Common.ArchiveSession,
                Language),
        };
        archive.Click += (_, _) => Archived?.Invoke(session.Path, !session.Archived);
        menu.Items.Add(archive);

        if (session.Unread)
        {
            var seen = new MenuItem { Header = MessageCatalog.Get(MessageKey.Common.MarkAsRead, Language) };
            seen.Click += (_, _) => MarkedViewed?.Invoke(session.Path);
            menu.Items.Add(seen);
        }

        return menu;
    }

    /// <summary>
    /// Builds a button that fills its row and shows the given content.
    /// </summary>
    /// <param name="content">What the row says.</param>
    /// <param name="faded">Whether the row stands for something that is gone.</param>
    /// <returns>The button.</returns>
    private Button RowButton(Control content, bool faded)
    {
        return new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 4, 4, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Opacity = faded ? 0.7 : 1,
        };
    }
}
