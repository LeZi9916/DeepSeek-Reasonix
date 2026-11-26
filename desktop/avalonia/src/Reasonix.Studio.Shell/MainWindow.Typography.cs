using Avalonia;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Retains the bundled family and CJK fallback after a chosen typeface.</summary>
    /// <param name="primary">The reader's chosen primary family.</param><param name="key">The shared fallback source list.</param>
    /// <returns>The requested family stack.</returns>
    private static FontFamily WithFontFallbacks(string primary, string key) =>
        Application.Current?.TryGetResource(key, null, out var sources) == true && sources is string fallback
            ? FontFamily.Parse(primary + ", " + fallback)
            : FontFamily.Parse(primary);

    /// <summary>Applies interface and code typefaces, UI scale and reading size independently of zoom.</summary>
    /// <param name="look">Normalized preferences.</param>
    /// <param name="language">Language used to interpret historical reading sizes.</param>
    private void SetWindowTypography(JsonObject look, InterfaceLanguage language)
    {
        var scale = (double?)look["textSize"] is > 0 and var value ? value : 1;
        foreach (var size in new[] { 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 22, 25, 26 })
        {
            Resources["StudioType" + size.ToString(CultureInfo.InvariantCulture)] = size * scale;
        }

        var reading = Settings.TypographyForm.ReadingSize((double?)look["readSize"] ?? 0, language);
        Resources["StudioReadingSize"] = reading;
        Resources["StudioReadingHeight"] = reading * 1.72;
        Resources["WorkbenchReadingHeight"] = reading * 1.75;
        Resources["WorkbenchDiffLineHeight"] = 19.2 * scale;
        Resources["WorkbenchDiffNoticeHeight"] = 16.8 * scale;
        Resources["StudioUserHeight"] = reading * 1.6;
        Resources["ComposeModelLineHeight"] = 19.5 * scale;
        Resources["KeyboardLabelLineHeight"] = 19 * scale;
        Resources["KeyboardChordLineHeight"] = 18.6 * scale;
        if ((string?)look["fontMono"] is { Length: > 0 } codeFamily)
        {
            var code = WithFontFallbacks(codeFamily, "StudioMonoFontSources");
            Resources["StudioMonoFamily"] = code;
            Resources["DataPathFamily"] = code;
            Resources["WorkbenchMonoFamily"] = code;
        }
        else
        {
            Resources.Remove("StudioMonoFamily");
            Resources.Remove("DataPathFamily");
            Resources.Remove("WorkbenchMonoFamily");
        }

        if ((string?)look["fontUi"] is { Length: > 0 } family)
        {
            FontFamily = WithFontFallbacks(family, "StudioUiFontSources");
            Resources["ConversationUiFamily"] = FontFamily;
            Resources["DataReadingFamily"] = FontFamily;
            Resources["WorkbenchUiFamily"] = FontFamily;
        }
        else
        {
            ClearValue(FontFamilyProperty);
            Resources.Remove("ConversationUiFamily");
            Resources.Remove("DataReadingFamily");
            Resources.Remove("WorkbenchUiFamily");
        }
    }
}
