using Reasonix.Studio.Pages.I18n;

namespace Reasonix.Studio.Pages.Composer;

/// <summary>
/// How much of the context window the last request took, as the status line says
/// it.
/// </summary>
/// <remarks>
/// The pair is what makes the number mean something: a count of tokens alone
/// says nothing about how close the turn is to the window, and a ratio alone
/// hides how large the window is. Both are abbreviated the way every other token
/// count in this interface is.
/// </remarks>
public static class ContextMeter
{
    /// <summary>
    /// Reads the meter for one session.
    /// </summary>
    /// <param name="used">The tokens the last request used.</param>
    /// <param name="window">The context window the kernel measured, or 0 when it has not.</param>
    /// <param name="language">The language to format the numbers in.</param>
    /// <returns>The readout, or an em dash before anything has been counted.</returns>
    public static string Readout(long used, long window, InterfaceLanguage language)
    {
        if (used <= 0 && window <= 0)
        {
            return "—";
        }

        var taken = Format.Tokens(used, language);
        return window > 0 ? taken + " / " + Format.Tokens(window, language) : taken;
    }
}
