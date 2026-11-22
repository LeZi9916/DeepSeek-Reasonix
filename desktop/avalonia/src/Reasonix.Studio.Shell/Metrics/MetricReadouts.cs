using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Linq;

namespace Reasonix.Studio.Shell.Metrics;

/// <summary>Shared formatting for measured context, cache ratios and covered costs.</summary>
internal static class MetricReadouts
{
    /// <summary>Excludes tokens whose cache split was not reported.</summary>
    /// <param name="hit">Cached tokens.</param><param name="miss">Uncached and unsplit tokens.</param>
    /// <param name="unknown">Unsplit tokens.</param><returns>The supported ratio, or unknown.</returns>
    internal static double? CacheRate(long hit, long miss, long unknown = 0) => hit + miss - unknown > 0 ? Math.Clamp((double)hit / (hit + miss - unknown), 0, 1) : null;

    /// <summary>Formats a compact token count.</summary>
    /// <param name="value">Measured count.</param><returns>The compact count.</returns>
    internal static string Tokens(long value) => value >= 1_000_000 ? (value / 1_000_000d).ToString("0.0", CultureInfo.CurrentCulture) + "M" : value >= 1000 ? (Math.Floor((value / 100d) + 0.5) / 10).ToString("0.#", CultureInfo.CurrentCulture) + "k" : value.ToString(CultureInfo.CurrentCulture);

    /// <summary>Formats the ledger's model work time using the interface clock labels.</summary>
    /// <param name="milliseconds">Canonical model duration.</param><param name="language">Interface language.</param>
    /// <returns>The supported duration.</returns>
    internal static string WorkTime(long milliseconds, InterfaceLanguage language)
    {
        if (milliseconds <= 0)
        {
            return "—";
        }

        var seconds = (long)Math.Floor((milliseconds / 1000d) + 0.5);
        string Number(long value) => value.ToString(CultureInfo.CurrentCulture);
        var minutes = seconds % 3600;
        return seconds >= 3600
            ? MessageCatalog.Get("common.h_h_m_m", language, ("h", Number(seconds / 3600)), ("m", Number(minutes / 60)))
            : seconds >= 60
                ? MessageCatalog.Get("common.m_m_s_s", language, ("m", Number(seconds / 60)), ("s", Number(seconds % 60)))
                : MessageCatalog.Get("session.s_s", language, ("s", Number(seconds)));
    }

    /// <summary>Formats a known cache ratio without rounding up.</summary>
    /// <param name="ratio">Known fraction.</param><returns>The percentage or unknown mark.</returns>
    internal static string Percent(double? ratio) => ratio is { } value ? Math.Floor(value * 100).ToString(CultureInfo.CurrentCulture) + "%" : "—";

    /// <summary>Honors the host's pricing coverage and selected currency.</summary>
    /// <param name="quote">Canonical quote.</param><param name="hidden">Whether displayed amounts are masked.</param>
    /// <param name="language">Interface language.</param><returns>The supported amount reading.</returns>
    internal static string Cost(CostQuote? quote, bool hidden, InterfaceLanguage language)
    {
        var coverage = quote?.Coverage ?? (quote?.CostComplete == true ? "complete" : "none");
        if (coverage is not ("complete" or "partial"))
        {
            return "—";
        }

        var amounts = quote?.Selected is { } selected ? new[] { selected } : quote?.OriginalTotals is { Count: > 0 } totals ? totals.ToArray() : quote?.Original is { } original ? new[] { original } : [];
        if (amounts.Length == 0 || amounts.Any(money => !decimal.TryParse(money.Amount, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            return "—";
        }

        return (coverage == "partial" ? "≥ " : string.Empty) + (hidden ? "•••" : Format.MoneyList(amounts.Select(money => (decimal.Parse(money.Amount, NumberStyles.Float, CultureInfo.InvariantCulture), money.Currency)), language));
    }
}
