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
            if (string.IsNullOrWhiteSpace(reference.Ref))
            {
                continue;
            }

            var token = reference.Ref.Trim();
            if (_attachments.Any(chip => chip.Token == token))
            {
                continue;
            }

            var path = reference.Path ?? token;
            var name = Path.GetFileName(path);
            _attachments.Add(new AttachmentChip(token, name.Length == 0 ? token : name, IsPicture(path)));
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
        _attachments.Add(new AttachmentChip(reference, name.Length == 0 ? reference : name, true));
        RebuildAttachments();
    }

    /// <summary>Builds the reference chips above the prompt.</summary>
    private void RebuildAttachments()
    {
        AttachmentStrip.Children.Clear();
        foreach (var attachment in _attachments.ToArray())
        {
            var chip = new Border { Classes = { "attachment-chip" } };
            ToolTip.SetTip(chip, attachment.Token);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(AttachmentGlyph(attachment.Image));
            var name = new TextBlock { Text = attachment.Name, Classes = { "attachment-name" } };
            row.Children.Add(name);
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

        AttachmentStrip.IsVisible = _attachments.Count > 0;
    }

    /// <summary>Serializes attachments ahead of the typed line for the model.</summary>
    /// <param name="line">The visible prompt.</param>
    /// <returns>The submit text.</returns>
    private string ComposeSubmitText(string line) =>
        _attachments.Count == 0
            ? line
            : string.Join(" ", _attachments.Select(chip => chip.Token)) + "\n\n" + line;

    /// <summary>Drops attachments with the draft that owned them.</summary>
    private void ClearAttachments()
    {
        _attachments.Clear();
        RebuildAttachments();
    }

    /// <summary>Builds the small file or picture mark beside a chip name.</summary>
    /// <param name="picture">Whether the reference is a picture.</param>
    /// <returns>The mark.</returns>
    private Control AttachmentGlyph(bool picture)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(picture ? "M4 5h16v14H4Z M8 15l3-3 2 2 3-3 4 4" : "M6 3h8l4 4v14H6Z M14 3v5h5"),
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
    /// <param name="Token">The host-issued token.</param>
    /// <param name="Name">The visible basename.</param>
    /// <param name="Image">Whether this reference is a picture.</param>
    private sealed record AttachmentChip(string Token, string Name, bool Image);
}
