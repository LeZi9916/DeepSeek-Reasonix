using Reasonix.Studio.Pages.I18n;
using System;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the language rule to the reference frontend's own cases: an explicit
/// choice wins, and everything else follows the machine.
/// </summary>
public sealed class LanguageResolverTests
{
    /// <summary>
    /// Checks that the two explicit choices are taken as written, whatever the
    /// machine is set to.
    /// </summary>
    [Fact]
    public void AnExplicitChoiceWins()
    {
        Assert.Equal(InterfaceLanguage.Zh, LanguageResolver.Resolve("zh", new[] { "en-US" }));
        Assert.Equal(InterfaceLanguage.En, LanguageResolver.Resolve("en", new[] { "zh-CN" }));
    }

    /// <summary>
    /// Checks that the choice is read case-insensitively and with its padding
    /// ignored, because it arrives from a config file a person typed.
    /// </summary>
    [Fact]
    public void AChoiceIsReadLoosely()
    {
        Assert.Equal(InterfaceLanguage.Zh, LanguageResolver.Resolve("  ZH  ", new[] { "en-US" }));
        Assert.Equal(InterfaceLanguage.En, LanguageResolver.Resolve("En", new[] { "zh-CN" }));
    }

    /// <summary>
    /// Checks that <c>auto</c>, an empty preference and no preference at all all
    /// mean the same thing: follow the machine.
    /// </summary>
    [Fact]
    public void AutoEmptyAndNullAllFollowTheMachine()
    {
        var chinese = new[] { "zh-CN" };

        Assert.Equal(InterfaceLanguage.Zh, LanguageResolver.Resolve("auto", chinese));
        Assert.Equal(InterfaceLanguage.Zh, LanguageResolver.Resolve(string.Empty, chinese));
        Assert.Equal(InterfaceLanguage.Zh, LanguageResolver.Resolve(null, chinese));
    }

    /// <summary>
    /// Checks that every kind of Chinese the machine may report lands on the one
    /// Chinese this interface draws in.
    /// </summary>
    [Fact]
    public void EveryChineseTagLandsOnChinese()
    {
        foreach (var tag in new[] { "zh", "zh-CN", "zh-Hans", "zh-Hant-TW", "zh-HK", "zh-SG", "yue", "yue-Hant-HK", "cmn-Hans-CN" })
        {
            Assert.True(LanguageResolver.IsChinese(tag), tag);
        }
    }

    /// <summary>
    /// Checks that a machine set to something else reads in English, which is the
    /// fallback rather than a claim that the machine is English.
    /// </summary>
    [Fact]
    public void AnythingElseLandsOnEnglish()
    {
        foreach (var tag in new[] { "en", "en-GB", "fr-FR", "ja-JP", string.Empty, "  " })
        {
            Assert.False(LanguageResolver.IsChinese(tag), tag);
        }

        Assert.Equal(InterfaceLanguage.En, LanguageResolver.Resolve("auto", new[] { "fr-FR", "zh-CN" }));
    }

    /// <summary>
    /// Checks that only the first preference counts: a machine set to French with
    /// Chinese second is a French machine.
    /// </summary>
    [Fact]
    public void OnlyTheFirstPreferenceCounts()
    {
        Assert.Equal(InterfaceLanguage.En, LanguageResolver.FromMachine(new[] { "fr-FR", "zh-CN", "ja-JP" }));
        Assert.Equal(InterfaceLanguage.Zh, LanguageResolver.FromMachine(new[] { "zh-CN", "en-US" }));
    }

    /// <summary>
    /// Checks that a machine that reported no language at all reads in English.
    /// </summary>
    [Fact]
    public void NoReportedLanguageReadsInEnglish()
    {
        Assert.Equal(InterfaceLanguage.En, LanguageResolver.FromMachine(Array.Empty<string>()));
    }

    /// <summary>
    /// Checks that a null list is refused rather than read as empty, which would
    /// quietly answer English for a caller that meant to pass something.
    /// </summary>
    [Fact]
    public void AMissingMachineListIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => LanguageResolver.Resolve("auto", null!));
        Assert.Throws<ArgumentNullException>(() => LanguageResolver.FromMachine(null!));
    }
}
