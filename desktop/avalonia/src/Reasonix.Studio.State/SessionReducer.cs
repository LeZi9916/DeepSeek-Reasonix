using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Reasonix.Studio.State;

/// <summary>
/// Folds the kernel's events into the state one conversation is drawn from.
/// </summary>
/// <remarks>
/// A pure function of the state and one event, so the same stream replayed onto
/// the same state produces the same transcript. That is what makes a resumed
/// window and a live one agree, and it is why nothing here reads the clock except
/// through a value the caller supplies.
/// </remarks>
public sealed class SessionReducer
{
    /// <summary>
    /// The message key the chip shows while waiting for the workspace.
    /// </summary>
    private const string WaitingWorkspace = "app.waiting_for_the_workspace";

    /// <summary>
    /// The message key the chip shows between a tool returning and the model's
    /// next packet.
    /// </summary>
    private const string WaitingModel = "composer.waiting_for_the_model";

    /// <summary>
    /// How much of a running call's output the page keeps: the end of it.
    /// </summary>
    /// <remarks>
    /// A progress frame carries only the newest chunk, and a call that prints for
    /// minutes would otherwise grow without bound.
    /// </remarks>
    private const int ToolOutputTail = 64 * 1024;

    /// <summary>
    /// Mints row identities.
    /// </summary>
    private readonly IdSequence _ids;

    /// <summary>
    /// Folds streamed text onto the message being written.
    /// </summary>
    private readonly TextAppender _text;

