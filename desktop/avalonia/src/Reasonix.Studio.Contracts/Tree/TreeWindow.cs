using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Reasonix.Studio.Contracts.Tree;

/// <summary>
/// The window a tree request asks for: which rows, in what order, and how many.
/// </summary>
/// <remarks>
/// Ordinary windows bound each project; a recent window pages across all of
/// them, so <see cref="Offset"/> is only meaningful with <see cref="Order"/> set
/// to <c>recent</c>. The kernel refuses the other combination rather than
/// guessing, and so does this.
/// </remarks>
public sealed record TreeWindow
{
    /// <summary>
    /// The most rows one project's window is served at.
    /// </summary>
    public const int MaxRows = 300;

    /// <summary>
    /// The most search text the kernel matches against.
    /// </summary>
    public const int MaxQueryBytes = 200;

    /// <summary>
    /// Gets the order to list in; empty for each project's own order.
    /// </summary>
    public string Order { get; init; } = string.Empty;

    /// <summary>
    /// Gets where a recent-ordered page begins.
    /// </summary>
    public int? Offset
    {
        get; init;
    }

    /// <summary>
    /// Gets how many rows each project contributes; 0 means every row.
    /// </summary>
    public int? Limit
    {
        get; init;
    }

    /// <summary>
    /// Gets per-project bounds, for a sidebar whose rows are sized unevenly.
    /// </summary>
    public IReadOnlyDictionary<string, int>? Limits
    {
        get; init;
    }

    /// <summary>
    /// Gets which rows the counts and the page cover: empty, <c>active</c> or
    /// <c>archived</c>.
    /// </summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>
    /// Gets the text to keep rows by, matched against titles, names and a
    /// project's own name.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the kernel should retry metadata it gave
    /// up on.
    /// </summary>
    public bool RetryMetadata
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether conversations kept in the 1.x store are
    /// left out.
    /// </summary>
    public bool HideLegacy
    {
        get; init;
    }

    /// <summary>
    /// Builds the query string this window sends.
    /// </summary>
    /// <returns>
    /// A query starting with <c>?</c>, or empty when the window asks for nothing
    /// beyond the default.
    /// </returns>
    /// <remarks>
    /// <c>progressive=1</c> is always sent: a first paint with the metadata that
    /// is already known beats a listing that waits for the slowest transcript.
    /// </remarks>
    public string QueryString()
    {
        var parts = new List<string>(8) { "progressive=1" };

        if (RetryMetadata)
        {
            parts.Add("retryMetadata=1");
        }

        if (!string.IsNullOrEmpty(Order))
        {
            parts.Add("order=" + Order);
        }

        if (Offset is { } offset)
        {
            parts.Add("offset=" + offset.ToString(CultureInfo.InvariantCulture));
        }

        if (HideLegacy)
        {
            parts.Add("hideLegacy=1");
        }

        if (Limit is { } limit)
        {
            parts.Add("limit=" + limit.ToString(CultureInfo.InvariantCulture));
        }

        if (Limits is { Count: > 0 })
        {
            var encoded = new StringBuilder("{");
            var first = true;
            foreach (var (root, rows) in Limits)
            {
                if (!first)
                {
                    encoded.Append(',');
                }

                first = false;
                encoded.Append(System.Text.Json.JsonSerializer.Serialize(root));
                encoded.Append(':');
                encoded.Append(rows.ToString(CultureInfo.InvariantCulture));
            }

            encoded.Append('}');
            parts.Add("limits=" + System.Uri.EscapeDataString(encoded.ToString()));
        }

        if (!string.IsNullOrEmpty(Scope))
        {
            parts.Add("scope=" + Scope);
        }

        var query = (Query ?? string.Empty).Trim();
        if (query.Length > 0 && Encoding.UTF8.GetByteCount(query) <= MaxQueryBytes)
        {
            parts.Add("q=" + System.Uri.EscapeDataString(query));
        }

        return "?" + string.Join("&", parts);
    }
}
