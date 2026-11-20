using System.Collections.Generic;

namespace Reasonix.Studio.State;

/// <summary>
/// The token and cost totals for one session, and what this turn added.
/// </summary>
public sealed record Metrics
{
    /// <summary>
    /// Gets how many prompt tokens were served from the cache.
    /// </summary>
    public long Hit
    {
        get; init;
    }

    /// <summary>
    /// Gets how many prompt tokens missed the cache.
    /// </summary>
    public long Miss
    {
        get; init;
    }

    /// <summary>
    /// Gets how many completion tokens were billed.
    /// </summary>
    public long Out
    {
        get; init;
    }

    /// <summary>
    /// Gets what each source spent, split by currency for the same reason the
    /// session total is.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CurrencyAmount>> BySource
    {
        get; init;
    } =
        new Dictionary<string, IReadOnlyList<CurrencyAmount>>();

    /// <summary>
    /// Gets the session total, one entry per currency.
    /// </summary>
    /// <remarks>
    /// Never one scalar plus a label: the label could only name one of the
    /// currencies that were added.
    /// </remarks>
    public IReadOnlyList<CurrencyAmount> Cost { get; init; } = System.Array.Empty<CurrencyAmount>();

    /// <summary>
    /// Gets the prefix the last round was sampled against.
    /// </summary>
    /// <remarks>
    /// A cache that stops hitting is almost always a prefix that changed, and this
    /// hash is what tells "changed" from "the endpoint dropped it".
    /// </remarks>
    public string PrefixHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the last round saw its prefix change.
    /// </summary>
    public bool PrefixChanged
    {
        get; init;
    }

    /// <summary>
    /// Gets why the prefix changed, as the kernel named the reasons.
    /// </summary>
    public IReadOnlyList<string> PrefixReasons { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// Gets a value indicating whether the carried messages are the bytes last sent.
    /// </summary>
    /// <remarks>
    /// The prefix hash alone cannot tell a dropped cache from a rewritten
    /// conversation, because the conversation is not in it.
    /// </remarks>
    public bool BodyChanged
    {
        get; init;
    }

    /// <summary>
    /// Gets how many messages the last round carried.
    /// </summary>
    public int CarriedMessages
    {
        get; init;
    }

    /// <summary>
    /// Gets how many tokens the tool schemas took.
    /// </summary>
    /// <remarks>
    /// The third thing filling a prefix and the one nobody counts, so it is
    /// reported rather than inferred from the context breakdown.
    /// </remarks>
    public int ToolSchema
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether the amount is an estimate.
    /// </summary>
    /// <remarks>
    /// A number billed at a published price and one estimated from a fallback
    /// table are different claims.
    /// </remarks>
    public bool Estimated
    {
        get; init;
    }

    /// <summary>
    /// Gets how much of what this session billed carries a price fact.
    /// </summary>
    /// <remarks>
    /// A session that has spent nothing yet and one whose rounds went unpriced
    /// both total zero, and the amount cannot tell them apart — only this can.
    /// </remarks>
    public string Coverage { get; init; } = "none";

    /// <summary>
    /// Gets why a round fell short, carried for provenance.
    /// </summary>
    /// <remarks>
    /// Coverage is the state; this never stands in for it.
    /// </remarks>
    public string IncompleteReason { get; init; } = string.Empty;

    /// <summary>
    /// Gets the converted amount, when the host quoted in a second currency.
    /// </summary>
    /// <remarks>
    /// Kept apart rather than summed: adding them would require inventing a rate.
    /// </remarks>
    public CurrencyAmount? Alt
    {
        get; init;
    }

    /// <summary>
    /// Gets what this turn added, split by currency like the session total.
    /// </summary>
    public IReadOnlyList<CurrencyAmount> Turn { get; init; } = System.Array.Empty<CurrencyAmount>();

    /// <summary>
    /// Gets the per-round series behind the trend.
    /// </summary>
    /// <remarks>
    /// Bounded: a long session must not grow an unbounded array in the reducer.
    /// </remarks>
    public IReadOnlyList<long> Rounds { get; init; } = System.Array.Empty<long>();
}
