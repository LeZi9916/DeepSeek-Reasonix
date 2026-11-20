using Reasonix.Studio.Pages.I18n;
using System.Globalization;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the formatting rules to the reference frontend's own cases.
/// </summary>
/// <remarks>
/// The assertions here are ported one for one from
/// <c>desktop/frontend-next/src/i18n/format.test.ts</c>. They are the contract two
/// windows in two languages are read against, so a case is ported with the input
/// and the expected string intact rather than re-derived from the C# code.
/// </remarks>
public sealed class FormatTests
{
    /// <summary>
    /// The Chinese window's language.
    /// </summary>
    private const InterfaceLanguage Zh = InterfaceLanguage.Zh;

    /// <summary>
    /// The English window's language.
    /// </summary>
    private const InterfaceLanguage En = InterfaceLanguage.En;

    /// <summary>
    /// Checks that whole numbers are grouped in both languages.
    /// </summary>
    [Fact]
    public void CountGroupsThousands()
    {
        Assert.Equal("1,234,567", Format.Count(1234567, Zh));
        Assert.Equal("1,234,567", Format.Count(1234567, En));
    }

    /// <summary>
    /// Checks that small counts are left alone.
    /// </summary>
    [Fact]
    public void CountLeavesSmallNumbersAlone()
    {
        Assert.Equal("0", Format.Count(0, Zh));
        Assert.Equal("999", Format.Count(999, Zh));
    }

    /// <summary>
    /// Checks the token abbreviation's thresholds.
    /// </summary>
    [Fact]
    public void TokensKeepOneDecimalUnderTenThousandAndNoneAbove()
    {
        Assert.Equal("999", Format.Tokens(999, Zh));
        Assert.Equal("1.2k", Format.Tokens(1200, Zh));
        Assert.Equal("10k", Format.Tokens(9999, Zh));
        Assert.Equal("12k", Format.Tokens(12_345, Zh));
        Assert.Equal("123k", Format.Tokens(123_456, Zh));
    }

    /// <summary>
    /// Checks that millions keep one decimal, including a trailing zero.
    /// </summary>
    [Fact]
    public void TokensSwitchToMillions()
    {
        Assert.Equal("1.2M", Format.Tokens(1_200_000, Zh));
        Assert.Equal("12.0M", Format.Tokens(12_000_000, Zh));
    }

    /// <summary>
    /// Checks that the suffix is the same in both languages: the scale a token
    /// count is measured on, not a quantity feeling.
    /// </summary>
    [Fact]
    public void TokensUseTheSameSuffixInBothLanguages()
    {
        Assert.Equal("1.2k", Format.Tokens(1200, En));
    }

    /// <summary>
    /// Checks that seconds carry one place by default.
    /// </summary>
    [Fact]
    public void SecondsSpellOnePlaceByDefault()
    {
        Assert.Equal("1.5s", Format.Seconds(1500, Zh));
        Assert.Equal("0.5s", Format.Seconds(500, Zh));
    }

    /// <summary>
    /// Checks that a caller can ask for another precision.
    /// </summary>
    [Fact]
    public void SecondsTakeThePrecisionTheirCallerNeeds()
    {
        Assert.Equal("2s", Format.Seconds(1500, Zh, 0));
        Assert.Equal("1.23s", Format.Seconds(1234, Zh, 2));
    }

    /// <summary>
    /// Checks the binary steps a size is spelled in.
    /// </summary>
    [Fact]
    public void BytesStepByBinaryUnits()
    {
        Assert.Equal("512 B", Format.Bytes(512, Zh));
        Assert.Equal("2 KB", Format.Bytes(2048, Zh));
        Assert.Equal("293 KB", Format.Bytes(300_000, Zh));
        Assert.Equal("5.0 MB", Format.Bytes(5 << 20, Zh));
        Assert.Equal("9.1 GB", Format.Bytes(9.1 * Math.Pow(2, 30), Zh));
        Assert.Equal("2.0 TB", Format.Bytes(Math.Pow(2, 41), Zh));
    }

    /// <summary>
    /// Checks that a ratio becomes a percentage.
    /// </summary>
    [Fact]
    public void PercentTakesARatio()
    {
        Assert.Equal("50%", Format.Percent(0.5, Zh));
        Assert.Equal("99.82%", Format.Percent(0.9982, Zh, 2));
    }

    /// <summary>
    /// Checks that the yuan is named differently in each language, which is the
    /// whole reason a symbol alone is not enough.
    /// </summary>
    [Fact]
    public void MoneyDistinguishesTheYuanFromTheYen()
    {
        Assert.Equal("¥12.34", Format.Money(12.34m, "CNY", Zh));
        Assert.Equal("CN¥12.34", Format.Money(12.34m, "CNY", En));
    }

    /// <summary>
    /// Checks that the symbol sits where the language puts it.
    /// </summary>
    [Fact]
    public void MoneyPlacesTheSymbolWhereTheLanguagePutsIt()
    {
        Assert.Equal("$1,234.50", Format.Money(1234.5m, "USD", En));
    }

    /// <summary>
    /// Checks that an unknown three-letter code is kept rather than dropped.
    /// </summary>
    [Fact]
    public void MoneyKeepsAnUnknownCode()
    {
        Assert.Equal("XYZ 5.00", Format.Money(5m, "XYZ", Zh));
    }

