using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Edits that still await readback, each attached to the conversation that initiated it.</summary>
    private readonly HashSet<(SessionEngine Engine, string Draft, ComposerEdit Edit)> _pendingComposerEdits = [];

    /// <summary>Independent settings that contribute to the tool-row pending feedback.</summary>
    private enum ComposerEdit
    {
        /// <summary>The task planning mode.</summary>
        Plan,

        /// <summary>The model to use on future requests.</summary>
        Model,

        /// <summary>The model's reasoning effort.</summary>
        Effort,

        /// <summary>The mode advertised by the selected model.</summary>
        ModelMode,

        /// <summary>The current branch or isolated worktree.</summary>
        Branch,
    }

    /// <summary>Checks whether the visible composer already has this edit in flight.</summary>
    /// <param name="edit">The control's setting identity.</param>
    /// <returns>Whether another edit of this kind still awaits readback.</returns>
    private bool IsComposerEditPending(ComposerEdit edit) =>
        _engine is { } engine && _pendingComposerEdits.Contains((engine, DraftKey, edit));

    /// <summary>Marks a newly requested change without blocking the other composer controls.</summary>
    /// <param name="ticket">The conversation and setting that initiated the change.</param>
    /// <returns>Whether this change can start.</returns>
    private bool BeginComposerEdit((SessionEngine Engine, string Draft, ComposerEdit Edit) ticket)
    {
        if (!_pendingComposerEdits.Add(ticket))
        {
            return false;
        }

        UpdateComposerPending();
        return true;
    }

    /// <summary>Clears feedback after the request settles, whether it succeeded or failed.</summary>
    /// <param name="ticket">The conversation and setting that initiated the change.</param>
    private void EndComposerEdit((SessionEngine Engine, string Draft, ComposerEdit Edit) ticket)
    {
        _pendingComposerEdits.Remove(ticket);
        UpdateComposerPending();
    }

    /// <summary>Applies a picker edit and holds the sweep through its status readback.</summary>
    /// <param name="engine">The conversation receiving the edit.</param>
    /// <param name="edit">The selected setting identity.</param>
    /// <param name="write">The kernel setting change.</param>
    /// <returns>The confirmed status of the active conversation.</returns>
    private async Task<SessionStatus?> RunComposerEditAsync(SessionEngine engine, ComposerEdit edit, Func<Task> write)
    {
        var draft = DraftKey;
        var ticket = (engine, draft, edit);
        if (!BeginComposerEdit(ticket))
        {
            return null;
        }

        SessionStatus? confirmed = null;
        try
        {
            await TryAnswerAsync(async () =>
            {
                await write().ConfigureAwait(true);
                if (engine != _engine || draft != DraftKey)
                {
                    return;
                }

                var status = await engine.StatusAsync(_closing.Token).ConfigureAwait(true);
                if (status is not null && engine == _engine && draft == DraftKey)
                {
                    confirmed = status;
                    ShowComposeStatus(status);
                }
            }).ConfigureAwait(true);
            return confirmed;
        }
        finally
        {
            EndComposerEdit(ticket);
        }
    }

    /// <summary>Projects only edits still owned by the visible conversation.</summary>
    private void UpdateComposerPending()
    {
        var model = IsComposerEditPending(ComposerEdit.Model) || IsComposerEditPending(ComposerEdit.Effort) || IsComposerEditPending(ComposerEdit.ModelMode);
        var plan = IsComposerEditPending(ComposerEdit.Plan);
        var branch = IsComposerEditPending(ComposerEdit.Branch);
        PlanMode.Classes.Set("pending", plan);
        ModelButton.Classes.Set("pending", model);
        EffortButton.Classes.Set("pending", model);
        ComposeBranch.Classes.Set("pending", branch);
        var pending = plan || model || branch;
        PendingComposerSweep.IsVisible = pending;
        PendingComposerSweep.Classes.Set("pending", pending);
    }
}
