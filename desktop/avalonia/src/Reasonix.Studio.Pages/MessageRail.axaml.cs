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
    private (UserItem Item, Control Row)[] _marks = [];

    /// <summary>The measured heights of the individual marks along the track.</summary>
    private double[] _positions = [];

    /// <summary>The runtime that owns stars before the session is saved.</summary>
    private object? _draftOwner;

    /// <summary>The current conversation's bookmark scope.</summary>
    private string _scope = string.Empty;

    /// <summary>The index of the closest segment to the pointer or keyboard focus.</summary>
    private int _active = -1;

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

    /// <summary>Raised when a rail mark requests a jump to its message.</summary>
    internal event Action<Control>? JumpRequested;

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
    internal void SetMarks((UserItem Item, Control Row)[] marks, InterfaceLanguage language)
    {
        var sameMarks = marks.Length == _marks.Length && marks.Select(mark => mark.Item.Id).SequenceEqual(_marks.Select(mark => mark.Item.Id));
        if (!sameMarks)
        {
            _positions = [];
            BuildSegments();
        }

        _marks = marks;
        _language = language;
        SetAvailable(_available);
        AutomationProperties.SetName(Track, MessageCatalog.Get("chat.what_you_said", language));
        Hint.Text = MessageCatalog.Get("chat.click_to_jump_right_click", language);
    }

    /// <summary>Measures the marks against the scrolled transcript.</summary>
    /// <param name="scroll">The viewport of the transcript.</param>
    internal void MeasureMarks(ScrollViewer scroll)
    {
        if (!IsVisible || Bounds.Height <= 0)
        {
            return;
        }

        var height = Math.Max(1, scroll.Extent.Height);
        var positions = new double[_marks.Length];
        for (var index = 0; index < _marks.Length; index++)
        {
            var point = _marks[index].Row.TranslatePoint(default, scroll);
            positions[index] = Place(point is { } p ? (p.Y + scroll.Offset.Y) / height : index / (double)(_marks.Length - 1));
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
    private double Place(double fraction) => 18 + (Math.Clamp(fraction, 0, 1) * Math.Max(0, Bounds.Height - 36));

    /// <summary>Draws dense runs as one segment when gaps are under five pixels.</summary>
    private void BuildSegments()
    {
        CloseRun();
        Highlight(-1);
        foreach (var segment in _segments)
        {
            Track.Children.Remove(segment.Button);
        }

        _segments.Clear();
        for (var first = 0; first < _positions.Length;)
        {
            var last = first;
            while (last + 1 < _positions.Length && _positions[last + 1] - _positions[last] < 5 && _positions[last + 1] - _positions[first] <= 56)
            {
                last++;
            }

            AddSegment(first, last);
            first = last + 1;
        }
    }

    /// <summary>Places the input target and its visible line on the track.</summary>
    /// <param name="first">First mark in the segment.</param>
    /// <param name="last">Last mark in the segment.</param>
    private void AddSegment(int first, int last)
    {
        var clustered = last != first;
        var span = Math.Max(4, _positions[last] - _positions[first]);
        var line = new Border { Height = clustered ? span : 2, Width = clustered ? 12 : 10, CornerRadius = new CornerRadius(clustered ? 5 : 1) };
        line.Classes.Add("message-line");
        var button = new Button { Content = line, Width = 26, Height = clustered ? span + 8 : 12 };
        button.Classes.Add("message-mark");
        var text = _marks[first].Item.Text;
        var label = clustered
            ? MessageCatalog.Get("chat.jump_to_questions_a_to", _language, ("a", Number(first + 1)), ("b", Number(last + 1)))
            : MessageCatalog.Get("chat.jump_to_what_you_said", _language, ("text", text[..Math.Min(40, text.Length)]));
        AutomationProperties.SetName(button, label);
        var slot = _segments.Count;
        Canvas.SetLeft(button, 2);
        var middle = (_positions[first] + _positions[last]) / 2;
        Canvas.SetTop(button, middle - (button.Height / 2));
        button.GotFocus += (_, _) => Highlight(slot);
        button.LostFocus += (_, _) => Highlight(-1);
        button.Click += (_, _) =>
        {
            if (clustered && _opened != slot)
            {
                OpenRun(slot);
            }
            else
            {
                CloseRun();
                Jump(first);
            }
        };
        button.KeyDown += (_, args) =>
        {
            if (args.Key == Key.S)
            {
                Star(first);
                args.Handled = true;
            }
        };
        button.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(button).Properties.IsRightButtonPressed)
            {
                Star(first);
                args.Handled = true;
            }
        };
        _segments.Add((first, last, button, line));
        Track.Children.Add(button);
        RefreshStar(slot);
    }

    /// <summary>Formats a question number using the reader's language.</summary>
    /// <param name="value">The question number.</param>
    /// <returns>The formatted number.</returns>
    private string Number(int value) => Format.Count(value, _language);
}
