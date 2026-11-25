using Avalonia.Controls;
using Reasonix.Studio.State.Transcript;
using System;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Reuses nearby prose while bounding the views retained outside the virtual panel.</summary>
    /// <param name="realize">Creates a view for a projected row.</param>
    private sealed class ViewportItems(Func<ViewportRow, Control> realize) : ItemsControl
    {
        /// <summary>Limits off-screen prose reuse even when the loaded history keeps growing.</summary>
        private const int RecentLimit = 96;

        /// <summary>Prose views detached from Avalonia's recycled containers.</summary>
        private readonly (ViewportRow? Entry, Control? View)[] _recent = new (ViewportRow?, Control?)[RecentLimit];

        /// <summary>The next position in the bounded cache.</summary>
        private int _next;

        /// <inheritdoc/>
        protected override Type StyleKeyOverride => typeof(ItemsControl);

        /// <summary>Discards views whose theme or session has changed.</summary>
        internal void ClearRecent() => Array.Clear(_recent);

        /// <inheritdoc/>
        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new Border();

        /// <inheritdoc/>
        protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
        {
            if (container is not Border border || item is not ViewportRow entry)
            {
                return;
            }

            for (var i = 0; i < _recent.Length; i++)
            {
                if (ReferenceEquals(_recent[i].Entry, entry))
                {
                    border.Child = _recent[i].View;
                    _recent[i] = default;
                    return;
                }
            }

            border.Child = realize(entry);
        }

        /// <inheritdoc/>
        protected override void ClearContainerForItemOverride(Control container)
        {
            if (container is not Border { Child: { } child } border)
            {
                return;
            }

            border.Child = null;
            if (border.DataContext is ViewportRow entry && entry.Children.Count == 0
                && entry.Lead is UserItem or SayItem { Done: true, Text.Length: <= 8192, Reasoning.Length: <= 8192 } or SayItem { Done: true, Text.Length: <= 8192, Reasoning: null })
            {
                _recent[_next] = (entry, child);
                _next = (_next + 1) % _recent.Length;
            }
        }
    }
}
