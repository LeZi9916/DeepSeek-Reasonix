using Reasonix.Studio.Contracts.Diagnostics;
using System;
using System.Diagnostics.Tracing;
using System.IO;

namespace Reasonix.Studio.Shell.Host;

/// <summary>Writes opt-in desktop performance events to a local tab-separated log.</summary>
internal sealed class StudioPerformanceLog : EventListener
{
    /// <summary>Serializes event writes and shutdown.</summary>
    private readonly object _gate = new();

    /// <summary>The active output, which is flushed when the shell exits.</summary>
    private StreamWriter? _output;

    /// <summary>Initializes a new instance of the <see cref="StudioPerformanceLog"/> class.</summary>
    /// <param name="output">The buffered local output.</param>
    private StudioPerformanceLog(StreamWriter output)
    {
        _output = output;
        EnableEvents(StudioPerformance.Log, EventLevel.Informational);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        base.Dispose();
        lock (_gate)
        {
            try
            {
                _output?.Dispose();
            }
            catch (IOException)
            {
                ShellLog.Line("performance log write failed");
            }

            _output = null;
        }
    }

    /// <summary>Opens the trace destination selected by the environment.</summary>
    /// <returns>The active listener when tracing is enabled.</returns>
    internal static StudioPerformanceLog? Open()
    {
        var path = Environment.GetEnvironmentVariable("REASONIX_STUDIO_PERF_LOG");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return new StudioPerformanceLog(new StreamWriter(path, append: true, System.Text.Encoding.UTF8, bufferSize: 32 * 1024));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShellLog.Line("performance log unavailable");
            return null;
        }
    }

    /// <inheritdoc/>
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventId != 1 || eventData.Payload is not { Count: 4 } fields)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                _output?.WriteLine(FormattableString.Invariant($"{fields[0]}\t{fields[1]}\t{fields[2]}\t{fields[3]}"));
            }
            catch (IOException)
            {
                DisableEvents(StudioPerformance.Log);
            }
        }
    }
}
