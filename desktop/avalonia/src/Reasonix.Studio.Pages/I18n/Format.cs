using System.Globalization;

namespace Reasonix.Studio.Pages.I18n;

/// <summary>
/// The plural category a count falls in, as the reader's language defines it.
/// </summary>
public enum PluralCategory
{
    /// <summary>
    /// The form a language uses for exactly one.
    /// </summary>
    One,

    /// <summary>
    /// Every other form. Chinese has only this one.
    /// </summary>
    Other,
}

/// <summary>
/// Numbers, sizes, durations and money in the reader's language.
/// </summary>
/// <remarks>
/// Ported from the reference frontend's <c>i18n/format.ts</c>, rule for rule,
/// because these are the strings a reader compares across the two windows. The
/// technical suffixes stay as they are — k, M, KB, MB, s read the same in both
/// languages, while a compact-notation rule would write 12345 as 1.2万, a
/// quantity feeling rather than the scale a token count is measured on.
///
/// Every method takes the language rather than reading a mutable global: the
/// language is fixed for a session at launch, and passing it is what keeps two
/// windows in two languages from formatting through one shared culture.
/// </remarks>
public static class Format
{
    /// <summary>
    /// How many bytes make a kilobyte here. Binary steps, because that is what
    /// the kernel counts.
    /// </summary>
    private const double Kilobyte = 1 << 10;

    /// <summary>
    /// How many bytes make a megabyte.
    /// </summary>
    private const double Megabyte = 1 << 20;

    /// <summary>
    /// How many bytes make a gigabyte.
    /// </summary>
    private const double Gigabyte = 1u << 30;

    /// <summary>
    /// How many bytes make a terabyte.
    /// </summary>
    private const double Terabyte = 1UL << 40;

    /// <summary>
    /// Formats a whole number with its thousands grouped.
    /// </summary>
    /// <param name="value">The number to format.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <returns>For example <c>1,234,567</c>.</returns>
    public static string Count(long value, InterfaceLanguage language)
    {
        return value.ToString("N0", language.Culture());
    }

    /// <summary>
    /// Formats a number to exactly the places asked for.
    /// </summary>
    /// <param name="value">The number to format.</param>
    /// <param name="digits">How many decimal places to keep.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <returns>For example <c>1.00</c>.</returns>
    /// <remarks>
    /// The value is rounded as a decimal rather than as a binary fraction: the
    /// browser rounds the number it was given, and 1.005 is 1.01 to a reader.
    /// Rounding the double first would answer 1.00 for the same input.
    /// </remarks>
    public static string Decimals(double value, int digits, InterfaceLanguage language)
    {
        return AsDecimal(value).ToString("F" + digits.ToString(CultureInfo.InvariantCulture), language.Culture());
    }

    /// <summary>
    /// Formats a number to at most the places asked for, padding none.
    /// </summary>
    /// <param name="value">The number to format.</param>
    /// <param name="digits">The most decimal places to keep.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <returns>For example <c>1.5</c> from 1.50.</returns>
    public static string Plain(double value, int digits, InterfaceLanguage language)
    {
        // "N" followed by a decimal point is not a standard pattern: the runtime
        // reads it as a custom format and prints the N as a literal. The grouped
        // custom pattern is the one that means "thousands, at most n places".
        var pattern = digits <= 0 ? "#,##0" : "#,##0." + new string('#', digits);
        return AsDecimal(value).ToString(pattern, language.Culture());
    }

    /// <summary>
    /// Abbreviates a count of tokens or characters.
    /// </summary>
    /// <param name="value">The count.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <returns>For example <c>1.2k</c>, <c>12k</c> or <c>1.2M</c>.</returns>
    /// <remarks>
    /// The decimal is dropped at five figures so the field's width stays put as
    /// a turn grows; under a thousand the count is spelled in full.
    /// </remarks>
    public static string Tokens(long value, InterfaceLanguage language)
    {
        if (value < 1000)
        {
            return Count(value, language);
        }

        if (value < 1_000_000)
        {
            var thousands = value / 1000d;
            return value < 10_000
                ? Plain(thousands, 1, language) + "k"
                : Plain(thousands, 0, language) + "k";
        }

        return Decimals(value / 1_000_000d, 1, language) + "M";
    }

