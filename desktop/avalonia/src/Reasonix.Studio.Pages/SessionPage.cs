using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Pages;

/// <summary>
/// The conversation page: every row the session produced, in order.
/// </summary>
/// <remarks>
/// The page is a projection of <see cref="SessionState"/> and holds no state of its
/// own, so the same state drawn twice is the same page. Rows are rebuilt only when
/// the transcript's composition changes; a message still being written grows its own
/// text in place, because rebuilding the list on every streamed chunk would drop the
/// reader's scroll position hundreds of times per answer.
/// </remarks>
public sealed partial class SessionPage : UserControl
{
    /// <summary>
    /// The column every row is drawn in.
    /// </summary>
    private readonly StackPanel _rows;

    /// <summary>Open forms retain unsent selections across unrelated stream events.</summary>
    private readonly Dictionary<string, QuestionForm> _forms = new(StringComparer.Ordinal);

    /// <summary>
    /// The scroll host, kept so a rebuild can restore where the reader was.
    /// </summary>
    private readonly ScrollViewer _scroll;

    /// <summary>
    /// The rows as they stand, in the order they were drawn, so a streamed chunk can
    /// extend the last one instead of rebuilding the list.
    /// </summary>
    private readonly List<Control> _drawn = new List<Control>();

    /// <summary>
    /// Holds the reader's own choice for the activity group, or null while the
    /// fold policy still decides.
    /// </summary>
    /// <remarks>
    /// One group per page: the reference frontend keeps one answer for the
    /// activity fold, and a per-group answer would leave a long turn with some
    /// groups open and others closed for no reason the reader could name.
    /// </remarks>
    private bool? _opened;

    /// <summary>
    /// Whether the reader has opened the turn's work line, when the setting folds
    /// it.
    /// </summary>
    /// <remarks>
    /// A closed fold is drawn hidden rather than left out: find, copy and a
    /// jump-to-row all reach into the body, and building it again on every open
    /// would throw away what was already drawn.
    /// </remarks>
    private bool _workOpen;

