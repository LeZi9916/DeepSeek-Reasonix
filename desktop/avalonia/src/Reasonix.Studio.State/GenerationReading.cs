using System.Collections.Generic;

namespace Reasonix.Studio.State;

/// <summary>The speed figures supported by arrivals and recorded model attempts.</summary>
/// <param name="CurrentRate">Live window estimate.</param><param name="AverageRate">Timed model throughput.</param>
/// <param name="FirstTokenSeconds">Host-measured latency.</param><param name="OutputTokens">Recorded round output.</param>
/// <param name="ModelSeconds">Observed model time.</param><param name="Trail">Recent rate samples.</param>
/// <param name="Peak">Peak retained throughout this curve's lifetime.</param>
public sealed record GenerationReading(double CurrentRate, double AverageRate, double FirstTokenSeconds, long OutputTokens, double ModelSeconds, IReadOnlyList<double> Trail, double Peak);
