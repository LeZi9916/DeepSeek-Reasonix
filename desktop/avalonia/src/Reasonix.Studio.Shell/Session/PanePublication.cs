using Avalonia.Threading;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.State;
using System;

namespace Reasonix.Studio.Shell.Session;

/// <summary>Schedules at most one pending ordinary publication per runtime.</summary>
internal sealed class PanePublication : IDisposable
{
    /// <summary>The runtime supplying snapshots.</summary>
    private readonly SessionEngine _engine;

    /// <summary>Receives the newest state on the UI thread.</summary>
    private readonly Action<SessionEngine, SessionState> _present;

    /// <summary>Serializes worker publications and the UI drain.</summary>
    private readonly object _gate = new();

    /// <summary>The last accepted snapshot, used to detect UI boundaries.</summary>
    private SessionState? _received;

    /// <summary>The only snapshot waiting for a draw.</summary>
    private SessionState? _pending;

    /// <summary>The deferred drain owned by the UI thread.</summary>
    private IDisposable? _timer;

    /// <summary>Whether a Dispatcher operation or a timer already owns the drain.</summary>
    private bool _scheduled;

    /// <summary>Whether a prompt publication is queued.</summary>
    private bool _urgent;

    /// <summary>Whether the subscription has ended.</summary>
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="PanePublication"/> class.</summary>
    /// <param name="engine">Runtime supplying snapshots.</param>
    /// <param name="present">Receives a current state on the dispatcher.</param>
    internal PanePublication(SessionEngine engine, Action<SessionEngine, SessionState> present)
    {
        _engine = engine;
        _present = present;
        _engine.Changed += Offer;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _engine.Changed -= Offer;
        lock (_gate)
        {
            _disposed = true;
            _received = null;
            _pending = null;
        }

        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Queues one dispatcher drain and keeps the latest accepted snapshot.</summary>
    /// <param name="state">The state published by the engine.</param>
    private void Offer(SessionState state)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_engine.State, state) || ReferenceEquals(_received, state))
            {
                return;
            }

            var boundary = _received is not { } previous
                || previous.Running != state.Running
                || previous.Items.Count != state.Items.Count
                || previous.Terminal != state.Terminal
                || previous.ResponseWait != state.ResponseWait;

            _received = state;
            _pending = state;
            if (boundary && !_urgent)
            {
                _urgent = true;
                _scheduled = true;
                Dispatcher.UIThread.Post(Drain);
            }
            else if (!_scheduled)
            {
                _scheduled = true;
                Dispatcher.UIThread.Post(Arm, DispatcherPriority.Background);
            }
        }
    }

    /// <summary>Defers ordinary updates so they yield to input and layout.</summary>
    private void Arm()
    {
        lock (_gate)
        {
            if (_disposed || !_scheduled || _urgent || _timer is not null)
            {
                return;
            }

            _timer = DispatcherTimer.RunOnce(Drain, TimeSpan.FromMilliseconds(33), DispatcherPriority.Background);
        }
    }

    /// <summary>Draws the current snapshot if its runtime still owns it.</summary>
    private void Drain()
    {
        SessionState? state;
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            state = _pending;
            _pending = null;
            _scheduled = false;
            _urgent = false;
        }

        if (state is not null && ReferenceEquals(_engine.State, state))
        {
            _present(_engine, state);
        }
    }
}
