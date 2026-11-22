using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Contracts.Wire;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.State;

/// <summary>Observes live arrivals separately from the host's recorded model usage.</summary>
public sealed class GenerationTelemetry
{
    /// <summary>Serializes stream observations and UI sampling.</summary>
    private readonly object _sync = new();

    /// <summary>Reads arrival time in milliseconds.</summary>
    private readonly Func<long> _clock;

    /// <summary>The latest four seconds of model output arrivals.</summary>
    private readonly List<(long At, int Tokens)> _samples = [];

    /// <summary>Observed cumulative argument counts, by call.</summary>
    private readonly Dictionary<string, int> _arguments = new(StringComparer.Ordinal);

    /// <summary>The latest turn's host-identified model attempts.</summary>
    private readonly Dictionary<string, ModelInterval> _rounds = new(StringComparer.Ordinal);

    /// <summary>One observation per second, capped at sixty.</summary>
    private readonly List<double> _trail = [];

    /// <summary>The last observed rate, peak and sampling time.</summary>
    private (double Rate, double Peak, long? SampledAt) _reading;

    /// <summary>Counts stream observations so a late trajectory read cannot overwrite live data.</summary>
    private long _revision;

    /// <summary>Initializes a new instance of the <see cref="GenerationTelemetry"/> class.</summary>
    /// <param name="clock">Optional observation clock.</param>
    public GenerationTelemetry(Func<long>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    /// <summary>Gets the current live observation generation.</summary>
    public long Revision
    {
        get
        {
            lock (_sync)
            {
                return _revision;
            }
        }
    }

    /// <summary>Observes one already ordered frame.</summary>
    /// <param name="frame">The accepted event and transport provenance.</param>
    public void Observe(WireFrame frame)
    {
        lock (_sync)
        {
            _revision++;
            ObserveCore(frame);
        }
    }

    /// <summary>Restores host-recorded output and first-token timing without inventing arrival timing.</summary>
    /// <param name="trace">Canonical trajectory response.</param><param name="revision">Generation at request start.</param>
    public void Restore(JsonObject trace, long revision)
    {
        lock (_sync)
        {
            if (revision != _revision || _revision != 0)
            {
                return;
            }

            _rounds.Clear();
            foreach (var item in trace["events"]?.AsArray() ?? [])
            {
                if (item is not null && WireFrame.TryParse(item.ToJsonString(), out var frame))
                {
                    ObserveCore(frame with
                    {
                        IsReplay = true,
                    });
                }
            }
        }
    }

    /// <summary>Returns current readings and records at most one trail point per elapsed second.</summary>
    /// <param name="running">The kernel's current run state.</param><returns>The metric values this observation supports.</returns>
    public GenerationReading Read(bool running)
    {
        lock (_sync)
        {
            var now = _clock();
            var rate = running && _samples.Count > 0 && now - _samples[^1].At <= 4000 ? _reading.Rate : 0;
            if (running && _reading.SampledAt is null)
            {
                _reading.SampledAt = now;
            }

            if (running && _reading.SampledAt is { } sampled && now - sampled >= 1000)
            {
                _reading.SampledAt = now;
                _trail.Add(rate);
                if (_trail.Count > 60)
                {
                    _trail.RemoveAt(0);
                }

                _reading.Peak = Math.Max(_reading.Peak, Math.Max(rate, 1));
            }

            var timed = _rounds.Values.Where(round => round.Duration >= 50 && round.Output > 0).ToArray();
            var seconds = timed.Sum(round => round.Duration) / 1000d;
            var average = seconds > 0 ? timed.Sum(round => round.Output) / seconds : 0;
            if (average > 1000)
            {
                average = 0;
            }

            return new GenerationReading(rate, average, (_rounds.Values.LastOrDefault(round => round.FirstTokenMs > 0)?.FirstTokenMs / 1000d) ?? 0, _rounds.Values.Sum(round => round.Output), seconds, _trail.ToArray(), Math.Max(1, _reading.Peak));
        }
    }

    /// <summary>Uses the reference frontend's streaming token estimate.</summary>
    /// <param name="text">Newly arrived output.</param><returns>Estimated tokens in this arrival.</returns>
    private static int Estimate(string text)
    {
        var narrow = 0;
        var wide = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value < 128)
            {
                narrow++;
            }
            else
            {
                wide++;
            }
        }

        return (int)Math.Ceiling(narrow / 4d) + wide;
    }

    /// <summary>Updates accounting from a frame while the observation lock is held.</summary>
    /// <param name="frame">Accepted live or replayed frame.</param>
    private void ObserveCore(WireFrame frame)
    {
        var payload = frame.Payload.Deserialize<Event>();
        if (payload is null)
        {
            return;
        }

        var now = _clock();
        if (frame.Kind == "turn_started")
        {
            _samples.Clear();
            _arguments.Clear();
            _rounds.Clear();
            _reading.Rate = 0;
            _reading.SampledAt = frame.IsReplay ? null : now;
            return;
        }

        if (payload.StreamAttempt is { } attempt)
        {
            if (attempt.Action == "begin")
            {
                _rounds[attempt.ID] = new ModelInterval { StartedAt = frame.IsReplay ? null : now };
            }
            else if (_rounds.TryGetValue(attempt.ID, out var round))
            {
                round.Duration = round.StartedAt is { } start && !frame.IsReplay ? Math.Max(0, now - start) : 0;
                if (attempt.Action == "commit")
                {
                    round.FirstTokenMs = attempt.FirstTokenMs;
                }
            }
        }

        if (payload.Usage is { } usage && usage.AttemptID is { Length: > 0 } id && _rounds.TryGetValue(id, out var measured))
        {
            measured.Output += usage.CompletionTokens;
        }

        if (frame.IsReplay)
        {
            return;
        }

        var tokens = frame.Kind is "text" or "reasoning" ? Estimate((payload.Text ?? string.Empty) + (payload.Reasoning ?? string.Empty)) : 0;
        if (frame.Kind == "tool_dispatch" && payload.Tool is { Partial: true, ArgChars: > 0, ID: { Length: > 0 } } tool)
        {
            var previous = _arguments.GetValueOrDefault(tool.ID);
            tokens += (int)Math.Ceiling(Math.Max(0, tool.ArgChars - previous) / 4d);
            _arguments[tool.ID] = Math.Max(previous, tool.ArgChars);
        }

        if (tokens <= 0)
        {
            return;
        }

        _samples.RemoveAll(sample => now - sample.At > 4000);
        _samples.Add((now, tokens));
        var span = (now - _samples[0].At) / 1000d;
        _reading.Rate = _samples.Count >= 2 && span > 0 ? _samples.Sum(sample => sample.Tokens) / span : 0;
    }

    /// <summary>Measurements attached to one host-issued attempt identifier.</summary>
    private sealed class ModelInterval
    {
        /// <summary>Gets or initializes the observed start, absent for replay.</summary>
        internal long? StartedAt
        {
            get; init;
        }

        /// <summary>Gets or sets elapsed live milliseconds.</summary>
        internal long Duration
        {
            get; set;
        }

        /// <summary>Gets or sets billed output tokens.</summary>
        internal long Output
        {
            get; set;
        }

        /// <summary>Gets or sets the host-measured first-token interval.</summary>
        internal long FirstTokenMs
        {
            get; set;
        }
    }
}
