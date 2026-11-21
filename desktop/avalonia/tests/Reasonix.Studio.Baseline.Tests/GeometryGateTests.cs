using Reasonix.Studio.Baseline;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// The geometry gate: renders the real window against the conversation the
/// reference frontend was captured on, and holds it there.
/// </summary>
/// <remarks>
/// The window is opened without a kernel and handed the captured conversation, so
/// what is measured is Studio's own drawing rather than whatever a kernel answered.
/// Differences that exist today are written down in <c>baseline/gate-debt.json</c>
/// and nothing else may join them: the file is a ratchet, and a new mismatch fails
/// the run. It is checked in the same shape as the findings it excuses, so a
/// deleted one is visible as an entry nobody needs any more.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class GeometryGateTests
{
    /// <summary>
    /// Where the test output goes.
    /// </summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeometryGateTests"/> class.
    /// </summary>
    /// <param name="output">Where the gate writes what it found.</param>
    public GeometryGateTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Holds the shell's conversation page to the captured boxes and inks.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    /// <remarks>
    /// Setting <c>STUDIO_GEOMETRY_RECORD=1</c> rewrites the debt file with what
    /// this run found, which is how a change that clears differences lowers the
    /// ratchet. Without it the run is a gate: every finding must already be on
    /// the list.
    /// </remarks>
    [Fact]
    public async Task TheConversationPageMatchesTheCapturedOne()
    {
        var observed = await GateFixture.Render();

        var domain = GateFixture.Domain();
        var findings = new GeometryComparer(GeometryComparerOptions.Studio).Compare(domain, observed);

        _output.WriteLine($"observed identities: {observed.Count}");
        _output.WriteLine($"baseline nodes: {domain.Nodes.Count}, tokens: {domain.Tokens.Count}");
        _output.WriteLine($"findings: {findings.Count}");

        if (Environment.GetEnvironmentVariable("STUDIO_GEOMETRY_RECORD") == "1")
        {
            var written = GateFixture.WriteDebt(findings);
            _output.WriteLine($"recorded: {written}");
            return;
        }

        var debt = GateFixture.ReadDebt();
        var fresh = findings.Where(f => !debt.Contains(GateFixture.KeyOf(f))).ToList();

        _output.WriteLine($"recorded as debt: {findings.Count - fresh.Count}, new: {fresh.Count}");

        foreach (var finding in fresh.Take(40))
        {
            _output.WriteLine($"  new [{finding.Kind}] {finding.Identity}: expected {finding.Expected} / actual {finding.Actual}");
        }

        Assert.True(
            fresh.Count == 0,
            $"{fresh.Count} difference(s) are not in baseline/gate-debt.json; first is {fresh.FirstOrDefault()}");
    }

    /// <summary>Checks both the mounted controls and the resolved resources in the dark appearance.</summary>
    /// <returns>The result of laying out the dark conversation.</returns>
    [Fact]
    public async Task TheDarkConversationMatchesTheCapturedOne()
    {
        var observed = await GateFixture.Render(dark: true);
        var findings = new GeometryComparer(GeometryComparerOptions.Studio).Compare(GateFixture.Domain(dark: true), observed);
        var debt = GateFixture.ReadDebt();
        Assert.All(findings, f => Assert.Contains(GateFixture.KeyOf(f), debt));
    }

    /// <summary>
    /// Reports debt the gate no longer finds, so a ratchet can be lowered.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    /// <remarks>
    /// Not a failure: a difference that stopped happening is an improvement, and
    /// failing on it would punish the change that made it. The list is printed so
    /// the next recording can write the smaller file.
    /// </remarks>
    [Fact]
    public async Task DebtThatNoLongerOccursIsReported()
    {
        var observed = await GateFixture.Render();
        var domain = GateFixture.Domain();
        var findings = new GeometryComparer(GeometryComparerOptions.Studio).Compare(domain, observed)
            .Select(GateFixture.KeyOf)
            .ToHashSet(StringComparer.Ordinal);

        var debt = GateFixture.ReadDebt();
        var stale = debt.Where(entry => !findings.Contains(entry)).OrderBy(e => e, StringComparer.Ordinal).ToList();
        _output.WriteLine($"recorded differences: {debt.Count}, no longer occurring: {stale.Count}");
        foreach (var entry in stale.Take(60))
        {
            _output.WriteLine($"  cleared: {entry}");
        }

        Assert.True(true);
    }
}
