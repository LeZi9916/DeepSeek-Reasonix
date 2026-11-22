using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Reasonix.Studio.Pages.Rail;

public sealed partial class ProjectRail
{
    /// <summary>Retains the pressed row until its native release has dispatched.</summary>
    private readonly RailPointerState _pointerState = new();

    /// <summary>Preserves the native click target while canonical tree updates arrive.</summary>
    private void InitializePointerStability()
    {
        AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    _pointerState.Pointer = e.Pointer;
                }
            },
            RoutingStrategies.Tunnel,
            true);
        AddHandler(PointerReleasedEvent, (_, _) => ReleasePointerRows(), RoutingStrategies.Bubble, true);
        AddHandler(PointerCaptureLostEvent, (_, _) => ReleasePointerRows(), RoutingStrategies.Bubble, true);
        DetachedFromVisualTree += (_, _) => ReleasePointerRows();
    }

    /// <summary>Defers visual replacement without delaying canonical state updates.</summary>
    /// <returns>Whether a pressed row still owns native input.</returns>
    private bool DeferPointerRebuild()
    {
        if (_pointerState.Pointer is null)
        {
            return false;
        }

        _pointerState.Pending = true;
        return true;
    }

    /// <summary>Applies the latest canonical tree after the native button dispatch completes.</summary>
    private void ReleasePointerRows()
    {
        _pointerState.Pointer = null;
        if (!_pointerState.Pending)
        {
            return;
        }

        _pointerState.Pending = false;
        Dispatcher.UIThread.Post(Rebuild);
    }

    /// <summary>Groups the lifetime of a native press and its deferred canonical projection.</summary>
    private sealed class RailPointerState
    {
        /// <summary>Gets or sets the pointer currently pressing a rail row.</summary>
        internal IPointer? Pointer
        {
            get; set;
        }

        /// <summary>Gets or sets a value indicating whether the latest tree needs projection.</summary>
        internal bool Pending
        {
            get; set;
        }
    }
}
