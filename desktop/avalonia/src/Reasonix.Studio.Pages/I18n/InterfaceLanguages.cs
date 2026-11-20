using System.Globalization;

namespace Reasonix.Studio.Pages.I18n;

/// <summary>
/// Reads an <see cref="InterfaceLanguage"/> as the culture its numbers are
/// formatted in.
/// </summary>
public static class InterfaceLanguages
{
    /// <summary>
    /// Gets the culture a language's numbers, dates and currency are written in.
    /// </summary>
    /// <param name="language">The language to read.</param>
    /// <returns>The culture carrying that language's separators and symbols.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a language this does not know.</exception>
    public static CultureInfo Culture(this InterfaceLanguage language)
    {
        return language switch
        {
            InterfaceLanguage.Zh => CultureInfo.GetCultureInfo("zh-CN"),
            InterfaceLanguage.En => CultureInfo.GetCultureInfo("en-US"),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "no culture for this language"),
        };
    }

    /// <summary>
    /// Reads a language as the tag an HTML document or an asset names.
    /// </summary>
    /// <param name="language">The language to read.</param>
    /// <returns>"zh-CN" or "en".</returns>
    public static string Tag(this InterfaceLanguage language)
    {
        return language == InterfaceLanguage.Zh ? "zh-CN" : "en";
    }
}
