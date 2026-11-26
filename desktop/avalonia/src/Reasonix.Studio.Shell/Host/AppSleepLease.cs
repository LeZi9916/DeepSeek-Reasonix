using System;

namespace Reasonix.Studio.Shell.Host;

/// <summary>Holds an application sleep inhibition while the host reports active work.</summary>
/// <param name="inhibit">Acquires one platform inhibition.</param>
internal sealed class AppSleepLease(Func<IDisposable> inhibit) : IDisposable
{
    /// <summary>The inhibition returned by the platform.</summary>
    private IDisposable? _held;

    /// <summary>Successful idle polls since work was last seen.</summary>
    private int _idle;

    /// <summary>Unanswered polls since the last usable result.</summary>
    private int _unanswered;

    /// <summary>Releases the current inhibition and clears its observation counts.</summary>
    public void Dispose()
    {
        _held?.Dispose();
        _held = null;
        _idle = 0;
        _unanswered = 0;
    }

    /// <summary>Takes or releases the inhibition from the host's results.</summary>
    /// <param name="running">Panes that are running a turn.</param>
    /// <param name="unknown">Panes whose status could not be read.</param>
    internal void Observe(int running, int unknown)
    {
        if (running > 0)
        {
            _idle = 0;
            _unanswered = 0;
            _held ??= inhibit();
        }
        else if (_held is not null && unknown == 0)
        {
            _unanswered = 0;
            if (++_idle >= 3)
            {
                Dispose();
            }
        }
        else if (_held is not null && ++_unanswered >= 12)
        {
            Dispose();
        }
    }
}
