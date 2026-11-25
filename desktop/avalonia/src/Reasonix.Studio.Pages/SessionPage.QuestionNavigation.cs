using Avalonia;
using Reasonix.Studio.State.Transcript;
using System.Linq;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Scrolls to the next or previous visible user-message position.</summary>
    /// <param name="next">Whether to move down the transcript.</param>
    public void MoveBetweenUserMessages(bool next)
    {
        var anchor = ReadAnchor();
        var start = anchor.Id is { } id ? _indices.GetValueOrDefault(id) : 0;
        var authored = _state.Items.OfType<UserItem>();
        var target = next
            ? authored.FirstOrDefault(row => Candidate(row, forward: true))
            : authored.LastOrDefault(row => Candidate(row, forward: false));
        if (target is not null && Locate(target.Id)?.TranslatePoint(default, _scroll) is { } point)
        {
            _scroll.Offset = _scroll.Offset.WithY(_scroll.Offset.Y + point.Y - 12);
        }

        bool Candidate(UserItem row, bool forward)
        {
            if (_realized.TryGetValue(row.Id, out var control) && control.TranslatePoint(default, _scroll) is { } point)
            {
                return forward ? point.Y > 12 : point.Y < -12;
            }

            var position = _indices.GetValueOrDefault(row.Id);
            return forward ? position > start : position < start;
        }
    }
}
