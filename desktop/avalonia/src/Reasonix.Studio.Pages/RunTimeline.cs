using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Pages;

/// <summary>The last turn's recorded signals and paired activities.</summary>
public sealed class RunTimeline
{
    /// <summary>Gets the rows in the order their first frame arrived.</summary>
    public IReadOnlyList<Entry> Rows { get; private init; } = Array.Empty<Entry>();

    /// <summary>Gets the extent of recorded activities in milliseconds.</summary>
    public double Span => Rows.Select(r => r.Start + r.Duration.GetValueOrDefault()).DefaultIfEmpty().Max();

    /// <summary>Projects the kernel record without using the replay reader's wall clock.</summary>
    /// <param name="events">The trajectory frames.</param>
    /// <returns>The last turn's timeline.</returns>
    public static RunTimeline From(JsonArray events)
    {
        var last = events.Select((node, index) => ((string?)node?["kind"], index)).LastOrDefault(x => x.Item1 == "turn_started").index;
        var turn = events.Skip(last).OfType<JsonObject>().ToArray();
        var anchor = (long?)turn.LastOrDefault(e => (string?)e["kind"] == "turn_done")?["workStartedAt"];
        var rows = new List<Entry>();
        var tools = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var attempts = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var frame in turn)
        {
            var kind = (string?)frame["kind"];
            var tool = frame["tool"];
            var id = (string?)tool?["id"];
            if (kind is "tool_dispatch" or "tool_result" or "tool_progress")
            {
                if (string.IsNullOrEmpty(id) || (bool?)tool?["partial"] == true)
                {
                    continue;
                }

                if (kind == "tool_dispatch" && !tools.ContainsKey(id))
                {
                    tools[id] = Add("tool", (string?)tool?["resolvedName"] ?? (string?)tool?["name"] ?? id);
                }

                if (tools.TryGetValue(id, out var call))
                {
                    call.Frames.Add(frame);
                    if (anchor is > 0 && (long?)tool?["startedAt"] is > 0 and var start)
                    {
                        call.Start = Math.Max(0, start - anchor.Value);
                    }

                    if (kind == "tool_result")
                    {
                        call.Duration = (double?)tool?["durationMs"];
                        call.Recovery = !string.IsNullOrEmpty((string?)tool?["err"]);
                        tools.Remove(id);
                    }
                }
            }
            else if (kind == "stream_attempt" && (string?)frame["streamAttempt"]?["id"] is { } attempt)
            {
                var action = (string?)frame["streamAttempt"]?["action"];
                if (action == "begin" && !attempts.ContainsKey(attempt))
                {
                    attempts[attempt] = Add("model", "model_round");
                }

                if (attempts.TryGetValue(attempt, out var round))
                {
                    round.Frames.Add(frame);
                    round.Recovery |= action == "discard";
                }
            }
            else if (kind == "usage" && (string?)frame["usage"]?["attemptId"] is { } billed && attempts.TryGetValue(billed, out var round))
            {
                round.Frames.Add(frame);
            }
            else if (kind is "turn_started" or "turn_done" or "message" or "retrying" or "usage" or "ask_request" or "approval_request" or "guardian_assessment" or "compaction_started" or "compaction_done" or "completion_summary" or "context_maintenance" or "todo_progress" or "steer")
            {
                var signal = Add(kind == "retrying" ? "recovery" : "system", kind);
                signal.Recovery = kind == "retrying";
                signal.Frames.Add(frame);
            }
        }

        return new RunTimeline { Rows = rows };

        Entry Add(string bucket, string label)
        {
            var row = new Entry(bucket, label);
            rows.Add(row);
            return row;
        }
    }

    /// <summary>One selectable timeline record.</summary>
    /// <param name="Bucket">Activity category.</param>
    /// <param name="Label">Tool name or event identity.</param>
    public sealed record Entry(string Bucket, string Label)
    {
        /// <summary>Gets the source events contributing to this row.</summary>
        public List<JsonObject> Frames { get; } = new();

        /// <summary>Gets the offset from the start of the turn in milliseconds.</summary>
        public double Start
        {
            get; internal set;
        }

        /// <summary>Gets the elapsed duration when the kernel supplied it.</summary>
        public double? Duration
        {
            get; internal set;
        }

        /// <summary>Gets a value indicating whether a retry or failure was recorded.</summary>
        public bool Recovery
        {
            get; internal set;
        }
    }
}
