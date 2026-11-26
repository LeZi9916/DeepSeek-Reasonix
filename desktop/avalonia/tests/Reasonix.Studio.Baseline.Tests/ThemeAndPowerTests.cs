using Reasonix.Studio.Shell;
using Reasonix.Studio.Shell.Host;
using System;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Checks local theme and sleep transitions against observed desktop conditions.</summary>
public sealed class ThemeAndPowerTests
{
    /// <summary>Applies daily boundaries including intervals that span midnight.</summary>
    /// <param name="start">Dark mode hour.</param><param name="end">Light mode hour.</param>
    /// <param name="hour">The observed local hour.</param><param name="dark">Whether dark mode is expected.</param>
    [Theory]
    [InlineData(20, 7, 20, true)]
    [InlineData(20, 7, 6, true)]
    [InlineData(20, 7, 7, false)]
    [InlineData(8, 18, 12, true)]
    [InlineData(8, 18, 20, false)]
    [InlineData(8, 8, 8, false)]
    public void ScheduleReadsLocalTimeIncludingAnOvernightInterval(int start, int end, int hour, bool dark) =>
        Assert.Equal(dark, MainWindow.IsScheduledDark(new TimeOnly(start, 0), new TimeOnly(end, 0), new TimeOnly(hour, 0)));

    /// <summary>Retains inhibition across transient failures before releasing it.</summary>
    [Fact]
    public void PowerLeaseSeparatesIdleAndUnreachablePanes()
    {
        var held = 0;
        using var guard = new AppSleepLease(() => new Lease(() => held--));
        guard.Observe(0, 1);
        Assert.Equal(0, held);
        held++;
        guard.Observe(1, 1);
        guard.Observe(0, 0);
        guard.Observe(0, 0);
        guard.Observe(0, 1);
        Assert.Equal(1, held);
        guard.Observe(0, 0);
        Assert.Equal(0, held);

        held++;
        guard.Observe(1, 0);
        for (var i = 0; i < 11; i++)
        {
            guard.Observe(0, 1);
        }

        Assert.Equal(1, held);
        guard.Observe(0, 1);
        Assert.Equal(0, held);
    }

    /// <summary>Records release of a test inhibition.</summary>
    /// <param name="release">Callback invoked when disposed.</param>
    private sealed class Lease(Action release) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose() => release();
    }
}
