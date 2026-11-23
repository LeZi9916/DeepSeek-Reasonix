using Reasonix.Studio.State.Transcript;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>The row preceded by the visible unread divider.</summary>
    private string? _newAnswerAt;

    /// <summary>Whether the reader wants unread dividers.</summary>
    private bool _showsNewAnswers = true;

    /// <summary>Gets or sets the unread span belonging to the displayed pane.</summary>
    public NewAnswerBoundary AnswerBoundary { get; set; } = new();

    /// <summary>Applies the display preference without discarding the pane's unread span.</summary>
    /// <param name="show">Whether to show the unread divider.</param>
    public void SetShowsNewAnswers(bool show)
    {
        _showsNewAnswers = show;
        RefreshNewAnswers();
    }

    /// <summary>Updates the divider after the rail publishes a newly viewed span.</summary>
    public void RefreshNewAnswers()
    {
        if (MoveNewAnswerLine())
        {
            Rebuild(_state.Items);
        }
    }

    /// <summary>Updates the visible position from the current pane, even if the divider is hidden.</summary>
    /// <returns>Whether the visible position changed.</returns>
    private bool MoveNewAnswerLine()
    {
        var anchor = AnswerBoundary.Resolve(_state.Items, _state.Running);
        var next = _showsNewAnswers ? anchor : null;
        if (next == _newAnswerAt)
        {
            return false;
        }

        _newAnswerAt = next;
        return true;
    }
}
