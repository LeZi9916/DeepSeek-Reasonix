using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Kernel.Session;

/// <summary>Restores bounded canonical history into the shared transcript projection.</summary>
internal static class HistoryProjection
{
    /// <summary>Restores messages while retaining dispatches crossing the page boundary.</summary>
    /// <param name="messages">Canonical page.</param><param name="context">Earlier tool dispatches.</param>
    /// <returns>The restored transcript rows.</returns>
    internal static List<TranscriptItem> Restore(IReadOnlyList<HistoryMessage> messages, IReadOnlyList<HistoryToolContext> context)
    {
        var items = new List<TranscriptItem>();
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var retained = context.GroupBy(c => c.Call.Id).ToDictionary(group => group.Key, group => group.First().Call, StringComparer.Ordinal);
        foreach (var message in messages)
        {
            var id = "history-" + message.MsgIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (message.Role == "user" && !message.HostAuthored)
            {
                items.Add(new UserItem { Id = id, Text = message.Content, AuthoredTurn = message.MsgIndex, MsgIndex = message.MsgIndex, At = message.CreatedAt == 0 ? null : message.CreatedAt, Steer = message.Steer });
            }
            else if (message.Role == "assistant")
            {
                if (message.Content.Length > 0 || message.Reasoning.Length > 0)
                {
                    items.Add(new SayItem { Id = id, Text = message.Content, Reasoning = message.Reasoning.Length > 0 ? message.Reasoning : null, Source = message.ModelRef.Length > 0 ? message.ModelRef : null, ThoughtMs = message.ThoughtMs, Done = true });
                }

                foreach (var call in message.ToolCalls)
                {
                    AddCall(call);
                }
            }
            else if (message.Role == "tool" && message.ToolCallId is { } callId)
            {
                if (!calls.ContainsKey(callId) && retained.TryGetValue(callId, out var call))
                {
                    AddCall(call);
                }

                if (!calls.TryGetValue(callId, out var index))
                {
                    continue;
                }

                var previous = (ToolItem)items[index];
                var output = message.Content;
                var hidden = Math.Max(0, output.Length - (64 * 1024));
                if (hidden > 0)
                {
                    output = output[..32768] + "\n…\n" + output[^32768..];
                }

                items[index] = previous with
                {
                    Hidden = hidden,
                    Call = previous.Call with
                    {
                        Output = output,
                        Err = message.ToolFailed ? message.Content : null!,
                        RefusalCode = message.ToolRefusalCode!,
                        OutputDiff = message.OutputDiff,
                    },
                };
            }
        }

        return items;

        void AddCall(HistoryToolCall call)
        {
            if (calls.ContainsKey(call.Id))
            {
                return;
            }

            calls[call.Id] = items.Count;
            items.Add(new ToolItem { Id = "history-tool-" + call.Id, Call = new Tool { ID = call.Id, Name = call.Name, Args = call.Arguments, ResolvedName = call.ResolvedName!, CapabilityID = call.CapabilityId!, ArgumentNormalization = call.ArgumentNormalization! }, Running = false });
        }
    }
}
