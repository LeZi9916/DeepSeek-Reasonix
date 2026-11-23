using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Contracts.Refusal;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;
using System;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages;

// A finished call whose middle the page let go of: the row starts folded, opens
// to its two kept ends with a count of what is missing, and can ask the kernel
// for the whole output a page at a time.
//
// The three steps are one control's states rather than three cards, because a
// reader looking for a line in a long result wants the same row to grow, not a
// second row to appear below it.
public sealed partial class SessionPage
{
    /// <summary>
    /// Gets or sets the read that brings a call's whole output back.
    /// </summary>
    /// <remarks>
    /// Null where the window has no way to ask, as in a page driven by a fixture:
    /// the card still says what it let go of, and offers nothing to press.
    /// </remarks>
    public Func<string, Task<string>>? ReadFullOutput
    {
        get; set;
    }

    /// <summary>
    /// Draws the call's own line: its name, and a mark while it runs.
    /// </summary>
    /// <param name="tool">The call.</param>
    /// <returns>The text control the captured conversation recorded for a call.</returns>
    private static Control ToolName(ToolItem tool)
    {
        return new SelectableTextBlock
        {
            Text = tool.Call.Name + (tool.Running ? " …" : string.Empty),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            Opacity = 0.55,
            FontFamily = new FontFamily("Consolas, monospace"),
        };
    }

    /// <summary>
    /// Draws one tool row, folded to its name until the reader asks for more.
    /// </summary>
    /// <param name="tool">The call.</param>
    /// <param name="inActivity">Whether the call is inside an activity group.</param>
    /// <returns>The row.</returns>
    /// <remarks>
    /// A call that kept its whole output draws as the one line it always did. The
    /// folded states exist only where something was dropped, so the ordinary row
    /// keeps the height the captured conversation recorded for it.
    /// </remarks>
    private Control ToolRow(ToolItem tool, bool inActivity)
    {
        var step = FoldChoice(TranscriptPart.Steps, tool.Id) ?? _folding.StartsOpen(
            TranscriptPart.Steps,
            failed: !inActivity && !string.IsNullOrEmpty(tool.Call.Err),
            changed: TranscriptFolding.ChangedFile(tool.Call));
        if (tool.Hidden <= 0)
        {
            return new ToolCallHeader(
                tool,
                Language,
                step,
                FoldChoice(TranscriptPart.Output, tool.Id) ?? _folding.StartsOpen(TranscriptPart.Output),
                open => RememberFold(TranscriptPart.Steps, tool.Id, open),
                open => RememberFold(TranscriptPart.Output, tool.Id, open));
        }

        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(ToolName(tool));

        var hidden = MessageCatalog.Get(
            "chat.n_characters_from_the_middle",
            Language,
            ("n", Format.Count(tool.Hidden, Language)));

        var note = new SelectableTextBlock
        {
            Text = hidden,
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        var output = new SelectableTextBlock
        {
            Text = tool.Call.Output,
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            Opacity = 0.6,
            FontFamily = new FontFamily("Consolas, monospace"),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        var toggle = new Button
        {
            Content = MessageCatalog.Get("common.expand", Language),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
            Padding = new Thickness(8, 2, 8, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var load = new Button
        {
            Content = MessageCatalog.Get("chat.show_full_output", Language),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsVisible = false,
        };

        var opened = step;
        ApplyStep();
        toggle.Click += (_, _) =>
        {
            opened = !opened;
            RememberFold(TranscriptPart.Steps, tool.Id, opened);
            ApplyStep();
        };

        void ApplyStep()
        {
            output.IsVisible = opened;
            note.IsVisible = opened;
            load.IsVisible = opened && ReadFullOutput is not null;
            toggle.Content = MessageCatalog.Get(opened ? "common.collapse" : "common.expand", Language);
        }

        load.Click += async (_, _) => await LoadFullAsync(tool, output, note, load).ConfigureAwait(true);

        body.Children.Add(toggle);
        body.Children.Add(note);
        body.Children.Add(output);
        body.Children.Add(load);
        return body;
    }

    /// <summary>
    /// Reads the call's whole output back and replaces what the row shows.
    /// </summary>
    /// <param name="tool">The call to read.</param>
    /// <param name="output">The text the row shows.</param>
    /// <param name="note">The line saying what was left out.</param>
    /// <param name="load">The button that asked.</param>
    /// <returns>A task that completes once the row is settled.</returns>
    /// <remarks>
    /// A refused read leaves the row exactly as it was, with the reason on the
    /// line that said something was missing: the count is still true, and a reader
    /// who pressed again should find the same button rather than a blank block.
    /// </remarks>
    private async Task LoadFullAsync(ToolItem tool, SelectableTextBlock output, SelectableTextBlock note, Button load)
    {
        if (ReadFullOutput is null)
        {
            return;
        }

        load.IsEnabled = false;
        note.Text = MessageCatalog.Get("chat.fetching", Language);
        try
        {
            var full = await ReadFullOutput(tool.Call.ID).ConfigureAwait(true);
            output.Text = full;
            output.IsVisible = true;
            note.IsVisible = false;
            load.IsVisible = false;
        }
        catch (Exception error)
        {
            // A refusal is said by its code; anything else keeps its own words,
            // and an empty one still says the read failed rather than blanking
            // the line that said something was missing.
            note.Text = error is IRefusal refusal
                ? RefusalText.Say(refusal.Reason, refusal.Status, refusal.Detail, Language)
                : string.IsNullOrWhiteSpace(error.Message)
                    ? MessageCatalog.Get("chat.could_not_fetch_it_try", Language)
                    : error.Message;
            note.IsVisible = true;
            load.Content = MessageCatalog.Get("common.retry", Language);
            load.IsEnabled = true;
        }
    }
}
