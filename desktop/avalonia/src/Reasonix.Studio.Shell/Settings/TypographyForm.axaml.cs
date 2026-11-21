using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Edits the shell and reading typography through the machine's saved appearance.</summary>
public sealed partial class TypographyForm : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="TypographyForm"/> class.</summary>
    public TypographyForm() => InitializeComponent();

    /// <summary>Maps saved reading sizes from the original four-step scale.</summary>
    /// <param name="stored">Size saved by this or a previous renderer.</param>
    /// <param name="language">Interface language.</param>
    /// <returns>The current reading size.</returns>
    internal static double ReadingSize(double stored, InterfaceLanguage language)
    {
        double[] retired = language == InterfaceLanguage.Zh ? [14.5, 16, 18, 20] : [13.5, 15, 17, 19];
        var index = Array.IndexOf(retired, stored);
        return index >= 0 ? ReadingSteps(language)[index] : stored > 0 ? stored : 14;
    }

    /// <summary>Connects the controls to the normalized settings.</summary>
    /// <param name="look">Initial appearance.</param>
    /// <param name="language">Reader's language.</param>
    /// <param name="api">Active kernel.</param>
    /// <param name="apply">Applies its acknowledgement to the window.</param>
    /// <param name="describe">Localizes refusals.</param>
    /// <param name="token">Window lifetime.</param>
    internal void Open(JsonObject look, InterfaceLanguage language, StudioPageClient api, Action<JsonObject> apply, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        double[] scales = [0.9, 1, 1.15, 1.3];
        var sizes = ReadingSteps(language);
        var labels = new[] { "app.small", "look.standard", "app.large", "app.larger" }.Select(k => Text(k)).ToArray();
        UiScale.ItemsSource = labels;
        ReadSize.ItemsSource = labels;
        Fonts.ItemsSource = FontManager.Current.SystemFonts.Select(f => f.Name).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(f => f).ToArray();
        Fonts.PlaceholderText = Text("look.typeface");
        var face = UiFont;
        UiFont.GotFocus += (_, _) => face = UiFont;
        CodeFont.GotFocus += (_, _) => face = CodeFont;
        Fonts.SelectionChanged += (_, _) =>
        {
            if (Fonts.SelectedItem is string family)
            {
                face.Text = family;
            }
        };
        var syncing = false;
        Sync(true);
        UiScale.SelectionChanged += async (_, _) =>
        {
            if (!syncing && UiScale.SelectedIndex is >= 0 and var i)
            {
                await SaveAsync(null, null, i == 1 ? 0 : scales[i], null).ConfigureAwait(true);
            }
        };
        ReadSize.SelectionChanged += async (_, _) =>
        {
            if (!syncing && ReadSize.SelectedIndex is >= 0 and var i)
            {
                await SaveAsync(null, null, null, sizes[i]).ConfigureAwait(true);
            }
        };
        ApplyFonts.Click += async (_, _) => await FontsAsync().ConfigureAwait(true);
        foreach (var field in new[] { UiFont, CodeFont })
        {
            field.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    await FontsAsync().ConfigureAwait(true);
                }
            };
        }

        Task FontsAsync() => SaveAsync(UiFont.Text?.Trim() ?? string.Empty, CodeFont.Text?.Trim() ?? string.Empty, null, null);

        async Task SaveAsync(string? ui, string? mono, double? scale, double? read)
        {
            if (!IsEnabled)
            {
                return;
            }

            IsEnabled = false;
            Error.IsVisible = false;
            try
            {
                look = await api.SetTypographyAsync(ui, mono, scale, read, token).ConfigureAwait(true);
                apply(look);
                Sync(ui is not null);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    Error.Text = describe(error);
                    Error.IsVisible = true;
                    Sync(false);
                }
            }
            finally
            {
                IsEnabled = true;
            }
        }

        void Sync(bool fonts)
        {
            syncing = true;
            if (fonts)
            {
                UiFont.Text = (string?)look["fontUi"] ?? string.Empty;
                CodeFont.Text = (string?)look["fontMono"] ?? string.Empty;
            }

            UiScale.SelectedIndex = Array.IndexOf(scales, (double?)look["textSize"] is > 0 and var n ? n : 1);
            ReadSize.SelectedIndex = Array.IndexOf(sizes, ReadingSize((double?)look["readSize"] ?? 0, language));
            syncing = false;
        }
    }

    /// <summary>Lists the sizes shown for this language.</summary>
    /// <param name="language">Reader's language.</param>
    /// <returns>Sizes in order.</returns>
    private static double[] ReadingSteps(InterfaceLanguage language) => language == InterfaceLanguage.Zh ? [12, 14, 16.5, 19] : [12, 14, 16, 18];
}
