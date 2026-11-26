using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages;

/// <summary>Places each user message on the transcript's scroll rail.</summary>
internal sealed partial class MessageRail : UserControl
{
    /// <summary>The visible single marks and clusters.</summary>
    private readonly List<(int First, int Last, Button Button, Border Line)> _segments = [];

    /// <summary>Holds stars by canonical session scope.</summary>
    private readonly Dictionary<string, HashSet<int>> _stars = new(StringComparer.Ordinal);

    /// <summary>Closes the cluster after crossing the gap unless the reader enters it.</summary>
    private readonly DispatcherTimer _closer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>The text and mounted position associated with a mark.</summary>
    private UserItem[] _marks = [];

    /// <summary>The measured heights of the individual marks along the track.</summary>
    private double[] _positions = [];

    /// <summary>The runtime that owns stars before the session is saved.</summary>
    private object? _draftOwner;

    /// <summary>The current conversation's bookmark scope.</summary>
    private string _scope = string.Empty;

    /// <summary>The index of the closest segment to the pointer or keyboard focus.</summary>
    private int _active = -1;

    /// <summary>The displayed single message, even if the layout groups it with its neighbors.</summary>
    private string? _peekId;

    /// <summary>The index of the expanded cluster.</summary>
    private int _opened = -1;

    /// <summary>Whether the viewport is wide enough for the reference rail.</summary>
    private bool _available = true;

    /// <summary>The language for rail labels.</summary>
    private InterfaceLanguage _language;

    /// <summary>Initializes a new instance of the <see cref="MessageRail"/> class.</summary>
    public MessageRail()
    {
        InitializeComponent();
        Earlier.Click += (_, _) => EarlierRequested?.Invoke();
        _closer.Tick += (_, _) => CloseRun();
        Track.PointerMoved += (_, args) => Aim(args.GetPosition(Track).Y);
        Track.PointerExited += (_, _) =>
        {
            Highlight(-1);
            _closer.Start();
        };
        Track.PointerPressed += (_, args) =>
        {
            if (ReferenceEquals(args.Source, Track) && args.GetCurrentPoint(Track).Properties.IsLeftButtonPressed && _active >= 0)
            {
                Jump(NearestMark(_active, args.GetPosition(Track).Y));
            }
        };
        Run.PointerEntered += (_, _) => _closer.Stop();
        Run.PointerExited += (_, _) => _closer.Start();
    }

    /// <summary>Raised when the reader requests the preceding history window.</summary>
    internal event Action? EarlierRequested;

    /// <summary>Raised when a rail mark requests a jump to its message.</summary>
    internal event Action<string>? JumpRequested;

    /// <summary>Raised with the updated stars of a saved conversation.</summary>
    internal event Action<string, int[]>? BookmarksChanged;

    /// <summary>Loads saved stars from the desktop preferences.</summary>
    /// <param name="bookmarks">Canonical scope and persisted message indexes.</param>
    internal void LoadBookmarks(IReadOnlyDictionary<string, int[]> bookmarks)
    {
        foreach (var (scope, positions) in bookmarks)
        {
            if (scope.Length > 0 && positions is not null)
            {
                _stars[scope] = positions.ToHashSet();
            }
        }
    }

    /// <summary>Associates bookmarks with the conversation holding the marks.</summary>
    /// <param name="scope">The saved conversation identity, or empty before first save.</param>
    /// <param name="draftOwner">The runtime that owns unsaved marks.</param>
    internal void SetScope(string scope, object? draftOwner)
    {
        var sameOwner = ReferenceEquals(_draftOwner, draftOwner);
        if (_scope == scope && (scope.Length > 0 || sameOwner))
        {
            return;
        }

        var promote = _scope.Length == 0 && scope.Length > 0 && sameOwner;
        if (_stars.Remove(string.Empty, out var draftStars) && promote && draftStars.Count > 0)
        {
            if (_stars.TryGetValue(scope, out var saved))
            {
                saved.UnionWith(draftStars);
            }
            else
            {
                _stars[scope] = draftStars;
            }
        }
        else
        {
            promote = false;
        }

        _scope = scope;
        _draftOwner = draftOwner;
        if (promote)
        {
            PublishBookmarks();
        }

        CloseRun();
        Highlight(-1);
        for (var slot = 0; slot < _segments.Count; slot++)
        {
            RefreshStar(slot);
        }
    }

