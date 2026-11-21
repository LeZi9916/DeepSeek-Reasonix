using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
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
        Resources["StudioReadingHeight"] = reading * (24d / 14d);
        Resources["StudioMonoFamily"] = FontFamily.Parse((string?)look["fontMono"] is { Length: > 0 } mono ? mono : "monospace");
        if ((string?)look["fontMono"] is { Length: > 0 } codeFamily)
        {
            Resources["DataPathFamily"] = FontFamily.Parse(codeFamily);
        }
        else
        {
            Resources.Remove("DataPathFamily");
        }

        if ((string?)look["fontUi"] is { Length: > 0 } family)
        {
            FontFamily = FontFamily.Parse(family);
            Resources["DataReadingFamily"] = FontFamily;
        }
        else
        {
            ClearValue(FontFamilyProperty);
            Resources.Remove("DataReadingFamily");
        }
    }
}
