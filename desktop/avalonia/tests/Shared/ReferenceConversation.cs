using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;
using Tool = Reasonix.Studio.Contracts.Wire.Tool;

namespace Reasonix.Studio.Testing;

/// <summary>The two turns used in the reference geometry fixture.</summary>
internal static class ReferenceConversation
{
    /// <summary>
    /// Builds the conversation the baseline was captured on.
    /// </summary>
    /// <returns>The state the captured window was showing.</returns>
    /// <remarks>
    /// The words are the captured ones. The gate is about boxes and inks, and a
    /// different sentence would wrap to a different height for a reason that has
    /// nothing to do with the layout being measured.
    /// </remarks>
    public static SessionState Create()
    {
        var items = new List<TranscriptItem>();
        for (var turn = 0; turn < 2; turn++)
        {
            items.Add(new UserItem
            {
                Id = $"user-{turn}",
                Text = $"第 {turn} 个问题，麻烦看一下这个文件。",
            });
            items.Add(new SayItem
            {
                Id = $"say-{turn}",
                Text = $"第 {turn} 段回答，说明刚才那一步做了什么。\n\n- 要点一\n- 要点二\n",
                Reasoning = "先看文件再决定改哪里。",
                Done = true,
            });
            items.Add(new ToolItem
            {
                Id = $"tool-{turn}",
                Call = new Tool
                {
                    Name = "edit_file",
                    Args = $"{{\"path\":\"internal/pkg/mod{turn}/file{turn}.go\"}}",
                    ReadOnly = false,
                },
            });
        }

        return new SessionState { Items = items };
    }
}