    /// <summary>Formats elapsed whole seconds with the job card's unit labels.</summary>
    /// <param name="milliseconds">The job duration.</param>
    /// <param name="language">The reader's language.</param>
    /// <returns>The localized elapsed time.</returns>
    public static string JobElapsed(long milliseconds, InterfaceLanguage language)
    {
        var seconds = milliseconds / 1000;
        var minutes = seconds / 60;
        if (minutes == 0)
        {
            return MessageCatalog.Get("common.n_s", language, ("n", Format.Count(seconds, language)));
        }

        if (minutes < 60)
        {
            var remainder = seconds % 60;
            return remainder == 0
                ? MessageCatalog.Get("panels.m_min", language, ("m", Format.Count(minutes, language)))
                : MessageCatalog.Get("common.m_m_s_s", language, ("m", Format.Count(minutes, language)), ("s", remainder.ToString("00", CultureInfo.InvariantCulture)));
        }

        var hours = Format.Count(minutes / 60, language);
        return minutes % 60 == 0
            ? MessageCatalog.Get("panels.h_h", language, ("h", hours))
            : MessageCatalog.Get("common.h_h_m_m", language, ("h", hours), ("m", Format.Count(minutes % 60, language)));
    }

    /// <summary>
    /// Spells a run time compactly: seconds under a minute, then hours, minutes
    /// and seconds with any zero unit left out.
    /// </summary>
    /// <param name="milliseconds">The duration.</param>
    /// <returns>For example <c>45s</c>, <c>3m 20s</c> or <c>1h 5s</c>.</returns>
    public static string Elapsed(double milliseconds)
    {
        var total = (long)Math.Round(milliseconds / 1000d, MidpointRounding.AwayFromZero);
        if (total < 60)
        {
            return total.ToString(CultureInfo.InvariantCulture) + "s";
        }

        var hours = total / 3600;
        var remainder = total % 3600;
        var minutes = remainder / 60;
        var seconds = total % 60;

        var parts = new List<string>(3);
        if (hours > 0)
        {
            parts.Add(hours.ToString(CultureInfo.InvariantCulture) + "h");
        }

        if (minutes > 0)
        {
            parts.Add(minutes.ToString(CultureInfo.InvariantCulture) + "m");
        }

        if (seconds > 0)
        {
            parts.Add(seconds.ToString(CultureInfo.InvariantCulture) + "s");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Names the moment something began: the time of day for today, with the
    /// date before it for any other day, on a 24-hour clock.
    /// </summary>
    /// <param name="at">The moment, in unix milliseconds.</param>
    /// <param name="now">What "today" means for this reading, in unix milliseconds.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <returns>For example <c>18:33</c> or <c>9/17 18:33</c>.</returns>
    public static string Clock(long at, long now, InterfaceLanguage language)
    {
        var moment = DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime();
        var today = DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime();
        var culture = language.Culture();

        var pattern = language == InterfaceLanguage.Zh ? "H:mm" : "HH:mm";
        var time = moment.ToString(pattern, culture);
        return moment.Date == today.Date
            ? time
            : moment.ToString("M/d", culture) + " " + time;
    }

    /// <summary>
    /// Spells a millisecond duration in seconds.
    /// </summary>
    /// <param name="milliseconds">The duration.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <param name="digits">How many decimal places to keep.</param>
    /// <returns>For example <c>1.5s</c>.</returns>
    public static string Seconds(double milliseconds, InterfaceLanguage language, int digits = 1)
    {
        return Decimals(milliseconds / 1000d, digits, language) + "s";
    }

    /// <summary>
    /// Spells a size in binary steps.
    /// </summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <returns>For example <c>293 KB</c>, <c>5.0 MB</c> or <c>9.1 GB</c>.</returns>
    /// <remarks>
    /// It runs to terabytes because storage roots and the volumes under them are
    /// measured there; a ceiling at megabytes is how a 9 GB total once read as
    /// "9318.4 MB".
    /// </remarks>
    public static string Bytes(double bytes, InterfaceLanguage language)
    {
        if (bytes >= Terabyte)
        {
            return Decimals(bytes / Terabyte, 1, language) + " TB";
        }

        if (bytes >= Gigabyte)
        {
            return Decimals(bytes / Gigabyte, 1, language) + " GB";
        }

        if (bytes >= Megabyte)
        {
            return Decimals(bytes / Megabyte, 1, language) + " MB";
        }

        if (bytes >= Kilobyte)
        {
            return Plain(bytes / Kilobyte, 0, language) + " KB";
        }

        return Count((long)Math.Round(bytes, MidpointRounding.AwayFromZero), language) + " B";
    }

    /// <summary>
    /// Spells a ratio as a percentage.
    /// </summary>
    /// <param name="ratio">The ratio, not the percentage: 0.5 is fifty percent.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <param name="digits">How many decimal places to keep.</param>
    /// <returns>For example <c>50%</c> or <c>99.82%</c>.</returns>
    public static string Percent(double ratio, InterfaceLanguage language, int digits = 0)
    {
        return Decimals(ratio * 100d, digits, language) + "%";
    }

    /// <summary>
    /// Formats an amount in its own currency.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <param name="code">The ISO code the kernel sent, or a legacy symbol.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <param name="digits">The currency's own precision, normally two.</param>
    /// <returns>For example <c>¥12.34</c>, <c>CN¥12.34</c> or <c>$1,234.50</c>.</returns>
    /// <remarks>
    /// A symbol alone cannot say which yuan or yen it is, so the two languages
    /// get different spellings for the same code: ¥ in Chinese and CN¥ in
    /// English are one amount named as each reader would read it.
    ///
    /// An amount smaller than half the last place a fixed precision can show
    /// would print as the same string a true zero prints, and a turn that cost
    /// money must not read as one that cost nothing; only those are widened to
    /// two significant digits.
    /// </remarks>
    public static string Money(decimal amount, string code, InterfaceLanguage language, int digits = 2)
    {
        var iso = (code ?? string.Empty).Trim().ToUpperInvariant();
        var number = VanishWide(amount, digits) is { } significant
            ? amount.ToString("G" + significant.ToString(CultureInfo.InvariantCulture), language.Culture())
            : amount.ToString("N" + digits.ToString(CultureInfo.InvariantCulture), language.Culture());

        if (iso.Length == 3 && iso.All(char.IsAsciiLetterUpper))
        {
            return SymbolOf(iso, language) + number;
        }

        // Not an ISO code: it is a symbol the person's config spelled, and the
        // code itself is the only honest prefix when nothing knows it.
        return (code ?? string.Empty).Trim() + number;
    }

    /// <summary>
    /// Prints one amount per currency, side by side.
    /// </summary>
    /// <param name="amounts">One amount and its currency per entry.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <returns>The amounts joined by the separator the reference frontend uses.</returns>
    /// <remarks>
    /// It never adds them: two currencies are two units, and a single sum would
    /// need a rate nobody quoted.
    /// </remarks>
    public static string MoneyList(IEnumerable<(decimal Amount, string Currency)> amounts, InterfaceLanguage language)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        return string.Join("  ·  ", amounts.Select(entry => Money(entry.Amount, entry.Currency, language)));
    }

    /// <summary>
    /// Reads the plural category a count falls in.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <returns>The category that language's own grammar defines.</returns>
    public static PluralCategory Category(double count, InterfaceLanguage language)
    {
        // Chinese has one written form for every count; English separates one.
        // Asking the language is what keeps a catalogue that later grows a
        // four-form language from needing this rewritten.
        return language == InterfaceLanguage.En && count == 1 ? PluralCategory.One : PluralCategory.Other;
    }

    /// <summary>
    /// Reads how many significant digits an amount needs to stay non-zero.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <param name="digits">The precision it would normally be printed at.</param>
    /// <returns>2 when the amount would vanish, otherwise null.</returns>
    private static int? VanishWide(decimal amount, int digits)
    {
        if (amount == 0m)
        {
            return null;
        }

        var threshold = 0.5m * (decimal)Math.Pow(10, -digits);
        return Math.Abs(amount) < threshold ? 2 : null;
    }

    /// <summary>
    /// Reads the symbol a currency code is written with in one language.
    /// </summary>
    /// <param name="iso">The three-letter code.</param>
    /// <param name="language">The language the reader is using.</param>
    /// <returns>The code's symbol, or the code followed by a space when it has none here.</returns>
    /// <remarks>
    /// The pairs are spelled out rather than read from a culture's currency data
    /// because the distinction that matters is between two currencies sharing a
    /// symbol — the yuan and the yen — and a culture's default table cannot make
    /// that distinction while keeping the same glyph.
    /// </remarks>
    private static string SymbolOf(string iso, InterfaceLanguage language)
    {
        return (iso, language) switch
        {
            ("CNY", InterfaceLanguage.Zh) => "¥",
            ("CNY", _) => "CN¥",
            ("JPY", InterfaceLanguage.Zh) => "JP¥",
            ("JPY", _) => "¥",
            ("USD", InterfaceLanguage.Zh) => "US$",
            ("USD", _) => "$",
            ("EUR", _) => "€",
            ("GBP", _) => "£",
            ("KRW", _) => "₩",
            ("HKD", InterfaceLanguage.Zh) => "HK$",
            ("HKD", _) => "HK$",
            _ => iso + " ",
        };
    }

    /// <summary>
    /// Reads a number as the decimal a person would have written.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>Its nearest decimal, so rounding happens on the printed digits.</returns>
    private static decimal AsDecimal(double value)
    {
        // Outside decimal's range the nearest decimal is meaningless, and the
        // figures that reach here (durations, sizes) are nowhere near it.
        return (decimal)value;
    }
}
