using Reasonix.Studio.Baseline;
using System;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the one list that excuses a difference to its own rules.
/// </summary>
/// <remarks>
/// A gate is only as strict as the places it stays quiet. The list is allowed to
/// exist — some CSS properties have no equivalent here — but every entry has to
/// name a property the comparison actually visits and say what takes its place.
/// Without this, a property could be added under an excuse for a property that is
/// never compared, which widens nothing and hides the next real difference behind
/// a list that reads as if it were considered.
/// </remarks>
public sealed class UncomparableAuditTests
{
    /// <summary>
    /// Checks that every excused property is one the comparison visits.
    /// </summary>
    [Fact]
    public void EveryExcusedPropertyIsCompared()
    {
        var options = GeometryComparerOptions.Studio;

        var unknown = options.Uncomparable.Keys
            .Where(property => !options.Properties.Contains(property, StringComparer.Ordinal))
            .OrderBy(property => property, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unknown.Count == 0,
            $"excused but never compared: {string.Join(", ", unknown)}");
    }

    /// <summary>
    /// Checks that every excused property carries a reason.
    /// </summary>
    [Fact]
    public void EveryExcuseCarriesAReason()
    {
        var options = GeometryComparerOptions.Studio;

        var silent = options.Uncomparable
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => entry.Key)
            .OrderBy(property => property, StringComparer.Ordinal)
            .ToList();

        Assert.True(silent.Count == 0, $"excused with no reason: {string.Join(", ", silent)}");
    }

    /// <summary>
    /// Checks that the tolerance is the sub-pixel one the plan declared, so a
    /// change to how much drift is allowed is a change someone made on purpose.
    /// </summary>
    [Fact]
    public void TheToleranceIsSubPixel()
    {
        Assert.Equal(0.5, GeometryComparerOptions.Studio.PixelTolerance);
    }
}
