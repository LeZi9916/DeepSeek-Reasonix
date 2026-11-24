using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Reasonix.Studio.State;

public sealed partial class SessionReducer
{
    /// <summary>Stores reserved child tracker progress independently from shell or tool output.</summary>
    /// <param name="state">The existing conversation.</param>
    /// <param name="progress">The kernel tracker update.</param>
    /// <returns>The updated tracker state.</returns>
    private static SessionState NoteSubagentProgress(SessionState state, Tool progress)
    {
        if (progress.Name != "reasonix.subagent.status" || string.IsNullOrEmpty(progress.ID))
        {
            return state;
        }

        var phase = progress.Output ?? string.Empty;
        var phases = state.SubagentPhases;
        if (!phases.TryGetValue(progress.ID, out var prior) || prior != phase)
        {
            phases = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(phases, StringComparer.Ordinal)
            {
                [progress.ID] = phase,
            });
        }

        var items = state.Items;
        if (progress.Profile is { Model.Length: > 0 } profile)
        {
            var rows = state.Items.Select(item =>
            {
                if (item is not ToolItem tool)
                {
                    return item;
                }

                var call = NoteProfile(tool.Call);
                var children = tool.Children.Select(NoteProfile).ToArray();
                return ReferenceEquals(call, tool.Call) && children.SequenceEqual(tool.Children)
                    ? item
                    : tool with
                    {
                        Call = call,
                        Children = children,
                    };
            }).ToArray();
            if (rows.Where((item, index) => !ReferenceEquals(item, items[index])).Any())
            {
                items = rows;
            }

            Tool NoteProfile(Tool call)
            {
                if (call.ID != progress.ID || (call.Profile?.Model == profile.Model && call.Profile.Effort == profile.Effort))
                {
                    return call;
                }

                var parent = call.Profile ?? new Profile();
                return call with
                {
                    Profile = parent with
                    {
                        Model = profile.Model,
                        Effort = profile.Effort,
                    },
                };
            }
        }

        return ReferenceEquals(phases, state.SubagentPhases) && ReferenceEquals(items, state.Items)
            ? state
            : state with
            {
                Items = items,
                SubagentPhases = phases,
            };
    }
}
