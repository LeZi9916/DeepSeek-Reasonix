using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Baseline;

/// <summary>
/// Decides whether the control tree accounts for every key a baseline names.
/// </summary>
/// <param name="Unaccounted">Baseline keys that no control claimed and no record explains.</param>
/// <param name="Dropped">Keys a control used to claim and no longer declares.</param>
/// <param name="Disputed">Keys a control claims while a record still calls them gaps.</param>
/// <remarks>
/// It is a pure function over four key sets, so the judgement can be held by cases
/// that cost nothing — no window, no session, no repository. The headless case
/// feeds it what the shell actually declared; the unit cases feed it the states
/// that must fail, which is the only way to show a green run means something.
/// </remarks>
public sealed record CoverageAudit(
    IReadOnlyList<string> Unaccounted,
    IReadOnlyList<string> Dropped,
    IReadOnlyList<string> Disputed)
{
    /// <summary>
    /// Gets a value indicating whether every key was accounted for exactly once.
    /// </summary>
    public bool Clean => Unaccounted.Count == 0 && Dropped.Count == 0 && Disputed.Count == 0;

    /// <summary>
    /// Audits four key sets against each other.
    /// </summary>
    /// <param name="baseline">Every key the captured domain names.</param>
    /// <param name="observed">Every key the shell's control tree declares.</param>
    /// <param name="covered">Every key the shell is known to have declared before.</param>
    /// <param name="recordedMissing">Every baseline key a record already calls a gap.</param>
    /// <returns>What is unaccounted for, what was dropped, and what is claimed twice.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <remarks>
    /// The four sets answer different questions and each failure is its own list:
    /// <list type="bullet">
    /// <item>A baseline key nobody claimed and nobody recorded is a page that does
    /// not draw what it was captured drawing.</item>
    /// <item>A key that used to be claimed and is not now is a control that
    /// stopped naming itself. It must fail even if a record excuses it, or adding
    /// a line to the record would be how a regression lands.</item>
    /// <item>A key both claimed and recorded is a record that has gone stale, and
    /// it hides a real comparison behind an excuse.</item>
    /// </list>
    /// </remarks>
    public static CoverageAudit Audit(
        IEnumerable<string> baseline,
        IEnumerable<string> observed,
        IEnumerable<string> covered,
        IEnumerable<string> recordedMissing)
    {
        if (baseline is null)
        {
            throw new ArgumentNullException(nameof(baseline));
        }

        if (observed is null)
        {
            throw new ArgumentNullException(nameof(observed));
        }

        if (covered is null)
        {
            throw new ArgumentNullException(nameof(covered));
        }

        if (recordedMissing is null)
        {
            throw new ArgumentNullException(nameof(recordedMissing));
        }

        var wanted = new HashSet<string>(baseline, StringComparer.Ordinal);
        var seen = new HashSet<string>(observed, StringComparer.Ordinal);
        var excused = new HashSet<string>(recordedMissing, StringComparer.Ordinal);

        var unaccounted = wanted
            .Where(key => !seen.Contains(key) && !excused.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var dropped = new HashSet<string>(covered, StringComparer.Ordinal)
            .Where(key => !seen.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var disputed = seen
            .Where(key => excused.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        return new CoverageAudit(unaccounted, dropped, disputed);
    }
}
