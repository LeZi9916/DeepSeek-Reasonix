using Reasonix.Studio.Baseline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the gate to its own subject: every node the baseline names is either
/// claimed by a control or recorded as a known gap, and nothing is claimed that
/// the baseline never measured.
/// </summary>
/// <remarks>
/// A comparison that observes nothing reports no differences, which reads exactly
/// like agreement. These cases fail on that shape: an identity that quietly
/// stopped being declared, or a control that declares an identity nobody has a
/// box for, is a fault here even though the comparer would have nothing to say
/// about it.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class GeometryCoverageTests
{
    /// <summary>
    /// Where the case writes what it found.
    /// </summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeometryCoverageTests"/> class.
    /// </summary>
    /// <param name="output">Where the case writes what it found.</param>
    public GeometryCoverageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Checks that every baseline node key is accounted for: either this shell
    /// claims it, or the record lists it as a gap — and that no key the shell
    /// used to claim has stopped being declared.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    /// <remarks>
    /// The judgement itself lives in <see cref="CoverageAudit"/> so its failing
    /// states can be held by cheap cases. What this case supplies is the tree the
    /// shell actually declared.
    /// </remarks>
    [Fact]
    public async Task EveryBaselineKeyIsClaimedOrRecorded()
    {
        var observed = await GateFixture.Render();

        if (Environment.GetEnvironmentVariable("STUDIO_GEOMETRY_RECORD") == "1")
        {
            var written = GateFixture.WriteCovered(observed);
            _output.WriteLine($"recorded: {written}");
            return;
        }

        var domain = GateFixture.Domain();
        var audit = CoverageAudit.Audit(
            domain.Nodes.Select(GeometryComparer.NodeKey).Concat(domain.Tokens.Keys.Select(GeometryComparer.TokenKey)),
            observed.Keys,
            GateFixture.ReadCovered(),
            GateFixture.ReadDebt()
                .Where(entry => entry.StartsWith("missing ", StringComparison.Ordinal))
                .Select(entry => entry.Substring("missing ".Length))
                .Concat(GateFixture.ReadDebt()
                    .Where(entry => entry.StartsWith("token ", StringComparison.Ordinal))
                    .Select(entry => GeometryComparer.TokenKey(entry.Substring("token ".Length)))));

        _output.WriteLine($"baseline keys: {domain.Nodes.Count + domain.Tokens.Count}, claimed: {observed.Count}");
        _output.WriteLine($"unaccounted: {audit.Unaccounted.Count}, dropped: {audit.Dropped.Count}, disputed: {audit.Disputed.Count}");

        foreach (var key in audit.Dropped.Take(40))
        {
            _output.WriteLine($"  dropped: {key}");
        }

        foreach (var key in audit.Unaccounted.Take(40))
        {
            _output.WriteLine($"  unaccounted: {key}");
        }

        foreach (var key in audit.Disputed.Take(40))
        {
            _output.WriteLine($"  disputed: {key}");
        }

        Assert.True(audit.Clean, audit.ToString());
    }

    /// <summary>
    /// Checks that the shell declares no identity the baseline never measured,
    /// except where the debt file records the addition.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task EveryClaimedIdentityHasABaselineBoxOrIsRecorded()
    {
        var observed = await GateFixture.Render();
        var domain = GateFixture.Domain();
        var debt = GateFixture.ReadDebt();

        var known = domain.Nodes.Select(GeometryComparer.NodeKey).ToHashSet(StringComparer.Ordinal);
        var unrecorded = observed.Keys
            .Where(key => !key.StartsWith("token:", StringComparison.Ordinal))
            .Where(key => !known.Contains(key) && !debt.Contains("extra " + key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var declared = observed.Keys.Count(key => !key.StartsWith("token:", StringComparison.Ordinal));
        _output.WriteLine($"claimed identities: {declared}, not in the baseline: {unrecorded.Count}");
        foreach (var key in unrecorded.Take(40))
        {
            _output.WriteLine($"  unrecorded: {key}");
        }

        Assert.True(
            unrecorded.Count == 0,
            $"{unrecorded.Count} claimed identit(ies) have no baseline box and no record; first is {unrecorded.FirstOrDefault()}");
    }

    /// <summary>
    /// Checks that the identities this shell claims are the same set the baseline
    /// names, so a page cannot quietly build a second vocabulary beside it.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    /// <remarks>
    /// The comparison is on identities, not on keys: a page may legitimately
    /// declare fewer rows than the captured one while that row's step is still
    /// ahead, and the occurrence number is what says how many of each it drew.
    /// A name that appears on one side only is a different vocabulary.
    /// </remarks>
    [Fact]
    public async Task BothSidesUseTheSameIdentityVocabulary()
    {
        var observed = await GateFixture.Render();
        var domain = GateFixture.Domain();

        var baseline = domain.Nodes.Select(GeometryComparer.IdentityOf).ToHashSet(StringComparer.Ordinal);
        var shell = observed.Keys
            .Where(key => !key.StartsWith("token:", StringComparison.Ordinal))
            .Select(key => key.Substring(0, key.LastIndexOf('#')))
            .ToHashSet(StringComparer.Ordinal);

        // The shell declaring a name the baseline lacks is the dangerous
        // direction: the gate would never look at it, so a page could grow
        // controls nobody holds to anything.
        var invented = shell.Except(baseline).OrderBy(name => name, StringComparer.Ordinal).ToList();

        _output.WriteLine($"baseline identities: {baseline.Count}, shell identities: {shell.Count}");
        foreach (var name in invented.Take(40))
        {
            _output.WriteLine($"  invented: {name}");
        }

        Assert.True(
            invented.Count == 0,
            $"{invented.Count} identit(ies) are declared but the baseline never measured them; first is {invented.FirstOrDefault()}");
    }
}
