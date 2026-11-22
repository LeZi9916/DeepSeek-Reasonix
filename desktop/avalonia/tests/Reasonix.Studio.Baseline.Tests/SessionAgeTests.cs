using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using System;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Holds session age wording at minute and local-calendar boundaries.</summary>
public sealed class SessionAgeTests
{
    /// <summary>Relative times give way to yesterday and calendar dates at midnight.</summary>
    [Fact]
    public void CalendarBoundariesMatchTheReference()
    {
        var now = new DateTimeOffset(new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Local));
        string Age(DateTimeOffset date) => SessionAge.Readout(date.ToUnixTimeMilliseconds(), now, InterfaceLanguage.Zh);
        Assert.Equal("刚刚", Age(now.AddSeconds(-30)));
        Assert.Equal("10 分钟", Age(now.AddMinutes(-10)));
        Assert.Equal("2 小时", Age(now.AddHours(-2)));
        Assert.Equal("昨天", Age(now.AddDays(-1)));
        Assert.Equal("11-15", Age(now.AddDays(-7)));
        Assert.Equal(string.Empty, SessionAge.Readout(null, now, InterfaceLanguage.En));
        Assert.Equal(string.Empty, SessionAge.Readout(long.MaxValue, now, InterfaceLanguage.En));
    }
}
