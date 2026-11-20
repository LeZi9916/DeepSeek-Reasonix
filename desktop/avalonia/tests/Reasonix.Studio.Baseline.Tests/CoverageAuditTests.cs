using Reasonix.Studio.Baseline;
using System;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the coverage judgement to the states it must reject.
/// </summary>
/// <remarks>
/// The headless case can only show that today's tree passes. These cases show what
/// failing looks like — a key nothing claimed, a key a control stopped declaring,
/// a key claimed while a record still calls it a gap — because a gate whose
/// failure mode is untested is a gate that turns green when it stops reading.
/// </remarks>
public sealed class CoverageAuditTests
{
    /// <summary>
    /// The baseline every case audits against.
    /// </summary>
    private static readonly string[] s_baseline = { "class:rail#0", "class:pane#0", "token:--text" };

    /// <summary>
    /// Checks that a tree claiming everything with nothing recorded is clean.
    /// </summary>
    [Fact]
    public void EverythingClaimedAndNothingRecordedIsClean()
    {
        var audit = CoverageAudit.Audit(s_baseline, s_baseline, s_baseline, Array.Empty<string>());

        Assert.True(audit.Clean, audit.ToString());
    }

    /// <summary>
    /// Checks that a baseline key nobody claimed and nobody recorded is reported.
    /// </summary>
    [Fact]
    public void AKeyNobodyClaimedIsReported()
    {
        var audit = CoverageAudit.Audit(
            s_baseline,
            new[] { "class:rail#0", "class:pane#0" },
            s_baseline,
            Array.Empty<string>());

        Assert.Equal(new[] { "token:--text" }, audit.Unaccounted);
        Assert.False(audit.Clean);
    }

    /// <summary>
    /// Checks that a key a control used to declare and no longer does fails, even
    /// when a record already excuses it.
    /// </summary>
    /// <remarks>
    /// This is the case that keeps the record from becoming the way a regression
    /// lands: moving a control's identity off it and onto the record must not turn
    /// the run green.
    /// </remarks>
    [Fact]
    public void AKeyThatStoppedBeingClaimedFailsEvenWhenRecorded()
    {
        var audit = CoverageAudit.Audit(
            s_baseline,
            new[] { "class:rail#0", "class:pane#0" },
            s_baseline,
            new[] { "token:--text" });

        Assert.Equal(new[] { "token:--text" }, audit.Dropped);
        Assert.Empty(audit.Unaccounted);
        Assert.False(audit.Clean);
    }

    /// <summary>
    /// Checks that a key both claimed and recorded as a gap is reported, because
    /// one of the two is wrong and the comparison is not being made.
    /// </summary>
    [Fact]
    public void AKeyClaimedAndRecordedIsReported()
    {
        var audit = CoverageAudit.Audit(s_baseline, s_baseline, s_baseline, new[] { "class:pane#0" });

        Assert.Equal(new[] { "class:pane#0" }, audit.Disputed);
        Assert.False(audit.Clean);
    }

    /// <summary>
    /// Checks that a key the record explains and no control claims is a clean gap,
    /// which is what an unbuilt page looks like today.
    /// </summary>
    [Fact]
    public void ARecordedGapThatIsNotClaimedIsClean()
    {
        var audit = CoverageAudit.Audit(
            s_baseline,
            new[] { "class:rail#0", "class:pane#0" },
            new[] { "class:rail#0", "class:pane#0" },
            new[] { "token:--text" });

        Assert.True(audit.Clean, audit.ToString());
    }

    /// <summary>
    /// Checks that the lists are ordered, so a failure message names the same key
    /// on every run rather than whichever the set happened to enumerate first.
    /// </summary>
    [Fact]
    public void FailuresAreOrdered()
    {
        var audit = CoverageAudit.Audit(
            new[] { "class:z#0", "class:a#0", "class:m#0" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.Equal(new[] { "class:a#0", "class:m#0", "class:z#0" }, audit.Unaccounted);
    }

    /// <summary>
    /// Checks that a missing argument is refused rather than read as an empty set,
    /// which would quietly report a clean audit.
    /// </summary>
    [Fact]
    public void AMissingArgumentIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => CoverageAudit.Audit(null!, s_baseline, s_baseline, s_baseline));
        Assert.Throws<ArgumentNullException>(() => CoverageAudit.Audit(s_baseline, null!, s_baseline, s_baseline));
        Assert.Throws<ArgumentNullException>(() => CoverageAudit.Audit(s_baseline, s_baseline, null!, s_baseline));
        Assert.Throws<ArgumentNullException>(() => CoverageAudit.Audit(s_baseline, s_baseline, s_baseline, null!));
    }
}
