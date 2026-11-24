using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.State.Transcript;

/// <summary>A delegate and the live reading of its child-run trackers.</summary>
/// <param name="Call">The requesting transcript row.</param>
/// <param name="Running">Whether the dispatch or a child remains active.</param>
public sealed record SubagentActivity(ToolItem Call, bool Running)
{
    /// <summary>Gets the kernel-declared number of agents in this dispatch.</summary>
    public int Count => System.Math.Max(Call.Call.Profile?.Count ?? 1, 1);

    /// <summary>Projects the canonical delegation rows for the session's meter rail.</summary>
    /// <param name="state">The current transcript and tracker phases.</param>
    /// <returns>Top-level delegations in transcript order.</returns>
    public static IEnumerable<SubagentActivity> Of(SessionState state)
    {
        foreach (var tool in state.Items.OfType<ToolItem>())
        {
            if (TranscriptLanes.IsDelegation(tool.Call))
            {
                yield return new SubagentActivity(tool, tool.Running || Active(tool.Call.ID) || tool.Children.Any(child => Active(child.ID)));
            }
        }

        bool Active(string? id) => id is not null && state.SubagentPhases.TryGetValue(id, out var phase)
            && phase.Length > 0 && phase is not ("completed" or "failed" or "cancelled");
    }
}