    /// <summary>Shows the history count on the rail when earlier messages are available.</summary>
    /// <param name="earlier">The number of preceding messages.</param>
    /// <param name="load">Whether the preceding page is available to load.</param>
    /// <param name="language">The interface language.</param>
    internal void SetEarlier(int earlier, bool load, InterfaceLanguage language)
    {
        var label = MessageCatalog.Get("chat.n_more_messages_before_these", language, ("n", Format.Count(earlier, language)));
        Earlier.Content = "↑ " + Format.Count(earlier, language);
        Earlier.IsVisible = earlier > 0 && load;
        AutomationProperties.SetName(Earlier, label);
        ToolTip.SetTip(Earlier, label);
    }

    /// <summary>Applies the reference's narrow-window fold.</summary>
    /// <param name="available">Whether the message rail can be shown.</param>
    internal void SetAvailable(bool available)
    {
        _available = available;
        IsVisible = available && _marks.Length >= 3;
        if (!IsVisible)
        {
            CloseRun();
            Highlight(-1);
        }
    }

    /// <summary>Associates the rail with the current transcript rows.</summary>
    /// <param name="marks">User messages and their mounted rows.</param>
    /// <param name="language">The shell's language.</param>
    internal void SetMarks(UserItem[] marks, InterfaceLanguage language)
    {
        var sameMarks = marks.Length == _marks.Length && marks.Select(mark => mark.Id).SequenceEqual(_marks.Select(mark => mark.Id));
        _marks = marks;
        _language = language;
        if (!sameMarks)
        {
            _positions = [];
            if (_segments.Count > 0)
            {
                _positions = Enumerable.Range(0, marks.Length).Select(index => Place(index / (double)Math.Max(1, marks.Length - 1))).ToArray();
                BuildSegments();
            }
        }

        SetAvailable(_available);
        AutomationProperties.SetName(Track, MessageCatalog.Get("chat.what_you_said", language));
        Hint.Text = MessageCatalog.Get("chat.click_to_jump_right_click", language);
    }

    /// <summary>Measures the marks against the scrolled transcript.</summary>
    /// <param name="scroll">The viewport of the transcript.</param>
    /// <param name="position">Measures or estimates a mark by its row identity.</param>
    internal void MeasureMarks(ScrollViewer scroll, Func<string, double> position)
    {
        if (!IsVisible || Bounds.Height <= 0)
        {
            return;
        }

        var positions = new double[_marks.Length];
        for (var index = 0; index < _marks.Length; index++)
        {
            positions[index] = Place(position(_marks[index].Id));
        }

        if (_positions.Length != positions.Length || _positions.Where((top, index) => Math.Abs(top - positions[index]) >= 0.25).Any())
        {
            _positions = positions;
            BuildSegments();
        }

        ShowViewport(scroll);
    }

    /// <summary>Moves the visible-range band when the reader scrolls.</summary>
    /// <param name="scroll">The viewport of the transcript.</param>
    internal void ShowViewport(ScrollViewer scroll)
    {
        var height = scroll.Extent.Height;
        Band.IsVisible = height > scroll.Viewport.Height + 1;
        var top = 0d;
        var bottom = 0d;
        if (Band.IsVisible)
        {
            top = Place(scroll.Offset.Y / height);
            var size = Math.Max(16, Place((scroll.Offset.Y + scroll.Viewport.Height) / height) - top);
            bottom = top + size;
            Canvas.SetTop(Band, top);
            Band.Height = size;
        }

        foreach (var (first, last, button, _) in _segments)
        {
            button.Classes.Set("seen", Band.IsVisible && _positions[last] >= top && _positions[first] <= bottom);
        }
    }

    /// <summary>Saves a snapshot of only the current persisted conversation.</summary>
    private void PublishBookmarks()
    {
        if (_scope.Length > 0)
        {
            BookmarksChanged?.Invoke(_scope, _stars[_scope].Order().ToArray());
        }
    }

    /// <summary>Maps the transcript's scroll extent into the padded track.</summary>
    /// <param name="fraction">The relative position in the transcript.</param>
    /// <returns>The vertical track coordinate.</returns>
    private double Place(double fraction)
    {
        var head = Earlier.IsVisible ? 38 : 18;
        return head + (Math.Clamp(fraction, 0, 1) * Math.Max(0, Bounds.Height - head - 18));
    }

