using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>References riding with the next turn, separate from the typed line.</summary>
    private readonly List<AttachmentChip> _attachments = [];

    /// <summary>Adds host-issued references as chips instead of text tokens.</summary>
    /// <param name="references">References returned by the drop or paste route.</param>
    private void AddAttachments(IEnumerable<DroppedReference> references)
    {
        foreach (var reference in references)
        {
            var path = reference.Path ?? string.Empty;
            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(reference.Ref))
            {
                if (string.IsNullOrEmpty(reference.Error) || path.Length == 0 || _attachments.Any(chip => chip.SourcePath == path && chip.Error.Length > 0))
                {
                    continue;
                }

                _attachments.Add(new AttachmentChip(string.Empty, name.Length == 0 ? MessageCatalog.Get("common.files", Language) : name, path, IsPicture(path), reference.Error));
                continue;
            }

            var token = reference.Ref.Trim();
            if (_attachments.Any(chip => chip.Token == token))
            {
                continue;
            }

            _attachments.RemoveAll(chip => chip.SourcePath == path && chip.Error.Length > 0);
            _attachments.Add(new AttachmentChip(token, name.Length == 0 ? token : name, path, IsPicture(path)));
        }

        RebuildAttachments();
    }

    /// <summary>Adds one image reference returned by the paste route.</summary>
    /// <param name="reference">The host-issued token.</param>
    private void AddAttachmentReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || _attachments.Any(chip => chip.Token == reference))
        {
            return;
        }

        var name = Path.GetFileName(reference);
        _attachments.Add(new AttachmentChip(reference, name.Length == 0 ? reference : name, reference, true));
        RebuildAttachments();
    }

    /// <summary>Adds selected file text as a quote chip instead of inline Markdown.</summary>
    /// <param name="text">Selected text.</param>
    /// <param name="source">Workspace path the selection came from.</param>
    private void AddQuoteChip(string text, string source)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var name = Path.GetFileName(source);
        var label = name.Length == 0
            ? MessageCatalog.Get("composer.quoting_a_reply", Language)
            : MessageCatalog.Get("composer.quote_name", Language, ("name", name));
        _attachments.Add(new AttachmentChip(string.Empty, label, source, false, QuoteBody: text, QuoteSource: source));
        RebuildAttachments();
    }

    /// <summary>Holds a long paste beside the prompt instead of letting it become the prompt.</summary>
    /// <param name="text">Clipboard text.</param>
    private void AddPasteChip(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _attachments.Add(new AttachmentChip(string.Empty, MessageCatalog.Get("composer.pasted_text", Language), string.Empty, false, PasteBody: text));
        RebuildAttachments();
    }

    /// <summary>Recognizes a paste long enough to hold out of the box.</summary>
    /// <param name="text">Clipboard text.</param>
    /// <returns>Whether the paste should become a chip.</returns>
    private bool IsLongPaste(string text) => text.Length > 4000 || text.Count(character => character == '\n') > 80;

    /// <summary>Builds the reference chips above the prompt.</summary>
    private void RebuildAttachments()
    {
        AttachmentStrip.Children.Clear();
        foreach (var attachment in _attachments.ToArray())
        {
            var chip = new Border { Classes = { "attachment-chip" } };
            if (attachment.SourcePath.Length > 0 || attachment.Token.Length > 0)
            {
                ToolTip.SetTip(chip, attachment.SourcePath.Length > 0 ? attachment.SourcePath : attachment.Token);
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(AttachmentGlyph(attachment.Image, attachment.QuoteBody.Length > 0));
            var name = new TextBlock { Text = attachment.Name, Classes = { "attachment-name" } };
            row.Children.Add(name);
            if (attachment.PasteBody.Length > 0)
            {
                var lines = attachment.PasteBody.Count(character => character == '\n') + 1;
                var restore = new Button
                {
                    Content = MessageCatalog.Get("composer.n_lines_put_it_back", Language, ("n", lines.ToString(System.Globalization.CultureInfo.CurrentCulture))),
                    Classes = { "attachment-undo" },
                };
                restore.Click += (_, _) =>
                {
                    var body = attachment.PasteBody;
                    _attachments.Remove(attachment);
                    RebuildAttachments();
                    InsertPastedText(body);
                };
                row.Children.Add(restore);
            }

            if (attachment.Error.Length > 0)
            {
                var reason = new TextBlock { Text = attachment.Error, Classes = { "attachment-error" } };
                row.Children.Add(reason);
                var retry = new Button { Content = MessageCatalog.Get("composer.could_not_add_retry", Language), Classes = { "attachment-retry" } };
                retry.Click += async (_, _) => await RetryAttachmentAsync(attachment).ConfigureAwait(true);
                row.Children.Add(retry);
            }

            var remove = new Button { Content = "×", Classes = { "attachment-remove" } };
            ToolTip.SetTip(remove, MessageCatalog.Get("common.remove", Language));
            remove.Click += (_, _) =>
            {
                _attachments.Remove(attachment);
                RebuildAttachments();
            };
            row.Children.Add(remove);
            chip.Child = row;
            AttachmentStrip.Children.Add(chip);
        }

        AttachmentArea.IsVisible = _attachments.Count > 0;
        UpdateAttachmentWarning();
    }

    /// <summary>Retries one refused drop without disturbing the others.</summary>
    /// <param name="attachment">The failed chip.</param>
    /// <returns>The retry.</returns>
    private async Task RetryAttachmentAsync(AttachmentChip attachment)
    {
        if (_engine is null || attachment.SourcePath.Length == 0 || _closing.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var refs = await _engine.Pages.DropAsync([attachment.SourcePath], _closing.Token).ConfigureAwait(true);
            if (refs.Any(reference => !string.IsNullOrWhiteSpace(reference.Ref)))
            {
                _attachments.Remove(attachment);
                AddAttachments(refs);
                return;
            }

            var error = refs.Select(reference => reference.Error).FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? MessageCatalog.Get("composer.could_not_add_retry", Language);
            var at = _attachments.IndexOf(attachment);
            if (at >= 0)
            {
                _attachments[at] = attachment with
                {
                    Error = error,
                };
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            var at = _attachments.IndexOf(attachment);
            if (at >= 0)
            {
                _attachments[at] = attachment with
                {
                    Error = DescribeError(error),
                };
            }
        }

        RebuildAttachments();
    }

    /// <summary>Warns when a picture rides with a model that does not read images.</summary>
    private void UpdateAttachmentWarning()
    {
        var pictures = _attachments.Any(chip => chip.Image && chip.Error.Length == 0);
        AttachmentWarning.IsVisible = pictures && !_composerStatus.Vision;
        AttachmentWarning.Text = AttachmentWarning.IsVisible
            ? MessageCatalog.Get("composer.this_model_does_not_read", Language)
            : string.Empty;
    }

    /// <summary>Serializes attachments ahead of the typed line for the model.</summary>
    /// <param name="line">The visible prompt.</param>
    /// <returns>The submit text.</returns>
    private string ComposeSubmitText(string line)
    {
        var parts = _attachments
            .Where(chip => chip.Error.Length == 0)
            .Select(chip => chip.QuoteBody.Length > 0 ? QuoteBlock(chip) : chip.PasteBody.Length > 0 ? chip.PasteBody : chip.Token)
            .Where(part => part.Length > 0)
            .ToArray();
        return parts.Length == 0 ? line : string.Join("\n\n", parts) + "\n\n" + line;
    }

    /// <summary>Puts a held paste back at the composer caret.</summary>
    /// <param name="text">The held text.</param>
    private void InsertPastedText(string text)
    {
        var start = Math.Min(Composer.SelectionStart, Composer.SelectionEnd);
        Composer.SelectionStart = start;
        Composer.SelectionEnd = start;
        Composer.SelectedText = text;
        Composer.Focus();
    }

    /// <summary>Builds the provider-visible quote block for a quote chip.</summary>
    /// <param name="chip">The quote chip.</param>
    /// <returns>The quoted block.</returns>
    private string QuoteBlock(AttachmentChip chip)
    {
        var where = chip.QuoteSource.Replace('\\', '/');
        var head = where.Length == 0
            ? "[Quoting an earlier assistant reply in this conversation.]"
            : $"[Quoting text selected in the workspace file {where}.]";
        return string.Join("\n", new[] { head }.Concat(chip.QuoteBody.Split('\n').Select(line => "> " + line.TrimEnd('\r'))));
    }

    /// <summary>Drops attachments with the draft that owned them.</summary>
    private void ClearAttachments()
    {
        _attachments.Clear();
        RebuildAttachments();
    }

    /// <summary>Builds the small file, picture or quote mark beside a chip name.</summary>
    /// <param name="picture">Whether the reference is a picture.</param>
    /// <param name="quote">Whether the chip is a quote.</param>
    /// <returns>The mark.</returns>
    private Control AttachmentGlyph(bool picture, bool quote)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(
                quote
                    ? "M7 7h4v4H7Z M13 7h4v4h-4Z M7 11c0 3 1 4 3 5 M13 11c0 3 1 4 3 5"
                    : picture
                        ? "M4 5h16v14H4Z M8 15l3-3 2 2 3-3 4 4"
                        : "M6 3h8l4 4v14H6Z M14 3v5h5"),
            Stroke = Brushes.Gray,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        return new Viewbox { Width = 13, Height = 13, Child = path };
    }

    /// <summary>Recognizes the image extensions the host can read.</summary>
    /// <param name="path">Reference path.</param>
    /// <returns>Whether the reference is a picture.</returns>
    private bool IsPicture(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";

    /// <summary>One reference held above the prompt.</summary>
    /// <param name="Token">The host-issued token, empty for a failed drop.</param>
    /// <param name="Name">The visible basename.</param>
    /// <param name="SourcePath">The native path retry uses.</param>
    /// <param name="Image">Whether this reference is a picture.</param>
    /// <param name="Error">Why the drop was refused, empty for a ready chip.</param>
    /// <param name="QuoteBody">Selected text for a quote chip, empty for an attachment.</param>
    /// <param name="QuoteSource">File the quoted text came from.</param>
    /// <param name="PasteBody">Long pasted text held out of the box, empty otherwise.</param>
    private sealed record AttachmentChip(string Token, string Name, string SourcePath, bool Image, string Error = "", string QuoteBody = "", string QuoteSource = "", string PasteBody = "");
}