    /// <summary>
    /// The state the page is showing.
    /// </summary>
    private SessionState _state = new SessionState();

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionPage"/> class.
    /// </summary>
    public SessionPage()
    {
        _rows = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
            Margin = new Thickness(0),
        };

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _rows,
        };

        _scroll.ScrollChanged += (_, e) =>
        {
            var previousEnd = _scroll.ScrollBarMaximum.Y - e.ExtentDelta.Y + e.ViewportDelta.Y;
            if (e.ExtentDelta.Y > 0 && _scroll.Offset.Y >= previousEnd - 2)
            {
                _scroll.ScrollToEnd();
            }
        };
        Content = _scroll;
        ActualThemeVariantChanged += (_, _) => Rebuild(_state.Items);
    }

    /// <summary>
    /// Raised when the person answers an approval card.
    /// </summary>
    /// <remarks>
    /// The verdict travels as the id the kernel named and the three scope flags, so
    /// the page never decides what a "remember this" answer writes: the kernel owns
    /// that, and this only says which button was pressed.
    /// </remarks>
    public event Action<ApprovalVerdict>? Answered;

    /// <summary>Raised when the reader quotes a passage into the composer.</summary>
    public event Action<string>? QuoteRequested;

    /// <summary>
    /// Gets the state the page is showing.
    /// </summary>
    public SessionState State => _state;

    /// <summary>Gets or sets the acknowledgement path for a user's question answers.</summary>
    public Func<string, IReadOnlyList<QuestionSelection>, Task<bool>>? SubmitAnswers
    {
        get; set;
    }

    /// <summary>Gets or sets the acknowledgement path for editing a timed question.</summary>
    public Func<string, Task<bool>>? EngageQuestion
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the language the page's own labels are drawn in.
    /// </summary>
    /// <remarks>
    /// The kernel's words are drawn as it wrote them; only the labels this page
    /// adds — "read 3 files", "done · 1m 5s" — are translated here.
    /// </remarks>
    public InterfaceLanguage Language
    {
        get; set;
    } = InterfaceLanguage.Zh;

    /// <summary>
    /// Gets or sets the last row the reader is known to have seen.
    /// </summary>
    /// <remarks>
    /// The kernel keeps the unread mark, not where the reader stopped reading, so
    /// the window remembers that itself. Null means nothing is known and no
    /// marker is drawn: a line at a guessed place would claim the reader had seen
    /// something they had not.
    /// </remarks>
    public string? SeenThrough
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets a value indicating whether a turn's intermediate work starts
    /// folded under one line.
    /// </summary>
    /// <remarks>
    /// It is the kernel's <c>ui.fold_turn_work</c> setting, and it starts off,
    /// which is also the kernel's default. The body is drawn either way, so
    /// opening the line does not rebuild what was already there.
    /// </remarks>
    public bool FoldsTurnWork
    {
        get; set;
    }

    /// <summary>Updates presentation preferences without replacing the session's state.</summary>
    /// <param name="language">The window language.</param>
    /// <param name="fold">Whether to group intermediate work.</param>
    public void ApplyPreferences(InterfaceLanguage language, bool fold)
    {
        if (language != Language || fold != FoldsTurnWork)
        {
            Language = language;
            FoldsTurnWork = fold;
            Rebuild(_state.Items);
        }
    }

    /// <summary>
    /// Shows one state.
    /// </summary>
    /// <param name="state">The state to draw.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null.</exception>
    public void Show(SessionState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var previous = _state;
        _state = state;
        if (_rows.Children.Count == 0
            || (!ReferenceEquals(previous.Items, state.Items) && !UpdateAnswers(previous.Items, state.Items)))
        {
            Rebuild(state.Items);
        }
    }

    /// <summary>
    /// Reads the ink a notice is drawn in.
    /// </summary>
    /// <param name="level">The level the kernel reported.</param>
    /// <returns>The brush for that level, or null to inherit the row's ink.</returns>
    private IBrush? NoticeInk(string level)
    {
        return level switch
        {
            "warn" => Brush("WarnBrush"),
            "error" => Brush("ErrBrush"),
            _ => null,
        };
    }

    /// <summary>
    /// Reads one of the shell's brushes by the token name it was rendered under.
    /// </summary>
    /// <param name="key">The token name, without its leading dashes.</param>
    /// <returns>The brush, or null when this theme has no such token.</returns>
    private IBrush? Brush(string key)
    {
        if (Application.Current is { } app
            && app.TryGetResource(key, ActualThemeVariant, out var value)
            && value is IBrush brush)
        {
            return brush;
        }

        return null;
    }

    /// <summary>
    /// Draws one row.
    /// </summary>
    /// <param name="item">The row to draw.</param>
    /// <returns>The control that stands for it.</returns>
    /// <remarks>
    /// Every row is one <c>class:call</c> box around its body, and the two kinds
    /// whose body is prose carry <c>class:txt</c> — the two identities the
    /// captured conversation uses for a row and for its text. They are the same
    /// strings the geometry gate compares by, so the page's structure is what the
    /// reference frontend drew rather than a second tree that merely looks alike.
    ///
    /// A row sits 8 below the one before it, which is the gap the capture
    /// recorded between consecutive rows; the first row has nothing above it, so
    /// it carries none.
    /// </remarks>
    private Control RowOf(TranscriptItem item)
    {
        var body = BodyOf(item);
        var row = new Border
        {
            Child = body,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, _drawn.Count == 0 ? 0 : 8, 0, 0),
        };

        AutomationProperties.SetAutomationId(row, "class:call");
        return row;
    }

    /// <summary>
    /// Draws the body of one row.
    /// </summary>
    /// <param name="item">The row to draw.</param>
    /// <returns>The control holding what this row says.</returns>
    private Control BodyOf(TranscriptItem item)
    {
        switch (item)
        {
            case UserItem user:
                var bubble = new ContentControl
                {
                    [!TextBlock.FontSizeProperty] = StudioTypography.Resource("StudioReadingSize"),
                    FontWeight = FontWeight.Medium,
                    Background = Brush("OverlayBrush"),
                    CornerRadius = new CornerRadius(15),
                    Padding = new Thickness(14, 10),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Content = new SelectableTextBlock { Text = user.Text, LineHeight = 22.4, TextWrapping = TextWrapping.Wrap },
                };
                AutomationProperties.SetAutomationId(bubble, "class:txt");
                return bubble;

            case SayItem say:
                return new AnswerCard(say, Language, text => QuoteRequested?.Invoke(text));

            case NoticeItem notice:
                return new SelectableTextBlock
                {
                    Text = notice.Text,
                    [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                    Opacity = 0.7,
                    Foreground = NoticeInk(notice.Level),
                };

            case ApprovalItem approval:
                return ApprovalCardOf(approval);

            case ToolItem tool:
                return ToolRow(tool);

            case ReadsItem reads:
                return Meta(MessageCatalog.Get(
                    "chat.read_n_files",
                    Language,
                    ("n", Format.Count(reads.Tools.Count, Language))));

            case GuardianItem guardian:
                return Meta(MessageCatalog.Get(
                    "chat.guardian_review",
                    Language) + " · " + guardian.Assessment.Tool + " · " + guardian.Assessment.Outcome);

            case AskItem ask:
                return Card(AskBody(ask), Brush("OverlayBrush"));

            case CompactionItem fold:
                return Card(CompactionBody(fold), Brush("RaisedBrush"));

            case RememberItem remember:
                return Card(RememberBody(remember.Fact), Brush("RaisedBrush"));

            case WorkedItem worked:
                return Meta(MessageCatalog.Get(
                    "chat.done_in_t",
                    Language,
                    ("t", Format.Elapsed(worked.Milliseconds))));

            case ExtensionItem extension:
                return Card(ExtensionBody(extension), Brush("RaisedBrush"));

            default:
                return new SelectableTextBlock { Text = string.Empty };
        }
    }

    /// <summary>
    /// Draws a line of greyed-out detail, which is what the kernel's side remarks
    /// look like.
    /// </summary>
    /// <param name="text">What the line says.</param>
    /// <returns>The text control for it.</returns>
    private Control Meta(string text)
    {
        return new SelectableTextBlock
        {
            Text = text,
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
        };
    }

    /// <summary>
    /// Draws a card around a body.
    /// </summary>
    /// <param name="body">What the card holds.</param>
    /// <param name="background">The card's fill, or null to inherit.</param>
    /// <returns>The bordered card.</returns>
    private Control Card(Control body, IBrush? background)
    {
        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Child = body,
        };
    }

    /// <summary>
    /// Draws the question waiting on the person.
    /// </summary>
    /// <param name="item">The open question.</param>
    /// <returns>The card's body: a heading, then each question it asks.</returns>
    private Control AskBody(AskItem item)
    {
        if (item.Answered is null && SubmitAnswers is { } submit && EngageQuestion is { } engage
            && item.Ask.Questions.All(q => q.Surface is null))
        {
            if (!_forms.TryGetValue(item.Ask.ID, out var form))
            {
                form = new QuestionForm(item.Ask, Language, engage, submit);
                _forms[item.Ask.ID] = form;
            }
            else if (form.Parent is Border previous)
            {
                previous.Child = null;
            }

            return form;
        }

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new SelectableTextBlock
        {
            Text = MessageCatalog.Get("chat.ask_user", Language),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            Opacity = 0.6,
        });

        foreach (var question in item.Ask.Questions)
        {
            body.Children.Add(new SelectableTextBlock
            {
                Text = string.IsNullOrEmpty(question.Header) ? question.Prompt : question.Header,
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(13),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });

            if (!string.IsNullOrEmpty(question.Prompt) && !string.IsNullOrEmpty(question.Header))
            {
                body.Children.Add(new SelectableTextBlock
                {
                    Text = question.Prompt,
                    [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                    Opacity = 0.75,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            if (question.Options.Count > 0)
            {
                body.Children.Add(new SelectableTextBlock
                {
                    Text = string.Join("  ·  ", question.Options.Select(option => option.Label)),
                    [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        if (item.Answered is not null && !string.IsNullOrEmpty(item.Subject))
        {
            body.Children.Add(new SelectableTextBlock { Text = item.Subject, TextWrapping = TextWrapping.Wrap });
        }

        if (item.Answered is { Count: > 0 } answers)
        {
            body.Children.Add(new SelectableTextBlock
            {
                Text = string.Join("  ·  ", answers.Select(answer => string.Join(", ", answer))),
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                Opacity = 0.55,
            });
        }

        return body;
    }

    /// <summary>
    /// Draws a fold: its heading, and the summary as far as it has been written.
    /// </summary>
    /// <param name="item">The fold.</param>
    /// <returns>The card's body.</returns>
    private Control CompactionBody(CompactionItem item)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new SelectableTextBlock
        {
            Text = MessageCatalog.Get("chat.compaction_brief", Language),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(13),
            FontWeight = FontWeight.SemiBold,
        });

        if (!item.Done)
        {
            body.Children.Add(new SelectableTextBlock
            {
                Text = MessageCatalog.Get("chat.folding_into_a_digest", Language),
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                Opacity = 0.7,
            });
        }

        if (!string.IsNullOrEmpty(item.Compaction.Summary))
        {
            body.Children.Add(new SelectableTextBlock
            {
                Text = item.Compaction.Summary,
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 160,
            });
        }

        return body;
    }

    /// <summary>
    /// Draws a saved fact: what it is called and what it says.
    /// </summary>
    /// <param name="fact">The saved fact.</param>
    /// <returns>The card's body.</returns>
    private Control RememberBody(RememberedFact fact)
    {
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new SelectableTextBlock
        {
            Text = MessageCatalog.Get("data.memory", Language) + " · " + fact.Title,
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(13),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrEmpty(fact.Description))
        {
            body.Children.Add(new SelectableTextBlock
            {
                Text = fact.Description,
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return body;
    }

    /// <summary>
    /// Draws a surface an extension published.
    /// </summary>
    /// <param name="item">The surface.</param>
    /// <returns>The card's body: the plugin's name, its status, and any card text.</returns>
    private Control ExtensionBody(ExtensionItem item)
    {
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new SelectableTextBlock
        {
            Text = MessageCatalog.Get(
                "chat.extension_id",
                Language,
                ("id", string.IsNullOrEmpty(item.Surface.PluginID) ? item.Surface.SurfaceID : item.Surface.PluginID)),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            Opacity = 0.6,
        });

        if (!string.IsNullOrEmpty(item.Surface.Status?.Label))
        {
            body.Children.Add(new SelectableTextBlock
            {
                Text = item.Surface.Status.Label,
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(13),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (!string.IsNullOrEmpty(item.Surface.Status?.Detail))
        {
            body.Children.Add(new SelectableTextBlock
            {
                Text = item.Surface.Status.Detail,
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return body;
    }

    /// <summary>
    /// Draws a call waiting on the person's decision.
    /// </summary>
    /// <param name="item">The open approval.</param>
    /// <returns>The card, with the buttons its scope allows.</returns>
    private Control ApprovalCardOf(ApprovalItem item)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };

        // A settled card keeps its place but offers nothing: the decision was made,
        // and a second answer would name an approval the kernel no longer holds.
        if (item.Open)
        {
            buttons.Children.Add(VerdictButton(item.Approval.ID, MessageCatalog.Get(MessageKey.Chat.AllowOnce, Language), allow: true, session: false, persist: false));
            buttons.Children.Add(VerdictButton(item.Approval.ID, MessageCatalog.Get(MessageKey.Common.Refuse, Language), allow: false, session: false, persist: false));

            // The wider answers appear only where the kernel declared them, so the
            // card cannot offer a scope the call does not have.
            if (item.Approval.AllowsSession)
            {
                buttons.Children.Add(VerdictButton(item.Approval.ID, MessageCatalog.Get(MessageKey.Chat.AllowThisKindForThis, Language), allow: true, session: true, persist: false));
            }

            if (item.Approval.AllowsPersist)
            {
                buttons.Children.Add(VerdictButton(item.Approval.ID, MessageCatalog.Get(MessageKey.Chat.AlwaysAllowThisKindStop, Language), allow: true, session: true, persist: true));
            }
        }

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new SelectableTextBlock
        {
            Text = string.IsNullOrEmpty(item.Approval.Subject) ? item.Approval.Tool : item.Approval.Subject,
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(13),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrEmpty(item.Approval.Reason))
        {
            body.Children.Add(new SelectableTextBlock
            {
                Text = item.Approval.Reason,
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (item.Open)
        {
            body.Children.Add(buttons);
        }
        else
        {
            body.Children.Add(new SelectableTextBlock
            {
                Text = item.Verdict switch
                {
                    "allow" or "allow_once" => MessageCatalog.Get(MessageKey.Chat.AllowedOnce, Language),
                    "allow_session" or "recovery_continue_task" => MessageCatalog.Get(MessageKey.Chat.AllowedThisKindWillNot, Language),
                    "allow_persistent" => MessageCatalog.Get(MessageKey.Chat.AllowedRecordedAsARule, Language),
                    "deny" or "recovery_revise" => MessageCatalog.Get(MessageKey.Chat.Denied, Language),
                    _ => item.Verdict,
                },
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                Opacity = 0.55,
            });
        }

        return new Border
        {
            Background = Brush("OverlayBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Child = body,
        };
    }

    /// <summary>
    /// Builds one verdict button.
    /// </summary>
    /// <param name="id">The approval the kernel named.</param>
    /// <param name="label">What the button says.</param>
    /// <param name="allow">Whether the call may run.</param>
    /// <param name="session">Whether the answer covers the rest of this session.</param>
    /// <param name="persist">Whether the answer is remembered beyond this session.</param>
    /// <returns>The button, wired to the page's answer.</returns>
    private Button VerdictButton(string id, string label, bool allow, bool session, bool persist)
    {
        var button = new Button
        {
            Content = label,
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            Padding = new Thickness(10, 4, 10, 4),
        };

        button.Click += (_, _) => Answered?.Invoke(new ApprovalVerdict(id, allow, session, persist));
        return button;
    }

    /// <summary>
    /// Draws every row from scratch.
    /// </summary>
    /// <param name="items">The rows to draw.</param>
    private void Rebuild(IReadOnlyList<TranscriptItem> items)
    {
        if (_forms.Count > 0)
        {
            var open = items.OfType<AskItem>().Where(ask => ask.Answered is null).Select(ask => ask.Ask.ID).ToHashSet();
            foreach (var id in _forms.Keys.Where(id => !open.Contains(id)).ToArray())
            {
                _forms.Remove(id);
            }
        }

        var held = _scroll.Offset;
        _rows.Children.Clear();
        _drawn.Clear();

        if (items.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = MessageCatalog.Get(MessageKey.Composer.DescribeATaskQuestionOr, Language),
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(13),
                Opacity = 0.55,
            });
            return;
        }

        DrawRows(items);

        _scroll.Offset = held;
    }

    /// <summary>
    /// Updates answer text without disturbing the selection or the surrounding cards.
    /// </summary>
    /// <param name="previous">The rows already mounted.</param>
    /// <param name="next">The rows from the reducer.</param>
    /// <returns>Whether the existing controls still represent every row.</returns>
    private bool UpdateAnswers(IReadOnlyList<TranscriptItem> previous, IReadOnlyList<TranscriptItem> next)
    {
        if (previous.Count != next.Count || _drawn.Count != next.Count)
        {
            return false;
        }

        for (var i = 0; i < next.Count; i++)
        {
            if (ReferenceEquals(previous[i], next[i]))
            {
                continue;
            }

            if (previous[i] is not SayItem before || next[i] is not SayItem after || before.Id != after.Id
                || _drawn[i] is not Border { Child: AnswerCard answer })
            {
                return false;
            }

            answer.Update(after, Language);
        }

        return true;
    }
}
