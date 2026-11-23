using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Linq;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Scrolls to the next or previous visible user-message position.</summary>
    /// <param name="next">Whether to move down the transcript.</param>
    public void MoveBetweenUserMessages(bool next)
    {
        var scroll = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null)
        {
            return;
        }

        var messages = this.GetVisualDescendants().OfType<UserMessage>()
            .Select(message => (Message: message, Y: message.TranslatePoint(default, scroll)?.Y))
            .Where(item => item.Y is not null)
            .OrderBy(item => item.Y)
            .ToArray();
        var target = next ? messages.FirstOrDefault(item => item.Y > 12) : messages.LastOrDefault(item => item.Y < -12);
        if (target.Y is double y)
        {
            scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + y - 12);
        }
    }
}