    /// <summary>
    /// Checks that a bare symbol is passed through as the person spelled it.
    /// </summary>
    [Fact]
    public void MoneyStillRendersALegacySymbol()
    {
        Assert.Equal("¥5.00", Format.Money(5m, "¥", Zh));
    }

    /// <summary>
    /// Checks the contract that keeps a real cost from printing as nothing.
    /// </summary>
    /// <remarks>
    /// The defect behind it: 45 real quotes from a sampled session were complete
    /// and non-zero — $0.000206, $0.00291 — and every one printed as US$0.00
    /// under a fixed two places, so a turn that cost money read as one that cost
    /// nothing.
    /// </remarks>
    [Fact]
    public void MoneyNeverPrintsAKnownAmountAsTheStringATrueZeroPrints()
    {
        var zero = Format.Money(0m, "USD", En);
        foreach (var amount in new[] { 1e-6m, 0.000206m, 0.00291m, 0.0049m, 0.005m, 0.01m, 0.018644m, 1m, 12.28m })
        {
            Assert.NotEqual(zero, Format.Money(amount, "USD", En));
        }

        Assert.Equal(zero, Format.Money(0m, "USD", En));
    }

    /// <summary>
    /// Checks that the same rule holds when no symbol is known for the code.
    /// </summary>
    [Fact]
    public void MoneyHoldsThatForTheFallbackPathToo()
    {
        var zero = Format.Money(0m, "¥", Zh);
        foreach (var amount in new[] { 0.000206m, 0.00291m, 0.0049m })
        {
            Assert.NotEqual(zero, Format.Money(amount, "¥", Zh));
        }
    }

    /// <summary>
    /// Checks that ordinary amounts keep their currency's own precision.
    /// </summary>
    [Fact]
    public void MoneyLeavesAnOrdinaryAmountAtItsPrecision()
    {
        Assert.Equal("$12.28", Format.Money(12.28m, "USD", En));
        Assert.Equal("$1,234.50", Format.Money(1234.5m, "USD", En));
        Assert.Equal("$0.02", Format.Money(0.02m, "USD", En));
    }

    /// <summary>
    /// Checks that the precision widens exactly where the figure would vanish.
    /// </summary>
    [Fact]
    public void MoneyWidensExactlyWhereTheFigureWouldVanish()
    {
        Assert.Equal("$0.01", Format.Money(0.005m, "USD", En));
        Assert.Equal("$0.0049", Format.Money(0.0049m, "USD", En));
    }

    /// <summary>
    /// Checks that a true zero still reads as zero.
    /// </summary>
    [Fact]
    public void MoneyKeepsATrueZeroReadingAsZero()
    {
        Assert.Equal("$0.00", Format.Money(0m, "USD", En));
    }

    /// <summary>
    /// Checks that the plural category is the language's own.
    /// </summary>
    [Fact]
    public void CategoryReportsTheLanguagesOwnForms()
    {
        Assert.Equal(PluralCategory.Other, Format.Category(1, Zh));
        Assert.Equal(PluralCategory.One, Format.Category(1, En));
        Assert.Equal(PluralCategory.Other, Format.Category(2, En));
        Assert.Equal(PluralCategory.Other, Format.Category(0, En));
    }

    /// <summary>
    /// Checks that fixed precision keeps exactly the places asked for.
    /// </summary>
    [Fact]
    public void DecimalsKeepExactlyThePlacesAskedFor()
    {
        Assert.Equal("1.00", Format.Decimals(1, 2, Zh));
        Assert.Equal("1.01", Format.Decimals(1.005, 2, Zh));
    }

    /// <summary>
    /// Checks the compact elapsed spellings.
    /// </summary>
    [Fact]
    public void ElapsedLeavesOutAZeroUnit()
    {
        Assert.Equal("45s", Format.Elapsed(45_000));
        Assert.Equal("3m 20s", Format.Elapsed(200_000));
        Assert.Equal("1h 5s", Format.Elapsed(3_605_000));
        Assert.Equal("2h", Format.Elapsed(7_200_000));
    }

    /// <summary>
    /// Checks that every amount keeps its own currency, joined and never summed.
    /// </summary>
    [Fact]
    public void MoneyListNeverAddsTwoCurrencies()
    {
        var text = Format.MoneyList(new[] { (12.34m, "CNY"), (1.5m, "USD") }, En);
        Assert.Equal("CN¥12.34  ·  $1.50", text);
    }

    /// <summary>
    /// Checks that the clock names today by its time and another day by its date.
    /// </summary>
    [Fact]
    public void ClockNamesTodayByTimeAndAnotherDayByDate()
    {
        var today = new DateTimeOffset(2026, 11, 21, 18, 33, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var earlier = new DateTimeOffset(2026, 9, 17, 18, 33, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        Assert.EndsWith(":33", Format.Clock(today, today, Zh), StringComparison.Ordinal);
        Assert.Contains("/", Format.Clock(earlier, today, En), StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks that each language resolves to its own culture rather than to the
    /// machine's.
    /// </summary>
    [Fact]
    public void TheCultureIsTheLanguagesOwn()
    {
        Assert.Equal("zh-CN", Zh.Culture().Name);
        Assert.Equal("en-US", En.Culture().Name);
        Assert.Equal("zh-CN", Zh.Tag());
        Assert.Equal("en", En.Tag());
    }
}
