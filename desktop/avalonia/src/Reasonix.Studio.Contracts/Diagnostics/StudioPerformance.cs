using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;

namespace Reasonix.Studio.Contracts.Diagnostics;

/// <summary>Records desktop timings with the Reasonix-Studio-Performance event provider.</summary>
[EventSource(Name = "Reasonix-Studio-Performance")]
public sealed class StudioPerformance : EventSource
{
    /// <summary>Identifies the user action across its asynchronous stages.</summary>
    private static readonly AsyncLocal<long> s_operation = new();

    /// <summary>Gets the shared timing provider.</summary>
    public static StudioPerformance Log { get; } = new();

    /// <summary>Starts a trace operation if a listener is attached.</summary>
    /// <param name="name">Fixed operation name.</param>
    /// <returns>The operation scope.</returns>
    public static Operation Begin(string name) => Log.IsEnabled() ? new Operation(name) : default;

    /// <summary>Gets a monotonic timestamp when collecting performance data.</summary>
    /// <returns>The timestamp or zero when collection is disabled.</returns>
    public static long Timestamp() => Log.IsEnabled() ? Stopwatch.GetTimestamp() : 0;

    /// <summary>Records one stage with its absolute timestamp and elapsed time.</summary>
    /// <param name="name">Fixed stage name.</param>
    /// <param name="start">Monotonic start timestamp.</param>
    [NonEvent]
    public static void Record(string name, long start)
    {
        if (start != 0 && Log.IsEnabled())
        {
            Log.Timing(name, s_operation.Value, start, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    /// <summary>Records a startup animation tick interval.</summary>
    /// <param name="start">Monotonic timestamp of the preceding tick.</param>
    /// <param name="end">Monotonic timestamp of the current tick.</param>
    [NonEvent]
    public static void Frame(long start, long end)
    {
        if (start != 0 && end != 0)
        {
            Log.Timing("startup.animation-interval", 0, start, Stopwatch.GetElapsedTime(start, end).TotalMilliseconds);
        }
    }

    /// <summary>Emits an elapsed stage for a desktop action.</summary>
    /// <param name="name">Fixed stage name.</param>
    /// <param name="operation">Timestamp identifying the enclosing operation, if any.</param>
    /// <param name="start">Monotonic timestamp.</param>
    /// <param name="milliseconds">Elapsed milliseconds.</param>
    [Event(1, Level = EventLevel.Informational)]
    public void Timing(string name, long operation, long start, double milliseconds) =>
        WriteEvent(1, name, operation, start, milliseconds);

    /// <summary>Correlates stages within an asynchronous desktop action.</summary>
    public readonly struct Operation : IDisposable
    {
        /// <summary>The action name.</summary>
        private readonly string _name;

        /// <summary>The start time and correlation identifier.</summary>
        private readonly long _start;

        /// <summary>The parent operation identifier.</summary>
        private readonly long _previous;

        /// <summary>Initializes a new instance of the <see cref="Operation"/> struct.</summary>
        /// <param name="name">Fixed operation name.</param>
        internal Operation(string name)
        {
            _name = name;
            _start = Stopwatch.GetTimestamp();
            _previous = s_operation.Value;
            s_operation.Value = _start;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_start != 0)
            {
                Record(_name, _start);
                s_operation.Value = _previous;
            }
        }
    }
}
