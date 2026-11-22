using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;

namespace Reasonix.Studio.Pages.Rail;

/// <summary>Projects the canonical timestamp using the reference sidebar's calendar boundaries.</summary>
public static class SessionAge
{
    /// <summary>Formats one sidebar timestamp.</summary>
    /// <param name="at">Unix milliseconds, or null when unknown.</param>
    /// <param name="now">The current local time.</param>
    /// <param name="language">The requested language.</param>
    /// <returns>The relative time or calendar date.</returns>
    public static string Readout(long? at, DateTimeOffset now, InterfaceLanguage language)
    {
        if (at is null or 0)
        {
            return string.Empty;
        }

        DateTimeOffset date;
        try
        {
            date = DateTimeOffset.FromUnixTimeMilliseconds(at.Value).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }

        var gap = now - date;
        if (gap.TotalMinutes < 1)
        {
            return MessageCatalog.Get("common.just_now", language);
        }

        if (gap.TotalHours < 1)
        {
            return MessageCatalog.Get("common.n_min", language, ("n", Math.Floor(gap.TotalMinutes).ToString(CultureInfo.InvariantCulture)));
        }

        if (now.Date == date.Date)
        {
            return MessageCatalog.Get("common.n_h", language, ("n", Math.Floor(gap.TotalHours).ToString(CultureInfo.InvariantCulture)));
        }

        return now.Date.AddDays(-1) == date.Date ? MessageCatalog.Get("common.yesterday", language) : date.ToString("MM-dd", CultureInfo.InvariantCulture);
    }
}
