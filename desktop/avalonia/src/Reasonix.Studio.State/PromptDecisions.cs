using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.State;

/// <summary>Settles a mounted prompt by the id shared by the kernel and all windows.</summary>
public static class PromptDecisions
{
    /// <summary>Applies a receipt including one received before the local HTTP acknowledgement.</summary>
    /// <param name="state">Current session state.</param>
    /// <param name="receipt">Decision emitted by the kernel.</param>
    /// <returns>The state with this prompt sealed.</returns>
    public static SessionState Receipt(SessionState state, DecisionReceipt receipt)
    {
        return Replace(state, item => item switch
        {
            AskItem ask when ask.Ask.ID == receipt.ID && ask.Answered is null => ask with
            {
                Answered = Array.Empty<IReadOnlyList<string>>(),
                Outcome = receipt.Outcome,
                Subject = receipt.Subject,
            },
            ApprovalItem approval when approval.Approval.ID == receipt.ID && approval.Open => approval with { Verdict = receipt.Outcome },
            _ => item,
        });
    }

    /// <summary>Retains the choices the kernel acknowledged to this window.</summary>
    /// <param name="state">Current state after any intervening events.</param>
    /// <param name="id">Kernel ask id.</param>
    /// <param name="answers">Accepted selections in question order.</param>
    /// <returns>The state with this question answered.</returns>
    public static SessionState Answer(SessionState state, string id, IReadOnlyList<IReadOnlyList<string>> answers)
    {
        return Replace(state, item => item is AskItem ask && ask.Ask.ID == id ? ask with { Answered = answers } : item);
    }

    /// <summary>Retains unrelated rows and changes only the first matching prompt.</summary>
    /// <param name="state">Current session.</param>
    /// <param name="change">The prompt transition.</param>
    /// <returns>The updated state.</returns>
    private static SessionState Replace(SessionState state, Func<TranscriptItem, TranscriptItem> change)
    {
        for (var i = 0; i < state.Items.Count; i++)
        {
            var before = state.Items[i];
            var after = change(before);
            if (!ReferenceEquals(before, after))
            {
                var items = state.Items.ToArray();
                items[i] = after;
                return state with
                {
                    Items = items,
                    Doing = before is AskItem && state.Doing == "app.waiting_for_your_answer"
                        ? (state.Running ? "common.running" : "common.idle")
                        : state.Doing,
                };
            }
        }

        return state;
    }
}
