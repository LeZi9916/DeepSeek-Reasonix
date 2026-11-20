namespace Reasonix.Studio.Pages.I18n;

/// <summary>
/// The two languages this interface draws in.
/// </summary>
/// <remarks>
/// It is not the language the model answers in: that follows what the person
/// wrote, per message, and the kernel decides it. Reusing this choice for the
/// reply would silence Chinese answers in an English window.
/// </remarks>
public enum InterfaceLanguage
{
    /// <summary>
    /// Simplified Chinese.
    /// </summary>
    Zh,

    /// <summary>
    /// English.
    /// </summary>
    En,
}