    /// <summary>
    /// Reads the current time in unix milliseconds.
    /// </summary>
    private readonly Func<long> _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionReducer"/> class.
    /// </summary>
    /// <param name="ids">Mints row identities.</param>
    /// <param name="clock">Reads the current time in unix milliseconds.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ids"/> is null.</exception>
    public SessionReducer(IdSequence ids, Func<long>? clock = null)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _text = new TextAppender(ids, _clock);
    }

    /// <summary>
    /// Folds one frame into the state.
    /// </summary>
    /// <param name="state">The state before the frame.</param>
    /// <param name="frame">The frame, as the subscription delivered it.</param>
    /// <returns>The state after the frame.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null.</exception>
    public SessionState Reduce(SessionState state, WireFrame frame)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var payload = JsonSerializer.Deserialize<Event>(frame.Payload.GetRawText());
        if (payload is null)
        {
            return state;
        }

        var next = Apply(state, payload);
        if (ReferenceEquals(next.Items, state.Items))
        {
            return next;
        }

        // Streaming text grows a row without changing the transcript's
        // composition, so it must not bump the revision: everything derived from
        // the cards would otherwise recompute hundreds of times per answer.
        if (payload.Kind is "text" or "reasoning")
        {
            return next;
        }

        return next with
        {
            Revision = state.Revision + 1,
        };
    }

    /// <summary>
    /// Reports whether every step of a plan is done.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <returns>True when the list has steps and all of them are complete.</returns>
    private static bool AllStepsDone(IReadOnlyList<PlanStep> plan)
    {
        foreach (var step in plan)
        {
            if (step.Status != TodoStatus.Completed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads how a turn ended off the frame that ended it.
    /// </summary>
    /// <param name="payload">The frame that ended the turn.</param>
    /// <returns>The terminal the transcript should draw.</returns>
    private static TurnTerminal TerminalOf(Event payload)
    {
        // Cancellation carries an error of its own, so the flag is what tells a
        // stop apart from a dropped connection.
        if (payload.Cancelled)
        {
            return new CancelledTerminal();
        }

        if (payload.Outcome == "no_progress")
        {
            return new IncompleteTerminal(payload.Outcome);
        }

        if (!string.IsNullOrEmpty(payload.Err))
        {
            return new FailedTerminal(payload.Err);
        }

        if (!string.IsNullOrEmpty(payload.Outcome))
        {
            return new IncompleteTerminal(payload.Outcome);
        }

        return TurnTerminal.Completed;
    }

    /// <summary>
    /// Reads which cut, if any, marks a thought that was still arriving.
    /// </summary>
    /// <param name="payload">The frame that ended the turn.</param>
    /// <returns>The cut, or null when the turn ended cleanly after the answer.</returns>
    private static ThoughtCut? ThoughtCutFor(Event payload)
    {
        if (payload.Cancelled)
        {
            return ThoughtCut.Stopped;
        }

        if (!string.IsNullOrEmpty(payload.Err) && payload.Outcome != "no_progress")
        {
            return ThoughtCut.Failed;
        }

        return null;
    }

    /// <summary>
    /// Reads what the status chip says after a turn ends.
    /// </summary>
    /// <param name="payload">The frame that ended the turn.</param>
    /// <returns>The message key the chip shows.</returns>
    private static string DoingAfter(Event payload)
    {
        if (payload.Outcome == "no_progress")
        {
            return "app.held";
        }

        if (payload.Cancelled)
        {
            return "common.cancelled";
        }

        return string.IsNullOrEmpty(payload.Err) ? "common.done_2" : "common.interrupted";
    }

    /// <summary>
    /// Reports whether two runtime notices say the same standing fact.
    /// </summary>
    /// <param name="notice">The notice already on screen.</param>
    /// <param name="payload">The frame that may repeat it.</param>
    /// <returns>True when the frame adds nothing.</returns>
    private static bool SameFact(RuntimeNotice notice, Event payload)
    {
        var code = string.IsNullOrEmpty(notice.Code) ? notice.Text : notice.Code;
        var next = string.IsNullOrEmpty(payload.Code) ? payload.Text : payload.Code;
        return code == next && (notice.Detail ?? string.Empty) == (payload.Detail ?? string.Empty);
    }

    /// <summary>
    /// Merges a later frame's fields over an earlier one.
    /// </summary>
    /// <remarks>
    /// The wire omits <c>partial: false</c>, so spreading a full dispatch over the
    /// streaming placeholder would leave its flag standing and every dispatched
    /// call would read as one that never started.
    /// </remarks>
    /// <param name="previous">The call as it stood.</param>
    /// <param name="next">The fields the later frame carries.</param>
    /// <returns>The merged call, with <c>partial</c> taken from the later frame.</returns>
    private static Tool Merge(Tool previous, Tool next)
    {
        return next with
        {
            Output = string.IsNullOrEmpty(next.Output) ? previous.Output : next.Output,
            ArgChars = next.ArgChars > 0 ? next.ArgChars : previous.ArgChars,
            StartedAt = next.StartedAt > 0 ? next.StartedAt : previous.StartedAt,
            Partial = next.Partial,
        };
    }

    /// <summary>
    /// Reports whether a call holds another as one of its children.
    /// </summary>
    /// <param name="item">The call's card.</param>
    /// <param name="id">The child's identity.</param>
    /// <returns>True when the child is nested one level down.</returns>
    private static bool ContainsChild(ToolItem item, string id)
    {
        foreach (var child in item.Children)
        {
            if (child.ID == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Nests one call under the card that owns its parent.
    /// </summary>
    /// <param name="items">The transcript so far.</param>
    /// <param name="tool">The child call.</param>
    /// <returns>
    /// The transcript with the call nested, or null when no card owns its parent.
    /// </returns>
    private static IReadOnlyList<TranscriptItem>? FoldIntoParent(IReadOnlyList<TranscriptItem> items, Tool tool)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not ToolItem parent)
            {
                continue;
            }

            if (parent.Call.ID != tool.ParentID && !ContainsChild(parent, tool.ParentID))
            {
                continue;
            }

            var kids = new List<Tool>(parent.Children);
            var at = kids.FindIndex(c => c.ID == tool.ID);
            if (at >= 0)
            {
                kids[at] = Merge(kids[at], tool);
            }
            else
            {
                kids.Add(tool);
            }

            var nested = new List<TranscriptItem>(items)
            {
                [i] = parent with
                {
                    Children = kids,
                },
            };
            return nested;
        }

        return null;
    }

    /// <summary>
    /// Folds one usage frame into the session's totals.
    /// </summary>
    /// <param name="metrics">The totals before the frame.</param>
    /// <param name="usage">The frame's usage.</param>
    /// <returns>The totals after the frame.</returns>
    private static Metrics FoldUsage(Metrics metrics, Usage usage)
    {
        var rounds = new List<long>(metrics.Rounds)
        {
            usage.TotalTokens,
        };
        var diagnostics = usage.CacheDiagnostics;

        // A round that carried no diagnostics leaves the last answer standing
        // instead of blanking the block mid-turn.
        return metrics with
        {
            Hit = metrics.Hit + usage.CacheHitTokens,
            Miss = metrics.Miss + usage.CacheMissTokens,
            Out = metrics.Out + usage.CompletionTokens,
            PrefixHash = string.IsNullOrEmpty(diagnostics?.PrefixHash) ? metrics.PrefixHash : diagnostics.PrefixHash,
            PrefixChanged = diagnostics?.PrefixChanged ?? metrics.PrefixChanged,
            BodyChanged = diagnostics?.BodyChanged ?? metrics.BodyChanged,
            CarriedMessages = diagnostics?.CarriedMessages ?? metrics.CarriedMessages,
            ToolSchema = diagnostics?.ToolSchemaTokens ?? metrics.ToolSchema,
            Estimated = usage.Estimated || metrics.Estimated,
            Rounds = rounds,
        };
    }

    /// <summary>
    /// Applies one decoded frame.
    /// </summary>
    /// <param name="state">The state before the frame.</param>
    /// <param name="payload">The decoded frame.</param>
    /// <returns>The state after the frame.</returns>
    private SessionState Apply(SessionState state, Event payload)
    {
        switch (payload.Kind)
        {
            case "turn_started":
                return BeginTurn(state, payload);

            case "reasoning":
                return state with
                {
                    Doing = "composer.thinking",
                    Items = _text.Append(state.Items, payload.Reasoning + payload.Text, reasoning: true, payload.Source),
                };

            case "text":
                return state with
                {
                    Doing = "composer.answering",
                    Items = _text.Append(state.Items, payload.Text, reasoning: false, payload.Source),
                };

            case "tool_dispatch":
                return DispatchTool(state, payload);

            case "tool_progress":
                return payload.Tool is null
                    ? state
                    : state with
                    {
                        Items = FoldProgress(state.Items, payload.Tool),
                    };

            case "tool_result":
                return SettleTool(state, payload);

            case "usage":
                return payload.Usage is null
                    ? state
                    : state with
                    {
                        OutLive = 0,
                        Metrics = FoldUsage(state.Metrics, payload.Usage),
                    };

            case "notice":
                return ApplyNotice(state, payload);

            case "steer":
                return ApplySteer(state, payload);

            case "approval_request":
                return payload.Approval is null
                    ? state
                    : state with
                    {
                        Doing = "app.waiting_for_approval",
                        Items = new List<TranscriptItem>(state.Items)
                        {
                            new ApprovalItem
                            {
                                Id = _ids.Next(),
                                Approval = payload.Approval,
                            },
                        },
                    };

            case "turn_done":
                return EndTurn(state, payload);

            default:
                return state;
        }
    }

    /// <summary>
    /// Folds a dispatch into the transcript.
    /// </summary>
    /// <param name="state">The state before the frame.</param>
    /// <param name="payload">The dispatch frame.</param>
    /// <returns>The state with the call shown as running.</returns>
    private SessionState DispatchTool(SessionState state, Event payload)
    {
        if (payload.Tool is null)
        {
            return state;
        }

        // A dispatch ends the message being written: the next chunk belongs to a
        // reply that has not started yet, and appending it to the row above would
        // put a second answer's words inside the first. A partial dispatch is the
        // same call still streaming its arguments, so it ends the thought only —
        // its response still owes a message frame.
        var sealedItems = payload.Tool.Partial
            ? _text.SealThought(state.Items)
            : _text.Seal(state.Items, all: false);

        return state with
        {
            Doing = payload.Tool.Name,
            Items = FoldTool(sealedItems, payload.Tool, running: true),
        };
    }

    /// <summary>
    /// Folds a result into the transcript.
    /// </summary>
    /// <param name="state">The state before the frame.</param>
    /// <param name="payload">The result frame.</param>
    /// <returns>The state with the call settled.</returns>
    private SessionState SettleTool(SessionState state, Event payload)
    {
        if (payload.Tool is null)
        {
            return state;
        }

        // A returned call hands the turn back to the model, so the wait starts
        // again here. A sub-agent's call does not, because the parent turn was
        // never the thing waiting on it.
        var handedBack = string.IsNullOrEmpty(payload.Tool.ParentID) && state.Running;
        return state with
        {
            Doing = handedBack ? WaitingModel : state.Doing,
            Items = FoldTool(state.Items, payload.Tool, running: false),
        };
    }

    /// <summary>
    /// Opens a new turn, clearing how the last one ended.
    /// </summary>
    /// <param name="state">The state before the frame.</param>
    /// <param name="payload">The frame that started the turn.</param>
    /// <returns>The state with the turn open.</returns>
    private SessionState BeginTurn(SessionState state, Event payload)
    {
        // A second start naming this turn's own message is the same open delivered
        // twice: the run is already going, so only the naming runs.
        if (state.Running && payload.MsgIndex is { } index && index == state.TurnMsgIndex)
        {
            return state with
            {
                TurnMsgIndex = index,
            };
        }

        return state with
        {
            Running = true,
            RunSince = _clock(),
            Doing = "common.running",
            Terminal = null,
            OutLive = 0,
            TurnModel = string.IsNullOrEmpty(payload.ModelRef) ? state.TurnModel : payload.ModelRef,
            TurnMsgIndex = payload.MsgIndex ?? state.TurnMsgIndex,
            Items = _text.Seal(_text.SealThought(state.Items), all: true),
        };
    }

    /// <summary>
    /// Closes the turn, settling what was still open.
    /// </summary>
    /// <param name="state">The state before the frame.</param>
    /// <param name="payload">The frame that ended the turn.</param>
    /// <returns>The state with the turn closed.</returns>
    private SessionState EndTurn(SessionState state, Event payload)
    {
        // A plan that ran to the end is spent: struck through in the rail it reads
        // as if the next turn already has one. One that still has open steps is
        // the half the person needs.
        var plan = state.Plan.Count > 0 && AllStepsDone(state.Plan)
            ? Array.Empty<PlanStep>()
            : state.Plan;

        return state with
        {
            Running = false,
            Terminal = TerminalOf(payload),
            Doing = DoingAfter(payload),
            Plan = plan,
            Items = _text.Seal(state.Items, all: true, ThoughtCutFor(payload)),
        };
    }

    /// <summary>
    /// Applies a notice, which is a fact about the machine or about the turn.
    /// </summary>
    /// <param name="state">The state before the frame.</param>
    /// <param name="payload">The notice frame.</param>
    /// <returns>The state after the notice.</returns>
    private SessionState ApplyNotice(SessionState state, Event payload)
    {
        var level = string.IsNullOrEmpty(payload.Level) ? "info" : payload.Level;

        // A notice about the runtime is not a turn in the conversation, so it does
        // not join the record of one. A warning needs to be seen now, so it takes
        // a place of its own; the same standing fact does not stack.
        if (payload.Audience == "operator")
        {
            return level == "info" ? state : AddRuntimeNotice(state, payload, level);
        }

        if (payload.Code == "workspace_lease")
        {
            var waiting = new List<TranscriptItem>(state.Items)
            {
                NoticeOf(payload, level),
            };
            return state with
            {
                Doing = WaitingWorkspace,
                Items = waiting,
            };
        }

        // A retry that repeats says the same thing each time; three identical
        // lines push what came before them off the screen and read as three
        // problems, so a repeat folds into the one already there.
        var last = state.Items.Count > 0 ? state.Items[state.Items.Count - 1] as NoticeItem : null;
        if (last is not null && last.Level == level && last.Code == payload.Code && last.Text == payload.Text)
        {
            var folded = new List<TranscriptItem>(state.Items)
            {
                [state.Items.Count - 1] = last with
                {
                    Count = (last.Count ?? 1) + 1,
                    Detail = payload.Detail,
                },
            };
            return state with
            {
                Items = folded,
            };
        }

        var appended = new List<TranscriptItem>(state.Items)
        {
            NoticeOf(payload, level),
        };
        return state with
        {
            Items = appended,
        };
    }

    /// <summary>
    /// Adds a notice about the running machine, unless the same fact is already shown.
    /// </summary>
    /// <param name="state">The state before the frame.</param>
    /// <param name="payload">The notice frame.</param>
    /// <param name="level">The level the frame carries.</param>
    /// <returns>The state, unchanged when the fact was already standing.</returns>
    private SessionState AddRuntimeNotice(SessionState state, Event payload, string level)
    {
        foreach (var notice in state.Runtime)
        {
            if (SameFact(notice, payload))
            {
                return state;
            }
        }

        var added = new List<RuntimeNotice>(state.Runtime)
        {
            new RuntimeNotice
            {
                Id = _ids.Next(),
                Level = level,
                Code = payload.Code,
                Text = payload.Text,
                Detail = payload.Detail,
            },
        };
        return state with
        {
            Runtime = added,
        };
    }

    /// <summary>
    /// Builds the transcript row for one notice.
    /// </summary>
    /// <param name="payload">The notice frame.</param>
    /// <param name="level">The level the frame carries.</param>
    /// <returns>The row to add.</returns>
    private NoticeItem NoticeOf(Event payload, string level)
    {
        return new NoticeItem
        {
            Id = _ids.Next(),
            Level = level,
            Text = payload.Text,
            Detail = payload.Detail,
            Code = payload.Code,
        };
    }

    /// <summary>
    /// Applies guidance said into a turn already running.
    /// </summary>
    /// <param name="state">The state before the frame.</param>
    /// <param name="payload">The steer frame.</param>
    /// <returns>The state after the guidance is seated.</returns>
    private SessionState ApplySteer(SessionState state, Event payload)
    {
        for (var i = 0; i < state.Items.Count; i++)
        {
            if (state.Items[i] is not UserItem user || !user.Pending)
            {
                continue;
            }

            var mine = string.IsNullOrEmpty(payload.Text) || user.Text == payload.Text;
            if (!mine)
            {
                continue;
            }

            // The row enters where the turn read it, not where it was typed: the
            // work that ran while it waited would otherwise read as work done
            // about it.
            var moved = new List<TranscriptItem>(state.Items);
            moved.RemoveAt(i);
            moved.Add(user with
            {
                Pending = false,
                Steer = true,
            });
            return state with
            {
                Items = moved,
            };
        }

        var asNew = new List<TranscriptItem>(state.Items)
        {
            new UserItem
            {
                Id = _ids.Next(),
                Text = payload.Text,
                Pending = false,
                Steer = true,
                At = _clock(),
            },
        };
        return state with
        {
            Items = asNew,
        };
    }

    /// <summary>
    /// Folds one call into the transcript, nesting it under the call that spawned it.
    /// </summary>
    /// <param name="items">The transcript so far.</param>
    /// <param name="tool">The call.</param>
    /// <param name="running">Whether the call is still running.</param>
    /// <returns>The transcript with the call folded in.</returns>
    private IReadOnlyList<TranscriptItem> FoldTool(IReadOnlyList<TranscriptItem> items, Tool tool, bool running)
    {
        // A sub-agent's calls carry parentId; they belong inside the task that
        // spawned them, not as siblings in the main flow.
        if (!string.IsNullOrEmpty(tool.ParentID))
        {
            var nested = FoldIntoParent(items, tool);
            if (nested is not null)
            {
                return nested;
            }
        }

        if (!string.IsNullOrEmpty(tool.ID))
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not ToolItem existing || existing.Call.ID != tool.ID)
                {
                    continue;
                }

                var merged = new List<TranscriptItem>(items)
                {
                    [i] = existing with
                    {
                        Call = Merge(existing.Call, tool),
                        Running = running,
                        Omitted = running ? existing.Omitted : null,
                    },
                };
                return merged;
            }
        }

        var appended = new List<TranscriptItem>(items)
        {
            new ToolItem
            {
                Id = _ids.Next(),
                Call = tool,
                Running = running,
            },
        };
        return appended;
    }

    /// <summary>
    /// Folds one progress frame onto the call it belongs to.
    /// </summary>
    /// <param name="items">The transcript so far.</param>
    /// <param name="tool">The progress frame's call.</param>
    /// <returns>The transcript with the output extended.</returns>
    private IReadOnlyList<TranscriptItem> FoldProgress(IReadOnlyList<TranscriptItem> items, Tool tool)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not ToolItem existing || existing.Call.ID != tool.ID)
            {
                continue;
            }

            var joined = existing.Call.Output + tool.Output;
            var cut = Math.Max(0, joined.Length - ToolOutputTail);
            var extended = new List<TranscriptItem>(items)
            {
                [i] = existing with
                {
                    Call = existing.Call with
                    {
                        Output = cut > 0 ? joined[cut..] : joined,
                    },
                    Running = true,
                    Omitted = cut > 0 ? (existing.Omitted ?? 0) + cut : existing.Omitted,
                },
            };
            return extended;
        }

        return FoldTool(items, tool, running: true);
    }
}