    /// <summary>Draws dense runs as one segment when gaps are under five pixels.</summary>
    private void BuildSegments()
    {
        var selected = _active < 0 ? null : _segments[_active].Button;
        var opened = _opened < 0 ? null : _segments[_opened].Button;
        var prior = _segments.ToDictionary(segment => ((string, string))segment.Button.Tag!);
        _segments.Clear();
        for (var first = 0; first < _positions.Length;)
        {
            var last = first;
            while (last + 1 < _positions.Length && _positions[last + 1] - _positions[last] < 5 && _positions[last + 1] - _positions[first] <= 56)
            {
                last++;
            }

            if (prior.Remove((_marks[first].Id, _marks[last].Id), out var reusable))
            {
                PlaceSegment(first, last, reusable.Button, reusable.Line);
                _segments.Add((first, last, reusable.Button, reusable.Line));
                RefreshStar(_segments.Count - 1);
            }
            else
            {
                AddSegment(first, last);
            }

            first = last + 1;
        }

        foreach (var segment in prior.Values)
        {
            Track.Children.Remove(segment.Button);
        }

        for (var index = 0; index < _segments.Count; index++)
        {
            var oldIndex = Track.Children.IndexOf(_segments[index].Button);
            if (oldIndex != index + 1)
            {
                Track.Children.Move(oldIndex, index + 1);
            }
        }

        _opened = _segments.FindIndex(segment => segment.Button == opened);
        if (_opened >= 0)
        {
            var segment = _segments[_opened];
            PlaceOverlay(Run, (_positions[segment.First] + _positions[segment.Last]) / 2, 150);
        }
        else
        {
            CloseRun();
        }

        UpdateHighlight(_segments.FindIndex(segment => segment.Button == selected));
        var peek = Array.FindIndex(_marks, mark => mark.Id == _peekId);
        if (Peek.IsVisible && peek >= 0 && _opened < 0)
        {
            ShowPeek(peek);
        }
        else
        {
            Highlight(_active);
        }
    }

    /// <summary>Places the input target and its visible line on the track.</summary>
    /// <param name="first">First mark in the segment.</param>
    /// <param name="last">Last mark in the segment.</param>
    private void AddSegment(int first, int last)
    {
        var clustered = last != first;
        var line = new Border { Width = clustered ? 12 : 10, CornerRadius = new CornerRadius(clustered ? 5 : 1) };
        line.Classes.Add("message-line");
        var button = new Button { Content = line, Width = 26, Tag = (_marks[first].Id, _marks[last].Id) };
        button.Classes.Add("message-mark");
        var text = _marks[first].Text;
        var label = clustered
            ? MessageCatalog.Get("chat.jump_to_questions_a_to", _language, ("a", Number(first + 1)), ("b", Number(last + 1)))
            : MessageCatalog.Get("chat.jump_to_what_you_said", _language, ("text", text[..Math.Min(40, text.Length)]));
        AutomationProperties.SetName(button, label);
        PlaceSegment(first, last, button, line);
        button.GotFocus += (_, _) => Highlight(_segments.FindIndex(segment => segment.Button == button));
        button.LostFocus += (_, _) =>
        {
            if (_active >= 0 && _active < _segments.Count && _segments[_active].Button == button)
            {
                Highlight(-1);
            }
        };
        button.Click += (_, _) =>
        {
            var slot = _segments.FindIndex(segment => segment.Button == button);
            if (slot < 0)
            {
                return;
            }

            if (clustered && _opened != slot)
            {
                OpenRun(slot);
            }
            else
            {
                CloseRun();
                Jump(_segments[slot].First);
            }
        };
        button.KeyDown += (_, args) =>
        {
            if (args.Key == Key.S && _segments.FindIndex(segment => segment.Button == button) is var slot && slot >= 0)
            {
                Star(_segments[slot].First);
                args.Handled = true;
            }
        };
        button.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(button).Properties.IsRightButtonPressed && _segments.FindIndex(segment => segment.Button == button) is var slot && slot >= 0)
            {
                Star(_segments[slot].First);
                args.Handled = true;
            }
        };
        _segments.Add((first, last, button, line));
        Track.Children.Add(button);
        RefreshStar(_segments.Count - 1);
    }

    /// <summary>Updates a retained marker's geometry to the new transcript extent.</summary>
    /// <param name="first">First message in the segment.</param><param name="last">Last message in the segment.</param>
    /// <param name="button">The segment's input surface.</param><param name="line">The painted marker.</param>
    private void PlaceSegment(int first, int last, Button button, Border line)
    {
        var span = Math.Max(4, _positions[last] - _positions[first]);
        line.Height = first == last ? 2 : span;
        button.Height = first == last ? 12 : span + 8;
        Canvas.SetLeft(button, 2);
        var middle = (_positions[first] + _positions[last]) / 2;
        Canvas.SetTop(button, middle - (button.Height / 2));
    }

    /// <summary>Formats a question number using the reader's language.</summary>
    /// <param name="value">The question number.</param>
    /// <returns>The formatted number.</returns>
    private string Number(int value) => Format.Count(value, _language);
}
